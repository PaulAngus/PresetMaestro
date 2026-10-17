using PresetMaestro.Midi;

namespace PresetMaestro.Tests;

public sealed class MidiCapabilityRefreshTests
{
    [Fact]
    public async Task DiscoveryRefreshesCapabilitiesBeforeEnumeratingOnAWorker()
    {
        var calls = new List<string>();
        using var midi = new MidiManager(
            () => { calls.Add("inputs"); return [("input", "Device input")]; },
            _ => throw new InvalidOperationException("Discovery must not open ports."),
            () => { calls.Add("outputs"); return [(0, "Device output")]; },
            _ => throw new InvalidOperationException("Discovery must not open ports."),
            () => { Assert.True(Thread.CurrentThread.IsThreadPoolThread); calls.Add("refresh"); });

        MidiPorts ports = await midi.DiscoverPortsAsync();

        Assert.Equal(["refresh", "inputs", "outputs"], calls);
        Assert.Equal(["Device input"], ports.Inputs);
        Assert.Equal(["Device output"], ports.Outputs);
    }

    [Fact]
    public async Task ExplicitRefreshCanRecoverAfterEarlierDiscoveryFailed()
    {
        int attempts = 0;
        using var midi = new MidiManager(() => [("input", "Recovered input")],
            _ => throw new InvalidOperationException("Discovery must not open ports."),
            () => [(0, "Recovered output")],
            _ => throw new InvalidOperationException("Discovery must not open ports."),
            () => { if (++attempts == 1) { throw new IOException("Service is starting."); } });
        var discovery = new MidiPortDiscovery(midi);

        await Assert.ThrowsAsync<IOException>(() => discovery.RefreshAsync());
        MidiPorts recovered = await discovery.RefreshAsync();

        Assert.Equal(2, attempts);
        Assert.Equal(["Recovered input"], recovered.Inputs);
        Assert.Equal(["Recovered output"], recovered.Outputs);
        Assert.Same(recovered, discovery.Current);
    }
}
