namespace PresetMaestro.Midi;

public sealed record MidiPorts(IReadOnlyList<string> Inputs, IReadOnlyList<string> Outputs);

/// <summary>Coalesces discovery requests so a slow driver never accumulates polling work.</summary>
public sealed class MidiPortDiscovery(IMidiManager midi)
{
    private readonly object _gate = new();
    private Task<MidiPorts>? _pending;
    public MidiPorts Current { get; private set; } = new([], []);

    public async Task<MidiPorts> RefreshAsync(CancellationToken token = default)
    {
        Task<MidiPorts> pending;
        lock (_gate)
        {
            if (_pending is null || _pending.IsCompleted) { _pending = midi.DiscoverPortsAsync(); }
            pending = _pending;
        }
        // Cancelling a caller does not start another driver enumeration while the
        // first is still running. Its result can be reused by the next caller.
        var ports = await pending.WaitAsync(token).ConfigureAwait(false);
        lock (_gate) { if (ReferenceEquals(pending, _pending)) { Current = ports; } }
        return ports;
    }
}
