using System;
using Godot;

namespace Snookering.Game.Net;

/// <summary>
/// Thin wrapper over Godot's ENet peer: host, join, leave, and the connection
/// events the rest of the game reacts to.
///
/// Deliberately knows nothing about billiards. All it does is get two processes
/// talking; the match itself stays in sync by re-simulating the same shot struct
/// on both sides, which is why nothing here needs to stream game state.
/// </summary>
public partial class NetworkManager : Node
{
    public const int DefaultPort = 24555;

    /// <summary>Bumped whenever the sim, rules or wire format change. Two peers on
    /// different builds would desync in confusing ways, so they are refused instead.</summary>
    public const int ProtocolVersion = 1;

    public enum Role { Offline, Host, Guest }

    public Role Current { get; private set; } = Role.Offline;
    public bool IsOnline => Current != Role.Offline;

    /// <summary>Host plays as player 1, guest as player 2.</summary>
    public int LocalSeat => Current == Role.Guest ? 1 : 0;

    public bool OpponentPresent { get; private set; }

    public event Action? OpponentJoined;
    public event Action<string>? Disconnected;
    public event Action<string>? Failed;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public string Host(int port = DefaultPort)
    {
        Leave();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, 1);
        if (err != Error.Ok)
            return $"Could not open port {port} ({err}).";

        Multiplayer.MultiplayerPeer = peer;
        Current = Role.Host;
        GD.Print($"[net] hosting on port {port}");
        return "";
    }

    /// <summary>
    /// Join a host. Accepts "1.2.3.4" or "1.2.3.4:56789" — relay and tunnel
    /// services hand out an arbitrary port, so pasting the address they give you
    /// has to just work.
    /// </summary>
    public string Join(string address, int port = DefaultPort)
    {
        Leave();
        if (string.IsNullOrWhiteSpace(address))
            return "Enter the host's address.";

        var (host, resolvedPort) = SplitHostPort(address, port);
        port = resolvedPort;

        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(host, port);
        if (err != Error.Ok)
            return $"Could not reach {address}:{port} ({err}).";

        Multiplayer.MultiplayerPeer = peer;
        Current = Role.Guest;
        GD.Print($"[net] connecting to {host}:{port}");
        return "";
    }

    /// <summary>
    /// Split a pasted address into host and port. Handles "1.2.3.4",
    /// "1.2.3.4:56789", "tunnel.example.com:56789", "[::1]:56789" and a bare
    /// "::1" — a tunnel's address is copied and pasted by hand, so the parsing
    /// has to tolerate whichever shape the provider prints.
    /// </summary>
    public static (string Host, int Port) SplitHostPort(string address, int fallbackPort)
    {
        var text = address.Trim();

        if (text.StartsWith('['))                       // [::1] or [::1]:port
        {
            var close = text.IndexOf(']');
            if (close > 0)
            {
                var inner = text[1..close];
                var rest = text[(close + 1)..];
                if (rest.StartsWith(':') && int.TryParse(rest[1..], out var bracketPort))
                    return (inner, bracketPort);
                return (inner, fallbackPort);
            }
        }

        var colon = text.LastIndexOf(':');
        if (colon <= 0)
            return (text, fallbackPort);

        // More than one colon and no brackets means a bare IPv6 literal, which
        // carries no port however tempting the last colon looks.
        if (text.IndexOf(':') != colon)
            return (text, fallbackPort);

        return int.TryParse(text[(colon + 1)..], out var explicitPort)
            ? (text[..colon], explicitPort)
            : (text, fallbackPort);
    }

    public void Leave()
    {
        if (Multiplayer.MultiplayerPeer is not null and not OfflineMultiplayerPeer)
        {
            Multiplayer.MultiplayerPeer.Close();
            Multiplayer.MultiplayerPeer = null;
        }
        Current = Role.Offline;
        OpponentPresent = false;
    }

    /// <summary>Local addresses to read out to the other player.</summary>
    public static string[] LocalAddresses()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var address in IP.GetLocalAddresses())
        {
            var s = address.ToString();
            if (s.Contains(':') || s.StartsWith("127.")) // skip IPv6 and loopback
                continue;
            list.Add(s);
        }
        return list.ToArray();
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"[net] peer {id} connected");
        OpponentPresent = true;
        OpponentJoined?.Invoke();
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"[net] peer {id} disconnected");
        OpponentPresent = false;
        Disconnected?.Invoke("The other player left the match.");
    }

    private void OnConnectedToServer()
    {
        GD.Print("[net] connected to host");
        OpponentPresent = true;
        OpponentJoined?.Invoke();
    }

    private void OnConnectionFailed()
    {
        GD.PrintErr("[net] connection failed");
        Leave();
        Failed?.Invoke("Could not connect — check the address, and that the host's port is reachable.");
    }

    private void OnServerDisconnected()
    {
        GD.Print("[net] host closed the match");
        Leave();
        Disconnected?.Invoke("The host closed the match.");
    }
}
