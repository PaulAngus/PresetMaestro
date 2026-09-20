namespace PresetMaestro.Midi;

// Abstracts the MIDI backend so a future platform-specific implementation (e.g. CoreMIDI for macOS)
// can be swapped in without touching UI code. MidiManager is the only implementation today (Windows/NAudio).
public interface IMidiManager : IDisposable
{
    event EventHandler<string>? LogMessage;
    event EventHandler<NoteOnEventArgs>? NoteOnReceived;
    event EventHandler<byte[]>? SysexMessageReceived;
    event EventHandler<int>? PresetChangeReceived;

    bool InputOpen { get; }
    bool OutputOpen { get; }
    IReadOnlyCollection<string> ThruInputPorts { get; }

    IReadOnlyList<string> GetInputPortNames();
    IReadOnlyList<string> GetOutputPortNames();

    bool OpenInput(string portName, out string? errorMessage);
    bool OpenOutput(string portName, out string? errorMessage);
    void CloseInput();
    void CloseOutput();

    bool OpenThruInput(string portName, out string? errorMessage);
    void CloseThruInput(string portName);
    void CloseAllThruInputs();

    bool SendBankAndPC(int bank, int pc, int midiChannel);
    bool SendFavorite(int bank, int pc, int scene, int sceneCc, int midiChannel);
    bool SendSysEx(byte[] frame);
    bool SendScene(int scene, int sceneCc, int midiChannel);
}
