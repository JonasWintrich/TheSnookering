using System;
using Godot;

namespace Snookering.Game.Render;

/// <summary>
/// Ambient character: loops a base animation (idle or sitting), occasionally
/// plays a random one-shot action so the room feels alive, and claps when
/// something pot-worthy happens (poked via <see cref="React"/>).
/// </summary>
public partial class NpcView : Node3D
{
    private AnimationPlayer? _anim;
    private string _baseClip = "";
    private string _reactClip = "";
    private string[] _fidgetClips = Array.Empty<string>();
    private double _nextFidget;
    private readonly Random _rng = new();

    public static NpcView Create(string glbPath, Vector3 pos, float yawDeg, string basePrefix, float scale = 1f)
    {
        var npc = new NpcView
        {
            Name = $"Npc_{basePrefix}_{pos.X:F0}{pos.Z:F0}",
            Position = pos,
            RotationDegrees = new Vector3(0f, yawDeg, 0f),
            Scale = Vector3.One * scale,
        };
        if (ResourceLoader.Exists(glbPath))
        {
            var model = GD.Load<PackedScene>(glbPath).Instantiate<Node3D>();
            npc.AddChild(model);
            npc._anim = model.FindChild("AnimationPlayer", recursive: true, owned: false) as AnimationPlayer
                        ?? npc.FindChild("AnimationPlayer", recursive: true, owned: false) as AnimationPlayer;
            npc.PickClips(basePrefix);

        }
        return npc;
    }

    public override void _Ready()
    {
        AddToGroup("npcs");
        _nextFidget = 6.0 + _rng.NextDouble() * 10.0;
    }

    private void PickClips(string basePrefix)
    {
        if (_anim is null)
            return;

        foreach (string clip in _anim.GetAnimationList())
        {
            var lower = clip.ToLowerInvariant();
            if (lower.Contains(basePrefix.ToLowerInvariant()))
                _baseClip = clip;
            else if (lower.Contains("clapping"))
                _reactClip = clip;
        }

        // Gentle fidgets only — nothing that would look unhinged in a lounge.
        var fidgets = new System.Collections.Generic.List<string>();
        foreach (string clip in _anim.GetAnimationList())
        {
            var lower = clip.ToLowerInvariant();
            if (lower.Contains("standing") || lower.Contains("idle"))
                if (clip != _baseClip)
                    fidgets.Add(clip);
        }
        _fidgetClips = fidgets.ToArray();
    }

    public override void _Process(double delta)
    {
        if (_anim is null || _baseClip.Length == 0)
            return;

        _nextFidget -= delta;
        if (_nextFidget <= 0.0 && _fidgetClips.Length > 0)
        {
            _anim.Play(_fidgetClips[_rng.Next(_fidgetClips.Length)]);
            _nextFidget = 8.0 + _rng.NextDouble() * 12.0;
        }

        // Base loop: whenever nothing is playing, return to the base clip.
        if (!_anim.IsPlaying())
            _anim.Play(_baseClip);
    }

    /// <summary>A nice pot happened — applaud.</summary>
    public void React()
    {
        if (_anim is not null && _reactClip.Length > 0)
            _anim.Play(_reactClip);
    }
}
