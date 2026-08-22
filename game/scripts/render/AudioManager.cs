using Godot;
using Snookering.Core.Physics;

namespace Snookering.Game.Render;

/// <summary>
/// Event-driven shot audio: the sim's event log carries impact speeds, so volume
/// and pitch scale with how hard things hit. A small pool of 3D players lets
/// overlapping impacts (breaks!) ring simultaneously.
/// </summary>
public partial class AudioManager : Node3D
{
    private const int PoolSize = 12;

    private AudioStreamPlayer3D[] _players = null!;
    private int _next;
    private AudioStream _click = null!;
    private AudioStream _cushion = null!;
    private AudioStream _pocket = null!;

    public override void _Ready()
    {
        _click = GD.Load<AudioStream>("res://assets/audio/click.wav");
        _cushion = GD.Load<AudioStream>("res://assets/audio/cushion.wav");
        _pocket = GD.Load<AudioStream>("res://assets/audio/pocket.wav");

        _players = new AudioStreamPlayer3D[PoolSize];
        for (var i = 0; i < PoolSize; i++)
        {
            _players[i] = new AudioStreamPlayer3D
            {
                MaxDistance = 12f,
                UnitSize = 2.5f,
            };
            AddChild(_players[i]);
        }
    }

    /// <summary>Play the sound for one sim event at a world position.</summary>
    public void PlayEvent(in SimEvent e, Vector3 worldPos)
    {
        var (stream, reference) = e.Type switch
        {
            SimEventType.BallBall => (_click, 6.0),
            SimEventType.CueStrike => (_click, 8.0),
            SimEventType.Cushion => (_cushion, 5.0),
            SimEventType.Pocketed => (_pocket, 3.0),
            _ => ((AudioStream?)null, 1.0),
        };
        if (stream is null)
            return;

        // Impact speed → loudness (quiet touches stay nearly silent) and a touch of pitch.
        var intensity = Mathf.Clamp((float)(e.Speed / reference), 0.02f, 1f);
        var player = _players[_next];
        _next = (_next + 1) % PoolSize;

        player.Stop();
        player.Stream = stream;
        player.GlobalPosition = worldPos;
        player.VolumeDb = Mathf.LinearToDb(Mathf.Pow(intensity, 1.4f));
        player.PitchScale = 0.92f + 0.16f * (float)GD.RandRange(0.0, 1.0) + 0.1f * intensity;
        player.Play();
    }
}
