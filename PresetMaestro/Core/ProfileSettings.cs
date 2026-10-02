namespace PresetMaestro.Core;

public sealed class ProfileSettings
{
    public System.Text.Json.JsonElement? FractalIndex { get; set; }
    public DeviceModel DeviceModel { get; set; } = DeviceModel.FM9;
    public string? DeviceName { get; set; }
    public int MidiChannel { get; set; } = 1;
    public int DisplayOffset { get; set; }
    public int MaxDisplayedPreset { get; set; } = 511;
    public int SceneCc { get; set; } = 34;
    public Dictionary<int, string> PresetNameCache { get; set; } = [];
    public Dictionary<string, Dictionary<int, SceneCacheEntry>> SceneNameCaches { get; set; } = [];

    public static ProfileSettings From(AppSettings settings) => new()
    {
        FractalIndex = settings.FractalIndex,
        DeviceModel = settings.DeviceModel,
        DeviceName = settings.DeviceName,
        MidiChannel = settings.MidiChannel,
        DisplayOffset = settings.DisplayOffset,
        MaxDisplayedPreset = settings.MaxDisplayedPreset,
        SceneCc = settings.SceneCc,
        PresetNameCache = settings.PresetNameCache,
        SceneNameCaches = settings.SceneNameCaches,
    };

    public void ApplyTo(AppSettings settings)
    {
        settings.FractalIndex = FractalIndex;
        settings.DeviceModel = DeviceModel;
        settings.DeviceName = DeviceName;
        settings.MidiChannel = MidiChannel;
        settings.DisplayOffset = DisplayOffset;
        settings.MaxDisplayedPreset = MaxDisplayedPreset;
        settings.SceneCc = SceneCc;
        settings.PresetNameCache = PresetNameCache;
        settings.SceneNameCaches = SceneNameCaches;
    }
}
