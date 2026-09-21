namespace PresetMaestro.Core;

public class AppSettings
{
    public string ActiveProfile { get; set; } = "Default";
    public List<string> Profiles { get; set; } = [];
    public string MidiInputPort { get; set; } = string.Empty;
    public string MidiOutputPort { get; set; } = string.Empty;
    public List<string> ThruInputPorts { get; set; } = [];
    public int MidiChannel { get; set; } = 1;
    public int DisplayOffset { get; set; }
    public int MaxDisplayedPreset { get; set; } = 511;
    public bool AutoSend { get; set; }
    public int AutoSendDelayMs { get; set; } = 35;
    public bool KeyboardEntryEnabled { get; set; } = true;
    public bool MidiEntryEnabled { get; set; } = true;
    public bool DebugMode { get; set; }
    public int SceneCc { get; set; } = 34; // Scene Select CC# (must match device's MIDI/Remote setup)
    public string Theme { get; set; } = "Dark";
    public List<string> CategoryOrder { get; set; } = [];
    public Dictionary<int, string> NoteMap { get; set; } = DefaultNoteMap();
    public Dictionary<int, string> PresetNameCache { get; set; } = [];
    // Scope names to a port pair; slots are always MIDI 0-511, independent of display offset.
    public Dictionary<string, Dictionary<int, SceneCacheEntry>> SceneNameCaches { get; set; } = [];

    public static Dictionary<int, string> DefaultNoteMap() => new()
    {
        { 36, "1" }, { 37, "2" }, { 38, "3" }, { 39, "4" }, { 40, "5" },
        { 41, "6" }, { 42, "7" }, { 43, "8" }, { 44, "9" }, { 45, "0" },
        { 46, "SEND" }, { 47, "CLEAR" }, { 48, "NEXT" }, { 49, "PREV" }, { 50, "LAST" },
    };
}
