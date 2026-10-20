using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public class SceneClientTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(4, true)]
    public async Task CancelledDumpDrainsToItsValidatedEndThenAllowsFreshReads(int prefixFrames, bool readSameSlot)
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        var oldFrames = StoredSceneTests.Fixture();
        midi.Send = _ => { foreach (var frame in oldFrames[..prefixFrames]) { midi.Reply(frame); } };
        using var cancelled = new CancellationTokenSource();
        var oldRead = client.StoredScenesAsync(152, cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldRead);

        midi.Send = request =>
        {
            if (request[5] == 0x03)
            { foreach (var frame in StoredSceneTests.Fixture(presetName: "Fresh saved preset")) { midi.Reply(frame); } }
            else if (request[5] == 0x0d) { midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Current preset")); }
            else { midi.Reply(SysexProtocol.Frame(0x0c, [3])); }
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        if (readSameSlot)
        {
            var next = client.StoredScenesAsync(152, deadline.Token);
            Assert.Single(midi.Sent);
            foreach (var frame in oldFrames[prefixFrames..]) { midi.Reply(frame); }
            var saved = await next;
            Assert.Equal("Fresh saved preset", saved.PresetName);
        }
        else
        {
            var next = client.CurrentStateAsync(deadline.Token);
            Assert.Single(midi.Sent);
            foreach (var frame in oldFrames[prefixFrames..]) { midi.Reply(frame); }
            var state = await next;
            Assert.Equal(5, state.Preset.Slot); Assert.Equal(3, state.Scene.Index);
        }
        Assert.True(midi.Sent.Count > 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledDumpWithoutAValidEndRemainsQuarantined(bool corruptFooter)
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        var frames = StoredSceneTests.Fixture();
        midi.Send = _ => { foreach (var frame in frames[..4]) { midi.Reply(frame); } };
        using var cancelled = new CancellationTokenSource();
        var old = client.StoredScenesAsync(152, cancelled.Token);
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        midi.Send = _ => throw new InvalidOperationException("A new query must wait for the incomplete or corrupt dump.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var next = client.CurrentStateAsync(deadline.Token);
        foreach (var frame in frames[4..^1]) { midi.Reply(frame); }
        if (corruptFooter) { frames[^1][^2] ^= 1; midi.Reply(frames[^1]); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
        Assert.Single(midi.Sent);
    }

    [Fact]
    public async Task CurrentStateRejectsPresetChangeAwayAndBackAndAllowsRetry()
    {
        var midi = new FakeMidi(); using var client = new PresetNameClient(midi);
        bool changeDuringRead = true;
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d) { midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Preset")); }
            else
            {
                if (changeDuringRead) { midi.Changed(); midi.Changed(); }
                midi.Reply(SysexProtocol.Frame(0x0c, [3]));
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CurrentStateAsync(CancellationToken.None));
        changeDuringRead = false;
        var state = await client.CurrentStateAsync(CancellationToken.None);
        Assert.Equal(5, state.Preset.Slot);
        Assert.Equal(3, state.Scene.Index);
        Assert.All(midi.Sent, frame => Assert.Contains(frame[5], new byte[] { 0x0d, 0x0c }));
    }

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
