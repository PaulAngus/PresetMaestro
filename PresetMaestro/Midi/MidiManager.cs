using PresetMaestro.Platforms;
using System.Collections.Concurrent;

namespace PresetMaestro.Midi;

public sealed class MidiManager : IMidiManager
{
    private IMidiInputPort? _midiIn;
    private string? _mainInputPortName;
    private IMidiOutputPort? _midiOut;
    private string? _outputPortName;
    private bool _disposed;

    // Extra input ports whose raw messages are forwarded straight to _midiOut (MIDI thru/merge).
    private readonly ConcurrentDictionary<string, IMidiInputPort> _thruInputs = new(StringComparer.OrdinalIgnoreCase);
    // Guards all writes to _midiOut, since preset sends and thru forwarding can happen from different threads.
    private readonly object _outLock = new();
    private readonly Func<IReadOnlyList<(string Id, string Name)>> _inputDevices;
    private readonly Func<string, IMidiInputPort> _createInput;
    private readonly Func<IReadOnlyList<(int Index, string Name)>> _outputDevices;
    private readonly Func<int, IMidiOutputPort> _createOutput;

    public MidiManager() : this(MidiBackend.GetInputDevices, MidiBackend.OpenInput,
        MidiBackend.GetOutputDevices, MidiBackend.OpenOutput)
    { }

    // Keep the actual routing and lifecycle testable independently of Windows drivers.
    internal MidiManager(Func<IReadOnlyList<(string Id, string Name)>> inputDevices, Func<string, IMidiInputPort> createInput,
        Func<IReadOnlyList<(int Index, string Name)>> outputDevices, Func<int, IMidiOutputPort> createOutput)
    {
        _inputDevices = inputDevices; _createInput = createInput;
        _outputDevices = outputDevices; _createOutput = createOutput;
    }

    // Events may fire on a background thread — subscribers must marshal to UI if needed.
    public event EventHandler<string>? LogMessage;
    public event EventHandler<NoteOnEventArgs>? NoteOnReceived;
    public event EventHandler<byte[]>? SysexMessageReceived;
    public event EventHandler<int>? PresetChangeReceived;

    public bool InputOpen => _midiIn != null;
    public bool OutputOpen { get { lock (_outLock) { return _midiOut != null; } } }
    public IReadOnlyCollection<string> ThruInputPorts => _thruInputs.Keys.ToArray();

    public static IReadOnlyList<string> GetInputPortNames() => MidiBackend.GetInputDevices().Select(device => device.Name).ToArray();

    public static IReadOnlyList<string> GetOutputPortNames() => MidiBackend.GetOutputDevices().Select(device => device.Name).ToArray();

    // Instance wrappers so callers can depend on IMidiManager instead of the static members directly.
    IReadOnlyList<string> IMidiManager.GetInputPortNames() => _inputDevices().Select(device => device.Name).ToArray();
    IReadOnlyList<string> IMidiManager.GetOutputPortNames() => _outputDevices().Select(device => device.Name).ToArray();

    public Task<MidiPorts> DiscoverPortsAsync() => Task.Run(() => new MidiPorts(
        _inputDevices().Select(device => device.Name).ToArray(), _outputDevices().Select(device => device.Name).ToArray()));

    public bool OpenInput(string portName, out string? errorMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        errorMessage = null;
        CloseInput();
        foreach (var device in _inputDevices())
        {
            if (!string.Equals(device.Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                _midiIn = _createInput(device.Id);
                _mainInputPortName = portName;
                _midiIn.MessageReceived += OnMidiMessage;
                _midiIn.SysexMessageReceived += OnSysexMessageReceived;
                _midiIn.ErrorReceived += OnInputError;
                _midiIn.Start();
                return true;
            }
            catch (Exception ex)
            {
                CloseInput();
                // WinMM returns MMSYSERR_ALLOCATED when another app holds the port.
                errorMessage = ex.Message.Contains("allocated", StringComparison.OrdinalIgnoreCase)
                        || ex.HResult == unchecked((int)0x80004005)
                    ? $"Port in use by another app (close MidiSuite?): {ex.Message}"
                    : ex.Message;
            }
        }
        errorMessage ??= $"Port not found: {portName}";
        return false;
    }

    public bool OpenOutput(string portName, out string? errorMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        errorMessage = null;
        CloseOutput();
        foreach (var port in _outputDevices())
        {
            if (!string.Equals(port.Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                lock (_outLock)
                {
                    _midiOut = _createOutput(port.Index);
                    _outputPortName = portName;
                }
                return true;
            }
            catch (Exception ex)
            {
                CloseOutput();
                errorMessage = ex.Message;
            }
        }
        errorMessage ??= $"Port not found: {portName}";
        return false;
    }

    public void CloseInput()
    {
        var input = Interlocked.Exchange(ref _midiIn, null);
        _mainInputPortName = null;
        if (input == null)
        {
            return;
        }

        input.MessageReceived -= OnMidiMessage;
        input.SysexMessageReceived -= OnSysexMessageReceived;
        input.ErrorReceived -= OnInputError;
        try { input.Stop(); } catch { }
        try { input.Dispose(); } catch { }
    }

    public void CloseOutput()
    {
        lock (_outLock)
        {
            var output = _midiOut;
            _midiOut = null;
            _outputPortName = null;
            try { output?.Dispose(); } catch { }
        }
    }

    // Opens an additional input port whose messages are forwarded as-is to the shared MIDI out.
    public bool OpenThruInput(string portName, out string? errorMessage)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        errorMessage = null;
        if (_thruInputs.ContainsKey(portName))
        {
            return true; // already open
        }

        foreach (var device in _inputDevices())
        {
            if (!string.Equals(device.Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IMidiInputPort? midiIn = null;
            try
            {
                midiIn = _createInput(device.Id);
                midiIn.MessageReceived += OnThruMidiMessage;
                midiIn.ErrorReceived += OnInputError;
                _thruInputs[portName] = midiIn;
                midiIn.Start();
                return true;
            }
            catch (Exception ex)
            {
                if (midiIn is not null)
                {
                    _thruInputs.TryRemove(portName, out _);
                    midiIn.MessageReceived -= OnThruMidiMessage;
                    midiIn.ErrorReceived -= OnInputError;
                    try { midiIn.Stop(); } catch { }
                    try { midiIn.Dispose(); } catch { }
                }

                errorMessage = ex.Message;
                return false;
            }
        }
        errorMessage ??= $"Port not found: {portName}";
        return false;
    }

    public void CloseThruInput(string portName)
    {
        if (!_thruInputs.TryRemove(portName, out var midiIn))
        {
            return;
        }

        midiIn.MessageReceived -= OnThruMidiMessage;
        midiIn.ErrorReceived -= OnInputError;
        try { midiIn.Stop(); } catch { }
        try { midiIn.Dispose(); } catch { }
    }

    public void CloseAllThruInputs()
    {
        foreach (var name in _thruInputs.Keys.ToList())
        {
            CloseThruInput(name);
        }
    }

    // Called from UI thread only.
    public bool SendBankAndPC(int bank, int pc, int midiChannel)
    {
        if (bank is < 0 or > 127 || pc is < 0 or > 127 || midiChannel is < 1 or > 16)
        {
            return false;
        }

        try
        {
            lock (_outLock)
            {
                if (_midiOut is null) { return false; }
                // The public API uses channels 1–16; wire messages use 0–15.
                _midiOut.Send(MidiMessageEncoding.ControlChange(0, bank, midiChannel));
                LogMessage?.Invoke(this, $"OUTPUT: CC ch{midiChannel} CC#0 value={bank}");
                _midiOut.Send(MidiMessageEncoding.ProgramChange(pc, midiChannel));
                LogMessage?.Invoke(this, $"OUTPUT: PC ch{midiChannel} program={pc}");
            }
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"OUTPUT ERROR: {ex.Message}");
            return false;
        }
    }

    // Sends Bank Select + Program Change to reach the preset, then a Scene Select CC (scene 1-8 -> value 0-7).
    public bool SendFavorite(int bank, int pc, int scene, int sceneCc, int midiChannel)
    {
        if (bank is < 0 or > 127 || pc is < 0 or > 127 || scene is < 1 or > 8 || sceneCc is < 0 or > 127 || midiChannel is < 1 or > 16)
        {
            return false;
        }

        try
        {
            lock (_outLock)
            {
                if (_midiOut is null) { return false; }
                _midiOut.Send(MidiMessageEncoding.ControlChange(0, bank, midiChannel));
                LogMessage?.Invoke(this, $"OUTPUT: CC ch{midiChannel} CC#0 value={bank}");
                _midiOut.Send(MidiMessageEncoding.ProgramChange(pc, midiChannel));
                LogMessage?.Invoke(this, $"OUTPUT: PC ch{midiChannel} program={pc}");
                int sceneValue = scene - 1;
                _midiOut.Send(MidiMessageEncoding.ControlChange(sceneCc, sceneValue, midiChannel));
                LogMessage?.Invoke(this, $"OUTPUT: CC ch{midiChannel} CC#{sceneCc} value={sceneValue} (scene {scene})");
            }
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"OUTPUT ERROR: {ex.Message}");
            return false;
        }
    }

    public bool SendSysEx(byte[] frame)
    {
        if (frame.Length < 2 || frame[0] != 0xf0 || frame[^1] != 0xf7 ||
            frame.AsSpan(1, frame.Length - 2).ContainsAnyInRange((byte)0x80, byte.MaxValue))
        {
            return false;
        }
        try
        {
            lock (_outLock)
            {
                if (_midiOut is null) { return false; }
                _midiOut.SendBuffer(frame);
            }

            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(this, $"OUTPUT SYSEX ERROR: {ex.Message}");
            return false;
        }
    }

    public bool SendScene(int scene, int sceneCc, int midiChannel)
    {
        if (scene is < 1 or > 8 || sceneCc is < 0 or > 127 || midiChannel is < 1 or > 16)
        {
            return false;
        }

        try
        {
            lock (_outLock)
            {
                if (_midiOut == null)
                {
                    return false;
                }

                _midiOut.Send(MidiMessageEncoding.ControlChange(sceneCc, scene - 1, midiChannel));
            }
            return true;
        }
        catch (Exception ex) { LogMessage?.Invoke(this, $"SCENE ERROR: {ex.Message}"); return false; }
    }

    private void OnInputError(object? sender, string error)
    {
        if (ReferenceEquals(sender, _midiIn) || _thruInputs.Values.Any(input => ReferenceEquals(input, sender)))
        {
            LogMessage?.Invoke(this, $"MIDI INPUT ERROR: {error}");
        }
    }

    private void OnSysexMessageReceived(object? sender, MidiSysExMessageEventArgs e)
    {
        if (ReferenceEquals(sender, _midiIn)) { SysexMessageReceived?.Invoke(this, e.SysexBytes.ToArray()); }
    }

    private void OnMidiMessage(object? sender, MidiShortMessageEventArgs e)
    {
        if (!ReferenceEquals(sender, _midiIn)) { return; }
        // Decode the packed MIDI 1.0 message directly — avoids NAudio parse exceptions
        // on real-time bytes (Active Sensing 0xFE, Clock 0xF8, etc.).
        byte status = (byte)(e.RawMessage & 0xFF);
        if (status >= 0xF8)
        {
            return; // ignore real-time messages
        }

        int command = status & 0xF0;
        int channel = (status & 0x0F) + 1;
        int data1 = (e.RawMessage >> 8) & 0x7F;
        int data2 = (e.RawMessage >> 16) & 0x7F;
        if (command == 0xc0)
        {
            PresetChangeReceived?.Invoke(this, channel);
        }

        if (command == 0x90 && data2 > 0)
        {
            LogMessage?.Invoke(this, $"INPUT: Note On ch{channel} note={data1} velocity={data2}");
            NoteOnReceived?.Invoke(this, new NoteOnEventArgs(data1, data2, channel, _mainInputPortName));
        }
        else if (command == 0x90 || command == 0x80)
        {
            LogMessage?.Invoke(this, $"INPUT: Note Off ch{channel} note={data1} (ignored)");
        }
        else
        {
            LogMessage?.Invoke(this, $"INPUT: 0x{status:X2} data={data1},{data2} ch{channel} (ignored)");
        }
    }

    private void OnThruMidiMessage(object? sender, MidiShortMessageEventArgs e)
    {
        byte status = (byte)(e.RawMessage & 0xFF);
        if (status >= 0xF8)
        {
            return; // ignore real-time messages (clock/active-sense would flood the merged output)
        }

        string? sourcePort = _thruInputs.FirstOrDefault(kv => ReferenceEquals(kv.Value, sender)).Key;
        if (sourcePort is null) { return; } // Ignore callbacks queued before a port was closed.
        string? outputPort;
        lock (_outLock)
        {
            if (_midiOut == null)
            {
                LogMessage?.Invoke(this, $"THRU: dropped 0x{status:X2} from '{sourcePort ?? "unknown"}' (no MIDI output connected)");
                return;
            }

            try { _midiOut.Send(e.RawMessage); }
            catch (Exception ex) { LogMessage?.Invoke(this, $"THRU ERROR: {ex.Message}"); return; }
            outputPort = _outputPortName;
        }
        int command = status & 0xF0;
        int channel = (status & 0x0F) + 1;
        int data1 = (e.RawMessage >> 8) & 0x7F;
        int data2 = (e.RawMessage >> 16) & 0x7F;

        LogMessage?.Invoke(this, $"THRU: forwarded 0x{status:X2} data={data1},{data2} ch{channel} from '{sourcePort ?? "unknown"}' to '{outputPort}' unchanged");

        if (command == 0xc0)
        {
            PresetChangeReceived?.Invoke(this, channel);
        }
        else if (command == 0x90 && data2 > 0)
        {
            LogMessage?.Invoke(this, $"THRU: Note On ch{channel} note={data1} velocity={data2}");
            NoteOnReceived?.Invoke(this, new NoteOnEventArgs(data1, data2, channel, sourcePort));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CloseInput();
        CloseOutput();
        CloseAllThruInputs();
        _disposed = true;
    }

}

public sealed class NoteOnEventArgs(int noteNumber, int velocity, int channel, string? sourcePort = null) : EventArgs
{
    public int NoteNumber { get; } = noteNumber;
    public int Velocity { get; } = velocity;
    public int Channel { get; } = channel;
    // The MIDI port the note arrived on (main input or a thru port); null if unknown.
    public string? SourcePort { get; } = sourcePort;
}
