namespace PresetMaestro.Core;

public sealed class ProfileSettings
{
    public DeviceModel DeviceModel { get; set; } = DeviceModel.FM9;
    public int MidiChannel { get; set; } = 1;
    public int DisplayOffset { get; set; }
    public int MaxDisplayedPreset { get; set; } = 511;
    public int SceneCc { get; set; } = 34;
    public List<string> CategoryOrder { get; set; } = [];
    public Dictionary<int, string> PresetNameCache { get; set; } = [];
    public Dictionary<string, Dictionary<int, SceneCacheEntry>> SceneNameCaches { get; set; } = [];

    public static ProfileSettings From(AppSettings settings) => new()
    {
        DeviceModel = settings.DeviceModel,
        MidiChannel = settings.MidiChannel,
        DisplayOffset = settings.DisplayOffset,
        MaxDisplayedPreset = settings.MaxDisplayedPreset,
        SceneCc = settings.SceneCc,
        CategoryOrder = settings.CategoryOrder,
        PresetNameCache = settings.PresetNameCache,
        SceneNameCaches = settings.SceneNameCaches,
    };

    public void ApplyTo(AppSettings settings)
    {
        settings.DeviceModel = DeviceModel;
        settings.MidiChannel = MidiChannel;
        settings.DisplayOffset = DisplayOffset;
        settings.MaxDisplayedPreset = MaxDisplayedPreset;
        settings.SceneCc = SceneCc;
        settings.CategoryOrder = CategoryOrder;
        settings.PresetNameCache = PresetNameCache;
        settings.SceneNameCaches = SceneNameCaches;
    }
}
