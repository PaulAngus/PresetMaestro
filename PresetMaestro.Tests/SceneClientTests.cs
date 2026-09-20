using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public class SceneClientTests
{
    [Fact]
    public async Task CurrentStateReadsPresetAndSceneAsOneQueuedSnapshot()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Preset"));
            }
            else
            {
                midi.Reply(SysexProtocol.Frame(0x0c, [3]));
            }
        };

        var state = await client.CurrentStateAsync(CancellationToken.None);

        Assert.Equal(5, state.Preset.Slot);
        Assert.Equal("Preset", state.Preset.PresetName);
        Assert.Equal(3, state.Scene.Index);
        Assert.Equal(new byte[] { 0x0d, 0x0c, 0x0d }, midi.Sent.Select(frame => frame[5]));
    }

    [Fact]
    public async Task LiveSnapshotQueriesAllNamesAndNeverSelectsScenes()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Preset"));
            }
            else
            {
                midi.Reply(SceneProtocolTests.Response(0x0e, [frame[6]], $"Scene {frame[6]}"));
            }
        };
        var names = await client.SceneNamesAsync(5, CancellationToken.None);
        Assert.Equal(8, names.Names.Length);
        Assert.Equal(10, midi.Sent.Count);
        Assert.All(midi.Sent, frame => Assert.Contains(frame[5], new byte[] { 0x0d, 0x0e }));
    }

    [Fact]
    public async Task ReturningToSamePresetDuringReadStillInvalidatesSnapshot()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Preset"));
            }
            else { if (frame[6] == 3) { midi.Changed(); } midi.Reply(SceneProtocolTests.Response(0x0e, [frame[6]], "Name")); }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SceneNamesAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task ChangedPresetIsRejectedBeforeQueryingNames()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        midi.Send = _ => midi.Reply(SceneProtocolTests.Response(0x0d, [6, 0], "Other"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SceneNamesAsync(5, CancellationToken.None));
        Assert.Single(midi.Sent);
    }

    [Fact]
    public async Task StoredDumpCanArriveAsFragmentedAndCombinedCallbacks()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        midi.Send = _ =>
        {
            var bytes = StoredSceneTests.Fixture().SelectMany(x => x).ToArray();
            for (int i = 0; i < bytes.Length; i += 1000)
            {
                midi.Reply(bytes[i..Math.Min(i + 1000, bytes.Length)]);
            }
        };
        var result = await client.StoredScenesAsync(152, CancellationToken.None);
        Assert.Equal("Lead", result.Names[2]); Assert.Single(midi.Sent);
    }

    [Fact]
    public async Task CancellationAndDisposeReleasePendingReaders()
    {
        var midi = new FakeMidi(); var client = new PresetNameClient(midi);
        using var cts = new CancellationTokenSource();
        var pending = client.SceneNamesAsync(0, cts.Token);
        cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var queued = client.CurrentPresetAsync(CancellationToken.None);
        client.Dispose(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
    }

    [Fact]
    public async Task TimedOutReplyIsDrainedBeforeAnotherRequest()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync(0, TimeSpan.FromMilliseconds(20), CancellationToken.None));
        midi.Reply(SceneProtocolTests.Response(0x0d, [0, 0], "Late"));
        midi.Send = _ => midi.Reply(SceneProtocolTests.Response(0x0d, [0, 0], "Fresh"));
        var result = await client.QueryAsync(0, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal("Fresh", result.PresetName);
    }

    private sealed class FakeMidi : IMidiManager
    {
        public event EventHandler<byte[]>? SysexMessageReceived;
        public event EventHandler<int>? PresetChangeReceived;
        public event EventHandler<string>? LogMessage { add { } remove { } }
        public event EventHandler<NoteOnEventArgs>? NoteOnReceived { add { } remove { } }
        public List<byte[]> Sent { get; } = [];
        public Action<byte[]>? Send;
        public void Reply(byte[] data) => SysexMessageReceived?.Invoke(this, data);
        public void Changed() => PresetChangeReceived?.Invoke(this, 1);
        public bool InputOpen => true;
        public bool OutputOpen => true;
        public IReadOnlyCollection<string> ThruInputPorts => [];
        public IReadOnlyList<string> GetInputPortNames() => [];
        public IReadOnlyList<string> GetOutputPortNames() => [];
        public bool OpenInput(string name, out string? error) { error = null; return true; }
        public bool OpenOutput(string name, out string? error) { error = null; return true; }
        public bool OpenThruInput(string name, out string? error) { error = null; return true; }
        public void CloseInput() { }
        public void CloseOutput() { }
        public void CloseThruInput(string name) { }
        public void CloseAllThruInputs() { }
        public bool SendBankAndPC(int bank, int pc, int channel) => throw new InvalidOperationException("Unexpected write");
        public bool SendFavorite(int bank, int pc, int scene, int cc, int channel) => throw new InvalidOperationException("Unexpected write");
        public bool SendScene(int scene, int cc, int channel) => throw new InvalidOperationException("Unexpected write");
        public bool SendSysEx(byte[] frame) { Sent.Add(frame); Send?.Invoke(frame); return true; }
        public void Dispose() { }
    }
}
