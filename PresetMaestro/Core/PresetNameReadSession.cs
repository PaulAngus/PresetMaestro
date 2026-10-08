using PresetNameSync.Core;

namespace PresetMaestro.Core;

/// <summary>FM9 name-read state, independent of controls and their refresh timer.</summary>
internal sealed class PresetNameReadSession(Dictionary<int, string> cachedNames)
{
    private int _checkedSlots;
    public Dictionary<int, string> Names { get; } = new(cachedNames);
    public HashSet<int> RefreshedSlots { get; } = [];
    public int CheckedSlots => Volatile.Read(ref _checkedSlots);

    public Task ReadAsync(Func<int, TimeSpan, CancellationToken, Task<PresetNameResult>> query,
        Action<string> log, CancellationToken token) => Task.Run(async () =>
    {
        int failures = 0;
        for (int slot = 0; slot < 512; slot++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var result = await ReadDiagnostics.MeasureAsync("names.query", slot,
                    () => query(slot, TimeSpan.FromSeconds(1.5), token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Slot != slot) { throw new InvalidDataException($"Expected preset name {slot}, received {result.Slot}."); }
                Names[slot] = result.PresetName;
                RefreshedSlots.Add(slot);
                failures = 0;
            }
            catch (TimeoutException)
            {
                token.ThrowIfCancellationRequested();
                log($"PRESET NAMES: slot {slot} timed out");
                if (++failures >= 3) { throw new IOException("Preset sync stopped after three unanswered requests."); }
            }
            Volatile.Write(ref _checkedSlots, slot + 1);
        }
        // Recover isolated missed replies without rereading the 511 successful names.
        // The MIDI client's shared gate and late-reply quarantine still apply.
        failures = 0;
        for (int slot = 0; slot < 512; slot++)
        {
            if (RefreshedSlots.Contains(slot)) { continue; }
            token.ThrowIfCancellationRequested();
            ReadDiagnostics.Record("names.retry", slot);
            log($"PRESET NAMES: retrying slot {slot} after no reply (attempt 2 of 2)");
            try
            {
                var result = await ReadDiagnostics.MeasureAsync("names.query", slot,
                    () => query(slot, TimeSpan.FromSeconds(1.5), token)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Slot != slot) { throw new InvalidDataException($"Expected preset name {slot}, received {result.Slot}."); }
                Names[slot] = result.PresetName;
                RefreshedSlots.Add(slot);
                failures = 0;
            }
            catch (TimeoutException)
            {
                token.ThrowIfCancellationRequested();
                log($"PRESET NAMES: slot {slot} did not reply after retry; cached name kept");
                // Stop recovery promptly if the connection has become unavailable.
                if (++failures >= 3) { break; }
            }
        }
        token.ThrowIfCancellationRequested();
    }, token);
}
