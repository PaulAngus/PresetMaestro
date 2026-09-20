using System.Diagnostics;
using System.Threading.Channels;
using PresetNameSync.Core;
using NAudio.Midi;

namespace PresetNameSyncProbe.Midi;

public sealed class MidiProbeClient : IDisposable
{
    private readonly object _outLock = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Channel<byte[]> _sysexFrames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    private MidiIn? _midiIn;
    private MidiOut? _midiOut;
    private bool _disposed;
    private readonly SysexAssembler _assembler = new();

    public event EventHandler<string>? LogMessage;

    public bool IsConnected => _midiIn != null && _midiOut != null;

    public static IReadOnlyList<string> GetInputPortNames()
    {
        var names = new string[MidiIn.NumberOfDevices];
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            names[i] = MidiIn.DeviceInfo(i).ProductName;
        }

        return names;
    }

    public static IReadOnlyList<string> GetOutputPortNames()
    {
        var names = new string[MidiOut.NumberOfDevices];
        for (int i = 0; i < MidiOut.NumberOfDevices; i++)
        {
            names[i] = MidiOut.DeviceInfo(i).ProductName;
        }

        return names;
    }

    public bool Connect(string inputPortName, string outputPortName, out string error)
    {
        error = string.Empty;
        Disconnect();

        int inputIndex = FindInputPortIndex(inputPortName);
        int outputIndex = FindOutputPortIndex(outputPortName);

        if (inputIndex < 0)
        {
            error = $"Input port not found: {inputPortName}";
            return false;
        }

        if (outputIndex < 0)
        {
            error = $"Output port not found: {outputPortName}";
            return false;
        }

        try
        {
            _midiIn = new MidiIn(inputIndex);
            _midiIn.SysexMessageReceived += OnSysexMessageReceived;
            _midiIn.ErrorReceived += OnMidiError;
            _midiIn.Start();

            _midiOut = new MidiOut(outputIndex);

            LogMessage?.Invoke(this, $"Connected input '{inputPortName}' and output '{outputPortName}'.");
            return true;
        }
        catch (Exception ex)
        {
            Disconnect();

            bool likelyAllocated = ex.Message.Contains("allocated", StringComparison.OrdinalIgnoreCase)
                                   || ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase)
                                   || ex.HResult == unchecked((int)0x80004005);

            error = likelyAllocated
                ? $"Failed to open MIDI port. The device port is likely in use by device editor or another app. Details: {ex.Message}"
                : $"Failed to open MIDI port. Details: {ex.Message}";
            return false;
        }
    }

    public void Disconnect()
    {
        if (_midiIn != null)
        {
            _midiIn.SysexMessageReceived -= OnSysexMessageReceived;
            _midiIn.ErrorReceived -= OnMidiError;
            try { _midiIn.Stop(); } catch { }
            try { _midiIn.Dispose(); } catch { }
            _midiIn = null;
        }

        if (_midiOut != null)
        {
            try { _midiOut.Dispose(); } catch { }
            _midiOut = null;
        }

        DrainFrames();
    }

    public async Task<PresetNameResult> QueryPresetNameAsync(int slot, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_midiOut == null)
        {
            throw new InvalidOperationException("MIDI output is not connected.");
        }

        if (_midiIn == null)
        {
            throw new InvalidOperationException("MIDI input is not connected.");
        }

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DrainFrames();

            byte[] request = SysexProtocol.BuildPresetNameQuery(slot);
            var stopwatch = Stopwatch.StartNew();
            lock (_outLock)
            {
                _midiOut.SendBuffer(request);
            }

            LogMessage?.Invoke(this, $"TX {SysexProtocol.ToHex(request)}");

            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    LogMessage?.Invoke(this, $"Slot {slot}: timeout after {stopwatch.ElapsedMilliseconds} ms.");
                    throw new TimeoutException($"Timed out waiting for preset name response for slot {slot}.");
                }

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                waitCts.CancelAfter(remaining);

                byte[] response;
                try
                {
                    response = await _sysexFrames.Reader.ReadAsync(waitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    LogMessage?.Invoke(this, $"Slot {slot}: timeout after {stopwatch.ElapsedMilliseconds} ms.");
                    throw new TimeoutException($"Timed out waiting for preset name response for slot {slot}.");
                }

                if (!SysexProtocol.TryParsePresetNameResponse(response, out PresetNameResult? parsed, out string parseError))
                {
                    LogMessage?.Invoke(this, $"Ignored SysEx frame: {parseError}");
                    continue;
                }

                if (parsed!.Slot != slot)
                {
                    LogMessage?.Invoke(this, $"Ignored preset-name response for unexpected slot {parsed.Slot}; expected {slot}.");
                    continue;
                }

                stopwatch.Stop();
                LogMessage?.Invoke(this, $"Slot {slot}: response received in {stopwatch.ElapsedMilliseconds} ms.");
                return parsed;
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private void OnSysexMessageReceived(object? sender, MidiInSysexMessageEventArgs e)
    {
        byte[] data = e.SysexBytes.ToArray();
        LogMessage?.Invoke(this, $"RX {SysexProtocol.ToHex(data)}");
        lock (_assembler)
        {
            foreach (var frame in _assembler.Feed(data))
            {
                _sysexFrames.Writer.TryWrite(frame);
            }
        }
    }

    public async Task<PresetScenes> QueryScenesAsync(int? storedSlot, CancellationToken token)
    {
        await _requestGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (storedSlot.HasValue)
            {
                var decoder = new StoredPresetDecoder();
                return await QueryRead(StoredPresetDecoder.BuildQuery(storedSlot.Value), f => decoder.Accept(f, storedSlot.Value), TimeSpan.FromSeconds(15), token);
            }
            Task<PresetNameResult> Current() => QueryRead(SysexProtocol.BuildCurrentPresetQuery(),
                f => SysexProtocol.TryParsePresetNameResponse(f, out var r, out _) ? r : null, TimeSpan.FromSeconds(1.5), token);
            var before = await Current(); var names = new string[8];
            for (int index = 0; index < 8; index++)
            {
                var result = await QueryRead(SysexProtocol.BuildSceneNameQuery(index),
                    f => SysexProtocol.TryParseSceneNameResponse(f, out var r) && r!.Index == index ? r : null, TimeSpan.FromSeconds(1.5), token);
                names[index] = result.Name;
            }
            var after = await Current();
            if (before.Slot != after.Slot)
            {
                throw new InvalidOperationException("Preset changed during scene read.");
            }

            return new PresetScenes(before.Slot, after.PresetName, names);
        }
        finally { _requestGate.Release(); }
    }

    private async Task<T> QueryRead<T>(byte[] request, Func<byte[], T?> parser, TimeSpan timeout, CancellationToken token) where T : class
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Connect both MIDI ports.");
        }

        DrainFrames();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
        lock (_outLock)
        {
            _midiOut!.SendBuffer(request);
        }

        LogMessage?.Invoke(this, $"TX {SysexProtocol.ToHex(request)}");
        try
        {
            while (true)
            {
                var frame = await _sysexFrames.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                var result = parser(frame); if (result != null)
                {
                    return result;
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Scene query timed out."); }
    }

    private void OnMidiError(object? sender, MidiInMessageEventArgs e)
    {
        LogMessage?.Invoke(this, $"MIDI input error: raw=0x{e.RawMessage:X8}");
    }

    private static int FindInputPortIndex(string portName)
    {
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            if (string.Equals(MidiIn.DeviceInfo(i).ProductName, portName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindOutputPortIndex(string portName)
    {
        for (int i = 0; i < MidiOut.NumberOfDevices; i++)
        {
            if (string.Equals(MidiOut.DeviceInfo(i).ProductName, portName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void DrainFrames()
    {
        while (_sysexFrames.Reader.TryRead(out _))
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Disconnect();
        // A cancelled query may still be unwinding and releasing this gate.
        _disposed = true;
    }
}
