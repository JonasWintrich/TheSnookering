using Godot;

namespace Snookering.Game.Ui;

/// <summary>Player preferences, persisted to user://settings.cfg between sessions.</summary>
public static class GameSettings
{
    private const string Path = "user://settings.cfg";

    public static float MasterVolume = 0.9f;
    public static float SfxVolume = 0.9f;
    public static float AmbienceVolume = 0.6f;
    /// <summary>0 = low, 1 = medium, 2 = high.</summary>
    public static int GraphicsPreset = 2;
    public static float AimSensitivity = 1.0f;

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok)
            return;
        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        AmbienceVolume = (float)cfg.GetValue("audio", "ambience", AmbienceVolume);
        GraphicsPreset = (int)cfg.GetValue("video", "preset", GraphicsPreset);
        AimSensitivity = (float)cfg.GetValue("input", "aim_sensitivity", AimSensitivity);
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ambience", AmbienceVolume);
        cfg.SetValue("video", "preset", GraphicsPreset);
        cfg.SetValue("input", "aim_sensitivity", AimSensitivity);
        cfg.Save(Path);
    }

    /// <summary>Push the volumes onto whichever audio buses exist.</summary>
    public static void ApplyAudio()
    {
        SetBusVolume("Master", MasterVolume);
        SetBusVolume("Sfx", SfxVolume);
        SetBusVolume("Ambience", AmbienceVolume);
    }

    private static void SetBusVolume(string bus, float linear)
    {
        for (var i = 0; i < AudioServer.BusCount; i++)
        {
            if (AudioServer.GetBusName(i) != bus)
                continue;
            AudioServer.SetBusVolumeDb(i, linear <= 0.001f ? -60f : Mathf.LinearToDb(linear));
            return;
        }
    }

    /// <summary>Apply the graphics preset to a scene's WorldEnvironment.</summary>
    public static void ApplyGraphics(Godot.Environment? env)
    {
        if (env is null)
            return;
        var high = GraphicsPreset >= 2;
        var medium = GraphicsPreset >= 1;
        env.SdfgiEnabled = high;
        env.VolumetricFogEnabled = medium;
        env.SsilEnabled = high;
        env.SsrEnabled = medium;
        env.SsaoEnabled = true; // the contact shadows under the balls stay at every level
    }
}
