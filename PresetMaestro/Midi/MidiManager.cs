using NAudio;
using NAudio.Midi;

namespace PresetMaestro.Midi;

public sealed class MidiManager : IMidiManager
{
    private IMidiInput? _midiIn;
    private string? _mainInputPortName;
    private MidiOut? _midiOut;
    private string? _outputPortName;
    private bool _disposed;

    // Extra input ports whose raw messages are forwarded straight to _midiOut (MIDI thru/merge).
    private readonly Dictionary<string, IMidiInput> _thruInputs = new(StringComparer.OrdinalIgnoreCase);
    // Guards all writes to _midiOut, since preset sends and thru forwarding can happen from different threads.
    private readonly object _outLock = new();

    // Events may fire on a background thread — subscribers must marshal to UI if needed.
    public event EventHandler<string>? LogMessage;
    public event EventHandler<NoteOnEventArgs>? NoteOnReceived;
    public event EventHandler<byte[]>? SysexMessageReceived;
    public event EventHandler<int>? PresetChangeReceived;

    public bool InputOpen => _midiIn != null;
    public bool OutputOpen => _midiOut != null;
    public IReadOnlyCollection<string> ThruInputPorts => _thruInputs.Keys;

    public static IReadOnlyList<string> GetInputPortNames()
    {
        return GetWinRtInputDevices().Select(device => device.Name).ToArray();
    }

    public static IReadOnlyList<string> GetOutputPortNames()
    {
        return GetOutputPorts(MidiOut.NumberOfDevices, index => MidiOut.DeviceInfo(index).ProductName)
            .Select(port => port.Name).ToArray();
    }

    internal static IReadOnlyList<(int Index, string Name)> GetOutputPorts(int deviceCount, Func<int, string> getName)
    {
        var ports = new List<(int Index, string Name)>();
        for (int i = 0; i < deviceCount; i++)
        {
            try { ports.Add((i, getName(i))); }
            // A stale or unavailable driver entry must not hide the other outputs.
            catch (MmException) { }
        }

        return ports;
    }

    // Instance wrappers so callers can depend on IMidiManager instead of the static members directly.
    IReadOnlyList<string> IMidiManager.GetInputPortNames() => GetInputPortNames();
    IReadOnlyList<string> IMidiManager.GetOutputPortNames() => GetOutputPortNames();

    public bool OpenInput(string portName, out string? errorMessage)
    {
        errorMessage = null;
        CloseInput();
        foreach (var device in GetWinRtInputDevices())
        {
            if (!string.Equals(device.Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                _midiIn = CreateWinRtInput(device.Id);
                _midiIn.MessageReceived += OnMidiMessage;
                _midiIn.SysexMessageReceived += OnSysexMessageReceived;
                _midiIn.Start();
                _mainInputPortName = portName;
                return true;
            }
            catch (Exception ex)
            {
                _midiIn?.Dispose();
                _midiIn = null;
                _mainInputPortName = null;
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
        errorMessage = null;
        CloseOutput();
        foreach (var port in GetOutputPorts(MidiOut.NumberOfDevices, index => MidiOut.DeviceInfo(index).ProductName))
        {
            if (!string.Equals(port.Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                _midiOut = new MidiOut(port.Index);
                _outputPortName = portName;
                return true;
            }
            catch (Exception ex)
            {
                _midiOut?.Dispose();
                _midiOut = null;
                errorMessage = ex.Message;
            }
        }
        errorMessage ??= $"Port not found: {portName}";
        return false;
    }

    public void CloseInput()
    {
        if (_midiIn == null)
        {
            return;
        }

        _midiIn.MessageReceived -= OnMidiMessage;
        _midiIn.SysexMessageReceived -= OnSysexMessageReceived;
        try { _midiIn.Stop(); } catch { }
        try { _midiIn.Dispose(); } catch { }
        _midiIn = null;
        _mainInputPortName = null;
    }

    public void CloseOutput()
    {
        try { _midiOut?.Dispose(); } catch { }
        _midiOut = null;
        _outputPortName = null;
    }

    // Opens an additional input port whose messages are forwarded as-is to the shared MIDI out.
    public bool OpenThruInput(string portName, out string? errorMessage)
    {
        errorMessage = null;
        if (_thruInputs.ContainsKey(portName))
        {
            return true; // already open
        }

        foreach (var device in GetWinRtInputDevices())
        {
            if (!string.Equals(device.Name, portName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IMidiInput? midiIn = null;
            try
            {
                midiIn = CreateWinRtInput(device.Id);
                midiIn.MessageReceived += OnThruMidiMessage;
                midiIn.Start();
                _thruInputs[portName] = midiIn;
                return true;
            }
            catch (Exception ex)
            {
                if (midiIn is not null)
                {
                    midiIn.MessageReceived -= OnThruMidiMessage;
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
        if (!_thruInputs.TryGetValue(portName, out var midiIn))
        {
            return;
        }

        midiIn.MessageReceived -= OnThruMidiMessage;
        try { midiIn.Stop(); } catch { }
        try { midiIn.Dispose(); } catch { }
        _thruInputs.Remove(portName);
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
        if (_midiOut == null)
        {
            return false;
        }

        try
        {
            lock (_outLock)
            {
                // NAudio 3.x MidiMessage expects 1-indexed channel (unlike 2.x which was 0-indexed)
                _midiOut.Send(MidiMessage.ChangeControl(0, bank, midiChannel).RawData);
                LogMessage?.Invoke(this, $"OUTPUT: CC ch{midiChannel} CC#0 value={bank}");
                _midiOut.Send(MidiMessage.ChangePatch(pc, midiChannel).RawData);
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
        if (_midiOut == null || scene is < 1 or > 8 || sceneCc is < 0 or > 127 || midiChannel is < 1 or > 16)
        {
            return false;
        }

        try
        {
            lock (_outLock)
            {
                _midiOut.Send(MidiMessage.ChangeControl(0, bank, midiChannel).RawData);
                LogMessage?.Invoke(this, $"OUTPUT: CC ch{midiChannel} CC#0 value={bank}");
                _midiOut.Send(MidiMessage.ChangePatch(pc, midiChannel).RawData);
                LogMessage?.Invoke(this, $"OUTPUT: PC ch{midiChannel} program={pc}");
                int sceneValue = scene - 1;
                _midiOut.Send(MidiMessage.ChangeControl(sceneCc, sceneValue, midiChannel).RawData);
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
        if (_midiOut == null)
        {
            return false;
        }

        try
        {
            lock (_outLock)
            {
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

                _midiOut.Send(MidiMessage.ChangeControl(sceneCc, scene - 1, midiChannel).RawData);
            }
            return true;
        }
        catch (Exception ex) { LogMessage?.Invoke(this, $"SCENE ERROR: {ex.Message}"); return false; }
    }

    private void OnSysexMessageReceived(object? sender, MidiInSysexMessageEventArgs e)
    {
        if (ReferenceEquals(sender, _midiIn)) { SysexMessageReceived?.Invoke(this, e.SysexBytes.ToArray()); }
    }

    private void OnMidiMessage(object? sender, MidiInMessageEventArgs e)
    {
        // Decode the raw WinMM short message directly — avoids NAudio parse exceptions
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

    private void OnThruMidiMessage(object? sender, MidiInMessageEventArgs e)
    {
        byte status = (byte)(e.RawMessage & 0xFF);
        if (status >= 0xF8)
        {
            return; // ignore real-time messages (clock/active-sense would flood the merged output)
        }

        string? sourcePort = _thruInputs.FirstOrDefault(kv => ReferenceEquals(kv.Value, sender)).Key;
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

    private static IReadOnlyList<Windows.Devices.Enumeration.DeviceInformation> GetWinRtInputDevices() =>
        Task.Run(async () => await WinRTMidiIn.GetDevicesAsync()).GetAwaiter().GetResult();

    private static WinRTMidiIn CreateWinRtInput(string deviceId) =>
        Task.Run(async () => await WinRTMidiIn.CreateAsync(deviceId)).GetAwaiter().GetResult();
}

public sealed class NoteOnEventArgs(int noteNumber, int velocity, int channel, string? sourcePort = null) : EventArgs
{
    public int NoteNumber { get; } = noteNumber;
    public int Velocity { get; } = velocity;
    public int Channel { get; } = channel;
    // The MIDI port the note arrived on (main input or a thru port); null if unknown.
    public string? SourcePort { get; } = sourcePort;
}
