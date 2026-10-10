namespace PresetMaestro.Midi;

// The UI and device protocols use the same contract on Windows and macOS.
// MidiManager supplies common routing over the backend selected at compile time.
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
    Task<MidiPorts> DiscoverPortsAsync() => Task.FromResult(new MidiPorts(GetInputPortNames(), GetOutputPortNames()));

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
