using System.Threading.Channels;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro.Midi;

/// <summary>Serializes all name/state reads on the existing MIDI connection.</summary>
public sealed class PresetNameClient : IDisposable
{
    private readonly IMidiManager _midi;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly SysexAssembler _assembler = new();
    private readonly CancellationTokenSource _lifetime = new();
    private long _quietUntil;
    private bool _disposed;
    private long _presetRevision;
    private DeviceModel _deviceModel = DeviceModel.FM9;

    public PresetNameClient(IMidiManager midi)
    {
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

    private async Task<T> Locked<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _requestGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (_deviceModel != DeviceModel.FM9)
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
        long remainingQuiet = _quietUntil - Environment.TickCount64;
        if (remainingQuiet > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(remainingQuiet), token).ConfigureAwait(false);
        }

        while (_frames.Reader.TryRead(out _)) { }
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        bool completed = false;
        try
        {
            if (!_midi.SendSysEx(request))
            {
                throw new IOException("Could not send SysEx query.");
            }

            while (true)
            {
                var frame = await _frames.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                T? result = parse(frame);
                if (result != null) { token.ThrowIfCancellationRequested(); completed = true; return result; }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException($"device query 0x{request[5]:X2} timed out."); }
        finally
        {
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
