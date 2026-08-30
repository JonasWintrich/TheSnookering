using Godot;

namespace Snookering.Game.Render;

/// <summary>
/// The mixer, built at runtime because this project creates everything in code.
///
/// Dry impacts played straight into Master were the single biggest reason the
/// audio felt fake: real clicks reach you through a room. The lounge is roughly
/// 12 x 10 x 3.4 m, which gives a mean free path of ~4.2 m (12 ms between
/// reflections) and an RT60 near 0.75 s — those numbers drive the settings here.
/// </summary>
public static class AudioBuses
{
    public const string Sfx = "Sfx";
    public const string Impacts = "Impacts";
    public const string Roll = "Roll";
    public const string Ambience = "Ambience";

    public static AudioEffectReverb? ImpactReverb { get; private set; }

    private static int Find(string name)
    {
        for (var i = 0; i < AudioServer.BusCount; i++)
            if (AudioServer.GetBusName(i) == name)
                return i;
        return -1;
    }

    private static int Add(string name, string sendTo, float volumeDb = 0f)
    {
        var existing = Find(name);
        if (existing >= 0)
            return existing;
        AudioServer.AddBus();
        var idx = AudioServer.BusCount - 1;
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, sendTo);
        AudioServer.SetBusVolumeDb(idx, volumeDb);
        return idx;
    }

    public static void Ensure()
    {
        if (Find(Sfx) >= 0)
            return; // already built (survives re-racks and scene reloads)

        // A break can stack a dozen impacts in a few hundred ms.
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -1.0f });

        var sfx = Add(Sfx, "Master");
        AudioServer.AddBusEffect(sfx, new AudioEffectCompressor
        {
            // Gentle glue only. The old +3 dB make-up drove the limiter and was
            // part of why a break turned to mush.
            Threshold = -10f,
            Ratio = 2.5f,
            AttackUs = 2500f,
            ReleaseMs = 120f,
            Gain = 0f,
        });

        var impacts = Add(Impacts, Sfx);
        // Two discrete early reflections — the ceiling above the table and the
        // nearest wall — are what make it read as *this* room, not generic reverb.
        AudioServer.AddBusEffect(impacts, new AudioEffectDelay
        {
            Dry = 1f,
            FeedbackActive = false,
            Tap1Active = true, Tap1DelayMs = 11f, Tap1LevelDb = -11f, Tap1Pan = -0.4f,
            Tap2Active = true, Tap2DelayMs = 23f, Tap2LevelDb = -15f, Tap2Pan = 0.5f,
        });
        ImpactReverb = new AudioEffectReverb
        {
            RoomSize = 0.62f,
            Damping = 0.42f,
            Spread = 1.0f,
            Hipass = 0.06f,
            PredelayMsec = 12f,
            PredelayFeedback = 0.35f,
            Dry = 1f,
            Wet = 0.24f,
        };
        AudioServer.AddBusEffect(impacts, ImpactReverb);

        var roll = Add(Roll, Sfx, -6f);
        AudioServer.AddBusEffect(roll, new AudioEffectLowPassFilter { CutoffHz = 7500f, Resonance = 0.4f });

        Add(Ambience, "Master", 0f); // the bed is already quiet at source
    }

    /// <summary>
    /// Critical distance in this room is only ~1.3 m, so a fixed wet mix is wrong
    /// for both the close aim view and the wide playback orbit. Track the listener.
    /// </summary>
    public static void SetListenerDistance(float metres)
    {
        if (ImpactReverb is not null)
            ImpactReverb.Wet = Mathf.Clamp(0.14f + 0.055f * metres, 0.14f, 0.42f);
    }
}
