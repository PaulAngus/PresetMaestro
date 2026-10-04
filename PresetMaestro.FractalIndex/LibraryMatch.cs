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
    public const int QuickMatchPresetCount = 16;

    // Explicit empty slots provide no populated content to sample.
    public static bool IsPopulated(IndexedPreset preset) => !preset.NameOnlyEmpty &&
        preset.Name.Trim() != "<EMPTY>" && !string.IsNullOrEmpty(preset.ContentSha256);

    // Names can rule out an obvious mismatch, but cannot prove that contents match.
    // Only freshly read names belong here; a cached fallback is not evidence.
    public static (int Matched, int Compared) CompareNames(IndexScan baseline, IReadOnlyDictionary<int, string> names)
    {
        var slots = baseline.Presets.Values.Where(IsPopulated).Select(p => p.Slot)
            .Union(names.Where(p => p.Value.Trim() != "<EMPTY>").Select(p => p.Key)).ToArray();
        int matched = slots.Count(slot => baseline.Presets.TryGetValue(slot, out var previous) && IsPopulated(previous) &&
            names.TryGetValue(slot, out var name) && string.Equals(previous.Name.Trim(), name.Trim(), StringComparison.Ordinal));
        return (matched, slots.Length);
    }

    public static int[] SampleSlots(IndexScan baseline, int maximum = QuickMatchPresetCount, Random? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        var slots = baseline.Presets.Values.Where(IsPopulated).OrderBy(p => p.Slot)
            .Select(p => p.Slot).Distinct().ToArray();
        (random ?? Random.Shared).Shuffle(slots);
        return slots.Take(maximum).ToArray();
    }

    public static LibraryMatchResult Compare(IndexScan baseline, IReadOnlyDictionary<int, IndexedPreset> observed,
        bool sample, string? connectedDeviceName, string? firmware, IReadOnlyCollection<int>? requestedSlots = null)
    {
        var slots = sample ? requestedSlots?.ToArray() ?? SampleSlots(baseline) :
            baseline.Presets.Values.Where(IsPopulated).Select(p => p.Slot)
                .Union(observed.Values.Where(IsPopulated).Select(p => p.Slot))
                .Union(requestedSlots?.Where(slot => !observed.ContainsKey(slot)) ?? []).ToArray();
        int matched = 0, failed = 0;
        foreach (int slot in slots)
        {
            if (!observed.TryGetValue(slot, out var current)) { failed++; continue; }
            if (baseline.Presets.TryGetValue(slot, out var previous) && IsPopulated(previous) && IsPopulated(current) &&
                previous.Variant == current.Variant && previous.ContentSha256 == current.ContentSha256 &&
                previous.SceneNames.SequenceEqual(current.SceneNames)) { matched++; }
        }
        string? caution = null;
        if (!string.IsNullOrWhiteSpace(baseline.ConnectedDeviceName) &&
            !string.Equals(baseline.ConnectedDeviceName.Trim(), connectedDeviceName?.Trim(), StringComparison.OrdinalIgnoreCase))
        { caution = "The device name differs or is unavailable."; }
        if (baseline.EffectiveFirmware is not null && firmware is not null &&
            Version.TryParse(baseline.EffectiveFirmware, out var oldVersion) && Version.TryParse(firmware, out var newVersion) && oldVersion != newVersion)
        { caution = "Firmware differs; saved preset fingerprints may have changed."; }
        if (baseline.EffectiveFirmware is not null && firmware is null)
        { caution = "Connected firmware is unavailable; the saved version has not been assumed."; }
        return new(sample, matched, slots.Length, failed, caution);
    }

    public static Task<LibraryMatchResult> CheckSampleAsync(PresetIndexReader reader, DeviceIndex cache,
        IndexScan baseline, string? connectedDeviceName, CancellationToken token,
        Action<LibraryMatchProgress>? progress = null) =>
        CheckAsync(reader, cache, baseline, connectedDeviceName, sample: true, progress, token);

    public static Task<LibraryMatchResult> CheckFullAsync(PresetIndexReader reader, DeviceIndex cache,
        IndexScan baseline, string? connectedDeviceName, CancellationToken token,
        Action<LibraryMatchProgress>? progress = null) =>
        CheckAsync(reader, cache, baseline, connectedDeviceName, sample: false, progress, token);

    private static async Task<LibraryMatchResult> CheckAsync(PresetIndexReader reader, DeviceIndex cache,
        IndexScan baseline, string? connectedDeviceName, bool sample, Action<LibraryMatchProgress>? progress,
        CancellationToken token)
    {
        var device = FractalDeviceDefinition.For(cache.Device.Variant);
        var firmware = Version.TryParse(cache.Device.Firmware, out var version) ? version : null;
        var slots = sample ? SampleSlots(baseline) : Enumerable.Range(0, device.PresetSlots).ToArray();
        var observed = new Dictionary<int, IndexedPreset>();
        int consecutiveFailures = 0, checkedSlots = 0, failed = 0;
        progress?.Invoke(new(0, slots.Length, 0));
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
                failed++;
                consecutiveFailures++;
            }
            progress?.Invoke(new(++checkedSlots, slots.Length, failed));
            if (consecutiveFailures >= 3) { break; }
        }
        token.ThrowIfCancellationRequested();
        return Compare(baseline, observed, sample, connectedDeviceName, cache.Device.Firmware, slots);
    }
}

public sealed record LibraryMatchProgress(int Checked, int Total, int Failed);
