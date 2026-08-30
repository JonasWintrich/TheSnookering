using System.Collections.Generic;
using Godot;
using Snookering.Core.Physics;
using Snookering.Core.Tables;

namespace Snookering.Game.Render;

/// <summary>
/// Turns the simulation's event list into sound.
///
/// Impacts are baked at three reference speeds per family, because a harder hit
/// is physically *brighter* (shorter Hertzian contact), not merely louder. Two
/// voices crossfade between neighbouring tiers so the change is continuous, and
/// several variants per tier stop a break sounding like a machine gun.
/// </summary>
public partial class AudioManager : Node3D
{
    private const int VoiceCount = 28;
    private const int RollVoices = 4;

    /// <summary>
    /// Per-family headroom. A break fires a dozen clicks inside a few hundred
    /// milliseconds, so they need the most room; the cue strike is a single
    /// close sound and can sit forward.
    /// </summary>
    private static float FamilyGainDb(string family) => family switch
    {
        "click" => -7f,
        "cue" => -1f,
        "cushion_rail" or "cushion_jaw" => -4f,
        _ => -4f,
    };

    private static readonly float[] ClickTiers = { 0.6f, 2.2f, 6.0f };
    private static readonly float[] CueTiers = { 1.5f, 4.0f, 7.5f };
    private static readonly float[] CushionTiers = { 0.8f, 2.5f, 5.5f };

    private AudioStreamPlayer3D[] _voices = null!;
    private int _next;

    private readonly Dictionary<string, AudioStream[][]> _families = new();
    private readonly Dictionary<string, int> _lastVariant = new();

    private AudioStreamPlayer3D[] _roll = null!;
    private float[] _rollGain = null!;
    private AudioStreamPlayer? _ambience;

    private TableSpec? _table;
    private HashSet<short> _jawFeatures = new();

    public override void _Ready()
    {
        AudioBuses.Ensure();
        Ui.GameSettings.ApplyAudio();

        LoadFamily("click", 3, 6);
        LoadFamily("cue", 3, 4);
        LoadFamily("cushion_rail", 3, 4);
        LoadFamily("cushion_jaw", 3, 4);
        LoadFamily("pocket_catch", 1, 4);
        LoadFamily("pocket_net", 1, 3);
        LoadFamily("pocket_return", 1, 3);

        _voices = new AudioStreamPlayer3D[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _voices[i] = new AudioStreamPlayer3D
            {
                Bus = AudioBuses.Impacts,
                UnitSize = 1.6f,
                MaxDistance = 24f,
                PanningStrength = 0.7f,
            };
            AddChild(_voices[i]);
        }

        _roll = new AudioStreamPlayer3D[RollVoices];
        _rollGain = new float[RollVoices];
        var rollStream = Loop("res://assets/audio/roll_loop.wav");
        for (var i = 0; i < RollVoices; i++)
        {
            _roll[i] = new AudioStreamPlayer3D
            {
                Bus = AudioBuses.Roll,
                Stream = rollStream,
                UnitSize = 1.2f,
                MaxDistance = 18f,
                VolumeDb = -60f,
            };
            AddChild(_roll[i]);
        }

        var ambienceStream = Loop("res://assets/audio/ambience.wav");
        if (ambienceStream is not null)
        {
            _ambience = new AudioStreamPlayer { Bus = AudioBuses.Ambience, Stream = ambienceStream };
            AddChild(_ambience);
            _ambience.Play();
        }
    }

    /// <summary>Cushion sounds differ at the jaws, so the table geometry is needed.</summary>
    public void SetTable(TableSpec table)
    {
        _table = table;
        _jawFeatures = new HashSet<short>();
        var reach = table.Physics.R * 2.0 + 0.06;

        foreach (var seg in table.Cushions)
        {
            var mid = (seg.A + seg.B) * 0.5;
            foreach (var pocket in table.Pockets)
                if ((mid - pocket.FallCenter).Length < pocket.FallRadius + reach)
                    _jawFeatures.Add(seg.FeatureId);
        }
        foreach (var arc in table.Jaws)
            _jawFeatures.Add(arc.FeatureId); // arcs only ever exist at pockets
    }

    // ------------------------------------------------------------------ loading

    private void LoadFamily(string name, int tiers, int variants)
    {
        var byTier = new AudioStream[tiers][];
        for (var t = 0; t < tiers; t++)
        {
            var list = new List<AudioStream>();
            for (var v = 0; v < variants; v++)
            {
                var path = tiers == 1
                    ? $"res://assets/audio/{name}_{v}.wav"
                    : $"res://assets/audio/{name}_{t}_{v}.wav";
                if (ResourceLoader.Exists(path))
                    list.Add(GD.Load<AudioStream>(path));
            }
            byTier[t] = list.ToArray();
        }
        _families[name] = byTier;
    }

    /// <summary>
    /// Load a looping bed. The loop points come from the import settings (raw PCM,
    /// loop_mode=1) rather than being computed here: Godot's default .wav import is
    /// lossy QOA, whose Data is compressed bytes, so deriving a loop end from the
    /// byte length pointed into the middle of compressed data and played as static.
    /// </summary>
    private static AudioStream? Loop(string path)
    {
        if (!ResourceLoader.Exists(path))
            return null;
        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Disabled } wav)
        {
            var copy = (AudioStreamWav)wav.Duplicate();
            copy.LoopMode = AudioStreamWav.LoopModeEnum.Forward; // fallback only
            return copy;
        }
        return stream;
    }

    // ------------------------------------------------------------------ events

    public void PlayEvent(in SimEvent e, Vector3 worldPos)
    {
        switch (e.Type)
        {
            case SimEventType.CueStrike:
                PlayTiered("cue", CueTiers, (float)e.Speed, worldPos, in e);
                break;
            case SimEventType.BallBall:
                PlayTiered("click", ClickTiers, (float)e.Speed, worldPos, in e);
                break;
            case SimEventType.Cushion:
                PlayTiered(_jawFeatures.Contains(e.FeatureId) ? "cushion_jaw" : "cushion_rail",
                    CushionTiers, (float)e.Speed, CushionPos(e, worldPos), in e);
                break;
            case SimEventType.Pocketed:
                PlayPocket(in e, worldPos);
                break;
        }
    }

    private Vector3 CushionPos(in SimEvent e, Vector3 fallback)
    {
        if (_table is null)
            return fallback;
        foreach (var seg in _table.Cushions)
            if (seg.FeatureId == e.FeatureId)
                return SimWorld.ToWorld((seg.A + seg.B) * 0.5, 0.02f);
        foreach (var arc in _table.Jaws)
            if (arc.FeatureId == e.FeatureId)
                return SimWorld.ToWorld(arc.Center, 0.02f);
        return fallback;
    }

    private void PlayPocket(in SimEvent e, Vector3 fallback)
    {
        var pos = fallback;
        if (_table is not null)
            foreach (var pocket in _table.Pockets)
                if (pocket.Id == e.FeatureId)
                    pos = SimWorld.ToWorld(pocket.FallCenter, -0.13f);

        // A ball always drops at roughly the same speed however hard it arrived,
        // so the catch must not scale with entry speed the way an impact does.
        var snooker = _table?.Snooker is not null;
        Play(snooker ? "pocket_net" : "pocket_catch", 0, in e, pos, 0f, 1f);
        if (!snooker)
            PlayDelayed("pocket_return", pos, 0.65f);
    }

    private async void PlayDelayed(string family, Vector3 pos, float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        if (!IsInsideTree())
            return;
        var e = new SimEvent(0, SimEventType.Pocketed, 0, 0, 0, 1.0);
        Play(family, 0, in e, pos, -4f, 1f);
    }

    /// <summary>Pick the two neighbouring speed tiers and crossfade between them.</summary>
    private void PlayTiered(string family, float[] tiers, float speed, Vector3 pos, in SimEvent e)
    {
        if (!_families.TryGetValue(family, out var byTier) || byTier.Length == 0)
            return;

        speed = Mathf.Max(speed, 0.02f);
        var x = Mathf.Log(speed / tiers[0]) / Mathf.Log(tiers[1] / tiers[0]);
        var lo = Mathf.Clamp((int)Mathf.Floor(x), 0, tiers.Length - 2);
        var f = Mathf.Clamp(x - lo, 0f, 1f);

        // Radiated energy grows roughly as v^0.9 in amplitude; the floor keeps a
        // feather-touch kiss audible instead of silent.
        var db = Mathf.Clamp(20f * (float)System.Math.Log10(speed / tiers[lo]) * 0.9f, -30f, 2f)
                 + FamilyGainDb(family);

        if (1f - f > 0.02f)
            Play(family, lo, in e, pos, db + Mathf.LinearToDb(Mathf.Sqrt(1f - f)), 1f);
        if (f > 0.02f)
            Play(family, lo + 1, in e, pos, db + Mathf.LinearToDb(Mathf.Sqrt(f)), 1f);
    }

    private void Play(string family, int tier, in SimEvent e, Vector3 pos, float volumeDb, float pitch)
    {
        if (!_families.TryGetValue(family, out var byTier) || tier >= byTier.Length)
            return;
        var variants = byTier[tier];
        if (variants.Length == 0)
            return;

        // Deterministic per event, so a replayed shot sounds identical, with a
        // guard so the same variant never fires twice in a row.
        var hash = Hash(in e, (uint)(family.GetHashCode() ^ tier));
        var index = (int)(hash % (uint)variants.Length);
        var key = $"{family}{tier}";
        if (variants.Length > 1 && _lastVariant.TryGetValue(key, out var last) && last == index)
            index = (index + 1) % variants.Length;
        _lastVariant[key] = index;

        var voice = TakeVoice();
        voice.Stream = variants[index];
        voice.GlobalPosition = pos;
        voice.VolumeDb = volumeDb;
        // Small jitter only: a big pitch shift would transpose the table body too,
        // and a table does not change size when you hit the ball harder.
        voice.PitchScale = pitch * (1f + 0.03f * ((hash >> 16 & 0xFF) / 127.5f - 1f));
        voice.Play();
    }

    private AudioStreamPlayer3D TakeVoice()
    {
        for (var i = 0; i < VoiceCount; i++)
        {
            var candidate = _voices[_next];
            _next = (_next + 1) % VoiceCount;
            if (!candidate.Playing)
                return candidate;
        }
        var steal = _voices[_next];
        _next = (_next + 1) % VoiceCount;
        steal.Stop();
        return steal;
    }

    private static uint Hash(in SimEvent e, uint salt)
    {
        var h = 2166136261u;
        h = (h ^ e.BallA) * 16777619u;
        h = (h ^ e.BallB) * 16777619u;
        h = (h ^ (uint)(ushort)e.FeatureId) * 16777619u;
        h = (h ^ (uint)(long)(e.Time * 8192.0)) * 16777619u;
        h = (h ^ salt) * 16777619u;
        return h ^ (h >> 15);
    }

    // ------------------------------------------------------------------ rolling

    /// <summary>
    /// Balls rolling on cloth. The table used to be silent between impacts, which
    /// is a large part of why shots felt lifeless. Speeds come from the trajectory
    /// frames, and the loop is resampled by speed — physically right, because the
    /// nap passes under the ball proportionally faster.
    /// </summary>
    public void UpdateRolling(IReadOnlyList<(Vector3 Pos, float Speed)> movers, float dt)
    {
        for (var i = 0; i < RollVoices; i++)
        {
            var target = 0f;
            if (i < movers.Count)
            {
                var speed = movers[i].Speed;
                target = Mathf.Pow(Mathf.Clamp(speed / 2.0f, 0f, 1.3f), 1.25f);
                _roll[i].GlobalPosition = movers[i].Pos;
                _roll[i].PitchScale = Mathf.Clamp(speed, 0.45f, 3.0f);
                if (!_roll[i].Playing)
                    _roll[i].Play((float)GD.RandRange(0.0, 2.0));
            }

            // Fade in quickly, out slowly, then gate hard — many balls creep to a
            // halt at the end of a shot and would otherwise leave a drone.
            var rate = target > _rollGain[i] ? 1f / 0.04f : 1f / 0.14f;
            _rollGain[i] = Mathf.MoveToward(_rollGain[i], target, rate * dt);
            if (_rollGain[i] < 0.02f)
            {
                _rollGain[i] = 0f;
                if (_roll[i].Playing)
                    _roll[i].Stop();
            }
            _roll[i].VolumeDb = _rollGain[i] <= 0f ? -60f : Mathf.LinearToDb(_rollGain[i]);
        }
    }

    public void StopRolling()
    {
        for (var i = 0; i < RollVoices; i++)
        {
            _rollGain[i] = 0f;
            _roll[i].VolumeDb = -60f;
            _roll[i].Stop();
        }
    }
}
