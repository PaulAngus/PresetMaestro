using System.Diagnostics;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class MidiReadPacingTests
{
    [Fact]
    public async Task SharedGateSpacesNameAndSceneQueriesForDeviceThatRejectsBursts()
    {
        var midi = new DeviceMidi();
        long? previous = null;
        int tooFast = 0;
        midi.Send = frame =>
        {
            long now = Stopwatch.GetTimestamp();
            if (previous is long last && Stopwatch.GetElapsedTime(last, now).TotalMilliseconds < 9)
            { tooFast++; return; }
            previous = now;
            midi.Reply(frame[5] == 0x0c ? SysexProtocol.Frame(0x0c, [2]) :
                SceneProtocolTests.Response(0x0d, [frame[6], frame[7]], "Name"));
        };
        using var client = new PresetNameClient(midi);
        var names = Enumerable.Range(0, 4).Select(slot => client.QueryAsync(slot, TimeSpan.FromSeconds(1), default)).ToArray();
        var scene = client.CurrentSceneAsync(default);
        Assert.Equal(new[] { 0, 1, 2, 3 }, (await Task.WhenAll(names)).Select(result => result.Slot));
        Assert.Equal(2, (await scene).Index);
        Assert.Equal(0, tooFast);
        Assert.Equal(5, midi.Sent.Count);
    }

    [Fact]
    public async Task CancellingPacedRequestDoesNotSendAndReleasesGate()
    {
        var midi = new DeviceMidi();
        midi.Send = frame => midi.Reply(SceneProtocolTests.Response(0x0d, [frame[6], frame[7]], "Name"));
        using var client = new PresetNameClient(midi);
        await client.QueryAsync(0, TimeSpan.FromSeconds(1), default);
        using var cancellation = new CancellationTokenSource();
        var pending = client.QueryAsync(1, TimeSpan.FromSeconds(1), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(midi.Sent);
        Assert.Equal(2, (await client.QueryAsync(2, TimeSpan.FromSeconds(1), default)).Slot);
    }

    [Fact]
    public async Task TimeoutIdentifiesUnrelatedTrafficWithoutLoggingItsPayload()
    {
        var timings = new List<ReadTiming>();
        using var capture = ReadDiagnostics.Capture(timings.Add);
        var midi = new DeviceMidi();
        midi.Send = _ =>
        {
            for (int i = 0; i < 4; i++) { midi.Reply(SysexProtocol.Frame(0x11, [1])); }
            midi.Reply(SceneProtocolTests.Response(0x0d, [9, 0], "Private name"));
        };
        using var client = new PresetNameClient(midi);
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync(0, TimeSpan.FromMilliseconds(30), default));
        var receive = Assert.Single(timings, timing => timing.Phase == "midi.receive").Receive!;
        Assert.Equal(4, receive.OpcodeCounts!["0x11"]);
        Assert.Equal(1, receive.OpcodeCounts["0x0D"]);
        Assert.Equal(1, receive.RejectedMatchingFrames);
        Assert.DoesNotContain("Private name", System.Text.Json.JsonSerializer.Serialize(receive));
    }
}
