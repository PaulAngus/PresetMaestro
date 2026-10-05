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
                var result = await query(slot, TimeSpan.FromSeconds(1.5), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (result.Slot != slot) { throw new InvalidDataException($"Expected preset name {slot}, received {result.Slot}."); }
                Names[slot] = result.PresetName;
                RefreshedSlots.Add(slot);
                failures = 0;
            }
            catch (TimeoutException)
            {
                log($"PRESET NAMES: slot {slot} timed out");
                if (++failures >= 3) { throw new IOException("Preset sync stopped after three unanswered requests."); }
            }
            Volatile.Write(ref _checkedSlots, slot + 1);
        }
    }, token);
}
