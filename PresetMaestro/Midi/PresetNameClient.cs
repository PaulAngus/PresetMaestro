using System.Diagnostics;
using System.Threading.Channels;
using PresetMaestro.Core;
#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;
#endif
using PresetNameSync.Core;

namespace PresetMaestro.Midi;

/// <summary>Serializes all name/state reads on the existing MIDI connection.</summary>
public sealed class PresetNameClient : IDisposable
#if FRACTAL_INDEX
    , IStoredPresetImageSource, IStoredPresetNameSource, IPresetCapacitySource
#endif
{
    private readonly IMidiManager _midi;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Channel<byte[]> _frames;
    private long _droppedFrames;
    private readonly SysexAssembler _assembler = new();
    private readonly CancellationTokenSource _lifetime = new();
    private long _quietUntil;
    private long? _lastQueryStarted;
    private static readonly TimeSpan MinimumQueryInterval = TimeSpan.FromMilliseconds(10);
    private bool _disposed;
    private long _presetRevision;
    private DeviceModel _deviceModel = DeviceModel.FM9;

    public PresetNameClient(IMidiManager midi)
    {
        _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
        { FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref _droppedFrames));
        _midi = midi; _midi.SysexMessageReceived += OnSysexMessageReceived;
        _midi.PresetChangeReceived += OnPresetChange;
    }
    public void SetDeviceModel(DeviceModel model) => _deviceModel = model;
    public Task<PresetNameResult> QueryAsync(int slot, TimeSpan timeout, CancellationToken token) =>
        Locked(ct => Exchange(SysexProtocol.BuildPresetNameQuery(slot), f =>
            SysexProtocol.TryParsePresetNameResponse(f, out var r, out _) && r!.Slot == slot ? r : null, timeout, ct), token);
    public Task<PresetNameResult> CurrentPresetAsync(CancellationToken token) => Locked(CurrentPreset, token);
    /// <summary>Reads a stable preset/scene snapshot without allowing another queued read between requests.</summary>
    public Task<(PresetNameResult Preset, SceneNameResult Scene)> CurrentStateAsync(CancellationToken token) => Locked(async ct =>
    {
        var before = await CurrentPreset(ct);
        var scene = await Exchange(SysexProtocol.BuildCurrentSceneQuery(), f => SysexProtocol.ValidFrame(f, 0x0c, 9) && f[6] < 8
            ? new SceneNameResult(f[6], "") : null, TimeSpan.FromSeconds(1.5), ct);
        var after = await CurrentPreset(ct);
        if (before.Slot != after.Slot)
        {
            throw new InvalidOperationException("Active preset changed while reading the current scene.");
        }

        return (after, scene);
    }, token);
    private Task<PresetNameResult> CurrentPreset(CancellationToken token) =>
        Exchange(SysexProtocol.BuildCurrentPresetQuery(), f =>
            SysexProtocol.TryParsePresetNameResponse(f, out var r, out _) ? r : null, TimeSpan.FromSeconds(1.5), token);
    public Task<SceneNameResult> CurrentSceneAsync(CancellationToken token) => Locked(ct =>
        Exchange(SysexProtocol.BuildCurrentSceneQuery(), f => SysexProtocol.ValidFrame(f, 0x0c, 9) && f[6] < 8
            ? new SceneNameResult(f[6], "") : null, TimeSpan.FromSeconds(1.5), ct), token);
    public Task<PresetScenes> SceneNamesAsync(int expectedSlot, CancellationToken token) => Locked(async ct =>
    {
        long revision = Interlocked.Read(ref _presetRevision);
        // Replies lack preset identity: hold the gate and verify the preset before AND after.
        var before = await CurrentPreset(ct);
        if (before.Slot != expectedSlot)
        {
            throw new InvalidOperationException("Active preset changed before reading scenes.");
        }

        var names = new string[8];
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            var scene = await Exchange(SysexProtocol.BuildSceneNameQuery(index), f =>
                SysexProtocol.TryParseSceneNameResponse(f, out var r) && r!.Index == index ? r : null,
                TimeSpan.FromSeconds(1.5), ct);
            names[index] = scene.Name;
        }
        var after = await CurrentPreset(ct);
        if (after.Slot != expectedSlot || revision != Interlocked.Read(ref _presetRevision))
        {
            throw new InvalidOperationException("Active preset changed while reading scenes.");
        }

        return new PresetScenes(expectedSlot, after.PresetName, names);
    }, token);
    public Task<PresetScenes> StoredScenesAsync(int slot, CancellationToken token) => Locked(ct =>
    {
        var decoder = new StoredPresetDecoder();
        return Exchange(StoredPresetDecoder.BuildQuery(slot), f => decoder.Accept(f, slot), TimeSpan.FromSeconds(15), ct);
    }, token);

#if FRACTAL_INDEX
    public async Task<int?> DetectPresetCapacityAsync(FractalDeviceDefinition device, CancellationToken token)
    {
        if (device.Variant.ToDeviceModel() != _deviceModel)
        { throw new InvalidOperationException("Capacity query does not match the connected device model."); }
        if (_deviceModel != DeviceModel.AxeFxIII) { return device.PresetSlots; }
        const int boundarySlot = 512;
        try
        {
            var evidence = await Locked(ct => Exchange(
                SysexProtocol.Frame(device.ModelByte, 0x0d, [0, 4]),
                frame => SysexProtocol.ValidFrame(frame, device.ModelByte, 0x0d, 42) &&
                    (frame[6] | frame[7] << 7) == boundarySlot ? new CapacityEvidence(1024) : null,
                TimeSpan.FromSeconds(1.5), ct), token, allowIndexRead: true).ConfigureAwait(false);
            return evidence.Slots;
        }
        catch (TimeoutException) { return null; }
    }

    private sealed record CapacityEvidence(int Slots);

    public async Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
    {
        // Keep the cheap name check on the existing verified FM9 path.
        if (device.Variant != FractalDeviceVariant.FM9) { return null; }
        if (device.Variant.ToDeviceModel() != _deviceModel)
        { throw new InvalidOperationException("Index name query does not match the connected device model."); }
        return (await QueryAsync(slot, TimeSpan.FromSeconds(1.5), token).ConfigureAwait(false)).PresetName;
    }

    /// <summary>Index reads share the name client's request gate and late-reply quarantine.</summary>
    public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => Locked(ct =>
    {
        if (device.Variant.ToDeviceModel() != _deviceModel)
        {
            throw new InvalidOperationException("Index decoder does not match the connected device model.");
        }
        var decoder = new StoredPresetDecoder(device.ModelByte, device.PresetSlots - 1);
        return Exchange(StoredPresetDecoder.BuildQuery(slot, device.ModelByte, device.PresetSlots - 1),
            frame => decoder.AcceptImage(frame, slot), TimeSpan.FromSeconds(15), ct);
    }, token, allowIndexRead: true);
#endif

    private async Task<T> Locked<T>(Func<CancellationToken, Task<T>> action, CancellationToken token, bool allowIndexRead = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await ReadDiagnostics.MeasureAsync("midi.queue", null, async () =>
        { await _requestGate.WaitAsync(linked.Token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
        try
        {
            if (_deviceModel != DeviceModel.FM9 &&
                !(allowIndexRead && _deviceModel is DeviceModel.FM3 or DeviceModel.AxeFxIII))
            {
                throw new NotSupportedException("Preset and scene name reads are verified for FM9 only.");
            }
            if (!_midi.InputOpen || !_midi.OutputOpen)
            {
                throw new InvalidOperationException("Connect both MIDI input and output.");
            }

            return await action(linked.Token).ConfigureAwait(false);
        }
        finally { _requestGate.Release(); }
    }
    private async Task<T> Exchange<T>(byte[] request, Func<byte[], T?> parse, TimeSpan timeout, CancellationToken token) where T : class
    {
        // Name requests use low/high septets; dump requests use high/low.
        int? slot = request[5] == 0x0d ? request[6] | request[7] << 7 :
            request[5] == 0x03 ? request[6] << 7 | request[7] : null;
        return await ReadDiagnostics.MeasureAsync("midi.exchange", slot,
            () => ExchangeCore(request, parse, timeout, slot, token)).ConfigureAwait(false);
    }

    private async Task<T> ExchangeCore<T>(byte[] request, Func<byte[], T?> parse, TimeSpan timeout,
        int? slot, CancellationToken token) where T : class
    {
        long remainingQuiet = _quietUntil - Environment.TickCount64;
        if (remainingQuiet > 0)
        {
            await ReadDiagnostics.MeasureAsync("midi.quiet", slot, async () =>
            { await Task.Delay(TimeSpan.FromMilliseconds(remainingQuiet), token).ConfigureAwait(false); return true; }).ConfigureAwait(false);
        }

        // Fast USB replies can arrive in ~1 ms. Avoid immediately issuing hundreds
        // of back-to-back queries; keep the gap within the shared request gate.
        // This is a conservative application policy, not a protocol requirement.
        if (_lastQueryStarted is long lastQuery)
        {
            var remaining = MinimumQueryInterval - Stopwatch.GetElapsedTime(lastQuery);
            if (remaining > TimeSpan.Zero)
            {
                await ReadDiagnostics.MeasureAsync("midi.pacing", slot, async () =>
                {
                    while (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(remaining, token).ConfigureAwait(false);
                        remaining = MinimumQueryInterval - Stopwatch.GetElapsedTime(lastQuery);
                    }
                    return true;
                }).ConfigureAwait(false);
            }
        }

        int discarded = 0;
        while (_frames.Reader.TryRead(out _)) { discarded++; }
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        bool completed = false;
        int received = 0, matching = 0, rejected = 0, invalid = 0;
        var opcodes = new Dictionary<string, int>();
        long dropsBefore = Interlocked.Read(ref _droppedFrames);
        string receiveOutcome = "failed";
        try
        {
            _lastQueryStarted = Stopwatch.GetTimestamp();
            if (!ReadDiagnostics.Measure("midi.send", slot, () => _midi.SendSysEx(request)))
            {
                throw new IOException("Could not send SysEx query.");
            }

            return await ReadDiagnostics.MeasureAsync("midi.reply", slot, async () =>
            {
                try
                {
                    while (true)
                    {
                        var frame = await _frames.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                        received++;
                        string opcode = frame.Length > 5 && frame[1] == 0 && frame[2] == 1 && frame[3] == 0x74
                            ? $"0x{frame[5]:X2}" : "other";
                        opcodes[opcode] = opcodes.GetValueOrDefault(opcode) + 1;
                        bool matches = frame.Length > 5 && frame[5] == request[5];
                        if (matches)
                        {
                            matching++;
                            if (!SysexProtocol.ValidFrame(frame, request[4], request[5], frame.Length)) { invalid++; }
                        }
                        T? result = parse(frame);
                        if (result != null) { token.ThrowIfCancellationRequested(); completed = true; receiveOutcome = "complete"; return result; }
                        if (matches) { rejected++; }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    receiveOutcome = "timeout";
                    throw new TimeoutException($"device query 0x{request[5]:X2} timed out ({received} frames received, {matching} matching opcode, {rejected} rejected).");
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException($"device query 0x{request[5]:X2} timed out."); }
        catch (OperationCanceledException) { receiveOutcome = "cancelled"; throw; }
        finally
        {
            ReadDiagnostics.Record(new ReadTiming("midi.receive", slot, 0, receiveOutcome)
            {
                Receive = new(request[5], received, matching, rejected, invalid, discarded,
                    Interlocked.Read(ref _droppedFrames) - dropsBefore)
                { OpcodeCounts = opcodes }
            });
            // No transaction IDs: quarantine late responses after cancellation/timeout.
            if (!completed)
            {
                _quietUntil = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            }
        }
    }
    private void OnSysexMessageReceived(object? sender, byte[] data)
    {
        lock (_assembler)
        {
            foreach (var frame in _assembler.Feed(data))
            {
                _frames.Writer.TryWrite(frame);
            }
        }
    }
    private void OnPresetChange(object? sender, int channel) => Interlocked.Increment(ref _presetRevision);
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true; _lifetime.Cancel(); _midi.SysexMessageReceived -= OnSysexMessageReceived;
        _midi.PresetChangeReceived -= OnPresetChange;
        // Do not dispose the gate while a cancelled operation still owns it.
    }
}
