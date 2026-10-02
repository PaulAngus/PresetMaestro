namespace PresetMaestro.FractalIndex;

/// <summary>Content evidence for an assigned library, never a physical device identifier.</summary>
public sealed record LibraryMatchResult(bool IsSample, int Matched, int Compared, int Failed,
    string? Caution = null)
{
    public decimal? Percent => Compared == 0 ? null : 100m * Matched / Compared;
    public bool MeetsThreshold(int threshold) => Compared > 0 && Failed == 0 && Caution is null &&
        Matched * 100L >= Compared * (long)threshold;

    public string Describe(int threshold)
    {
        string scope = IsSample ? "Sample" : "Full scan";
        string evidence = Compared == 0 ? "no populated presets to compare" :
            $"{Matched} of {Compared} {(IsSample ? "sampled" : "populated")} presets match ({Percent:0.##}%)";
        string decision = MeetsThreshold(threshold) ? "consistent with this library" : "library match unconfirmed";
        return $"{scope}: {evidence} · threshold {threshold}% · {decision}." +
            (Failed > 0 ? $" {Failed} reads unavailable." : "") + (Caution is null ? "" : " " + Caution);
    }
}

public static class LibraryMatch
{
    // Empty slots and repeated copies do not provide independent sample evidence.
    public static bool IsPopulated(IndexedPreset preset) => !preset.NameOnlyEmpty &&
        preset.Name.Trim() != "<EMPTY>" && !string.IsNullOrEmpty(preset.ContentSha256);

    public static int[] SampleSlots(IndexScan baseline, int maximum = 12)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        var slots = baseline.Presets.Values.Where(IsPopulated).OrderBy(p => p.Slot)
            .DistinctBy(p => p.ContentSha256).Select(p => p.Slot).ToArray();
        if (slots.Length <= maximum) { return slots; }
        if (maximum == 1) { return [slots[slots.Length / 2]]; }
        return Enumerable.Range(0, maximum).Select(i => slots[i * (slots.Length - 1) / (maximum - 1)]).ToArray();
    }

    public static LibraryMatchResult Compare(IndexScan baseline, IReadOnlyDictionary<int, IndexedPreset> observed,
        bool sample, string? connectedDeviceName, string? firmware, IReadOnlyCollection<int>? requestedSlots = null)
    {
        var slots = sample ? requestedSlots?.ToArray() ?? SampleSlots(baseline) :
            baseline.Presets.Values.Where(IsPopulated).Select(p => p.Slot)
                .Union(observed.Values.Where(IsPopulated).Select(p => p.Slot)).ToArray();
        int matched = 0, failed = 0;
        foreach (int slot in slots)
        {
            if (!observed.TryGetValue(slot, out var current)) { failed++; continue; }
            if (baseline.Presets.TryGetValue(slot, out var previous) && IsPopulated(previous) && IsPopulated(current) &&
                previous.Variant == current.Variant && previous.ContentSha256 == current.ContentSha256) { matched++; }
        }
        string? caution = null;
        if (!string.IsNullOrWhiteSpace(baseline.ConnectedDeviceName) &&
            !string.Equals(baseline.ConnectedDeviceName.Trim(), connectedDeviceName?.Trim(), StringComparison.OrdinalIgnoreCase))
        { caution = "The device name differs or is unavailable."; }
        if (baseline.Firmware is not null && firmware is not null &&
            Version.TryParse(baseline.Firmware, out var oldVersion) && Version.TryParse(firmware, out var newVersion) && oldVersion != newVersion)
        { caution = "Firmware differs; saved preset fingerprints may have changed."; }
        return new(sample, matched, slots.Length, failed, caution);
    }

    public static async Task<LibraryMatchResult> CheckSampleAsync(PresetIndexReader reader, DeviceIndex cache,
        IndexScan baseline, string? connectedDeviceName, CancellationToken token)
    {
        var device = FractalDeviceDefinition.For(cache.Device.Variant);
        var firmware = Version.TryParse(cache.Device.Firmware, out var version) ? version : null;
        var slots = SampleSlots(baseline);
        var observed = new Dictionary<int, IndexedPreset>();
        int consecutiveFailures = 0;
        foreach (int slot in slots)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                observed[slot] = await reader.ReadAsync(device, firmware, slot, token).ConfigureAwait(false);
                consecutiveFailures = 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException)
            {
                if (++consecutiveFailures >= 3) { break; }
            }
        }
        token.ThrowIfCancellationRequested();
        return Compare(baseline, observed, true, connectedDeviceName, cache.Device.Firmware, slots);
    }
}
