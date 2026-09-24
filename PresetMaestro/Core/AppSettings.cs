namespace PresetMaestro.Core;

public class AppSettings
{
    public string ActiveProfile { get; set; } = "Default";
    public List<string> Profiles { get; set; } = [];
    public string MidiInputPort { get; set; } = string.Empty;
    public string MidiOutputPort { get; set; } = string.Empty;
    public List<string> ThruInputPorts { get; set; } = [];
    // Per-port note-input channel filter (0 = Omni), keyed by MIDI port name (main input or a thru port).
    // Independent of MidiChannel, which is only the transmit channel for outgoing Bank/PC/CC.
    public Dictionary<string, int> NoteInputChannels { get; set; } = [];
    public DeviceModel DeviceModel { get; set; } = DeviceModel.FM9;
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
    public Dictionary<int, string> MidiNoteMap { get; set; } = DefaultMidiNoteMap();
    public Dictionary<int, string> PresetNameCache { get; set; } = [];
    // Scope names to a port pair; slots are always MIDI 0-511, independent of display offset.
    public Dictionary<string, Dictionary<int, SceneCacheEntry>> SceneNameCaches { get; set; } = [];

    public static Dictionary<int, string> DefaultMidiNoteMap() => new()
    {
        { 36, "1" }, { 37, "2" }, { 38, "3" }, { 39, "4" }, { 40, "5" },
        { 41, "6" }, { 42, "7" }, { 43, "8" }, { 44, "9" }, { 45, "0" },
        { 46, "SEND" }, { 47, "CLEAR" }, { 48, "NEXT" }, { 49, "PREV" }, { 50, "LAST" },
    };
}
