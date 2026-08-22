using System.Globalization;
using Godot;

namespace Snookering.Game.Debugging;

/// <summary>
/// CLI harness for headless-driven development. Passed after "--" on the command line:
///   --screenshot &lt;path&gt;   save a viewport PNG (after --frame N frames, default 30) and quit
///   --frame &lt;N&gt;           frames to wait before capturing (lets TAA/GI settle)
///   --dump-state &lt;path&gt;   write ball positions as JSON and quit
///   --quit-after &lt;N&gt;      quit after N frames regardless
/// </summary>
public partial class DebugAutoload : Node
{
    private string? _screenshotPath;
    private string? _dumpStatePath;
    private int _waitFrames = 30;
    private int _quitAfterFrames = -1;
    private int _frame;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--screenshot" when i + 1 < args.Length:
                    _screenshotPath = args[++i];
                    break;
                case "--frame" when i + 1 < args.Length:
                    _waitFrames = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--dump-state" when i + 1 < args.Length:
                    _dumpStatePath = args[++i];
                    break;
                case "--quit-after" when i + 1 < args.Length:
                    _quitAfterFrames = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
            }
        }

        if (_screenshotPath is null && _dumpStatePath is null && _quitAfterFrames < 0)
            SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _frame++;

        if (_frame == _waitFrames)
        {
            if (_screenshotPath is not null)
                CaptureScreenshot(_screenshotPath);
            if (_dumpStatePath is not null)
                DumpState(_dumpStatePath);
            if (_quitAfterFrames < 0)
                GetTree().Quit();
        }

        if (_quitAfterFrames >= 0 && _frame >= _quitAfterFrames)
            GetTree().Quit();
    }

    private void CaptureScreenshot(string path)
    {
        var image = GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        GD.Print(err == Error.Ok
            ? $"[debug] screenshot saved: {path}"
            : $"[debug] screenshot FAILED ({err}): {path}");
    }

    private void DumpState(string path)
    {
        var balls = new Godot.Collections.Array();
        foreach (var node in GetTree().GetNodesInGroup("balls"))
        {
            if (node is Node3D b)
            {
                balls.Add(new Godot.Collections.Dictionary
                {
                    ["name"] = b.Name.ToString(),
                    ["x"] = b.GlobalPosition.X,
                    ["y"] = b.GlobalPosition.Y,
                    ["z"] = b.GlobalPosition.Z,
                });
            }
        }
        var json = Json.Stringify(new Godot.Collections.Dictionary { ["balls"] = balls }, "  ");
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(json);
        GD.Print($"[debug] state dumped: {path}");
    }
}
