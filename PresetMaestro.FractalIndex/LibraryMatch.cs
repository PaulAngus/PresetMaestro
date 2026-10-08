namespace PresetMaestro.FractalIndex;

/// <summary>Content evidence for an assigned library, never a physical device identifier.</summary>
public sealed record LibraryMatchResult(bool IsSample, int Matched, int Compared, int Failed,
    string? Caution = null)
{
    public LibraryPresetDifference[] Differences { get; init; } = [];
    public LibraryValueDifference[] MetadataDifferences { get; init; } = [];
    public int[] RequestedSlots { get; init; } = [];
    public bool IncludesLegacyFingerprints { get; init; }

    // Exact evidence only: every compared preset must match. Any difference is shown
    // to the user rather than tolerated by a percentage.
    public bool IsConsistent => Compared > 0 && Failed == 0 && Caution is null && Matched == Compared;

    /// <param name="otherNameDifferences">Preset-name differences found outside the compared slots.</param>
    public string Describe(int otherNameDifferences = 0)
    {
        string scope = IsSample ? "Sample" : "Full scan";
        string evidence = Compared == 0 ? "no populated presets to compare" :
            $"{Matched} of {Compared} {(IsSample ? "sampled" : "populated")} presets match";
        if (otherNameDifferences > 0)
        { evidence += $" · {otherNameDifferences} other preset {(otherNameDifferences == 1 ? "name differs" : "names differ")}"; }
        string decision = IsConsistent && otherNameDifferences == 0 ? "consistent with this device" : "device match unconfirmed";
        return $"{scope}: {evidence} · {decision}." +
            (Failed > 0 ? $" {Failed} reads unavailable." : "") + (Caution is null ? "" : " " + Caution);
    }
}

public static class LibraryMatch
{
    public const int QuickMatchPresetCount = 16;

    // Explicit empty slots provide no populated content to sample.
    public static bool IsPopulated(IndexedPreset preset) => !preset.NameOnlyEmpty &&
        preset.Name.Trim() != "<EMPTY>" && !string.IsNullOrEmpty(preset.ContentSha256);

    // Names can reveal differences, but cannot prove that contents match.
    // Only freshly read names belong here; a cached fallback is not evidence.
    public static LibraryPresetDifference[] NameDifferences(IndexScan baseline, IReadOnlyDictionary<int, string> names) =>
        baseline.Presets.Values.Where(IsPopulated).Select(p => p.Slot)
            .Union(names.Where(p => p.Value.Trim() != "<EMPTY>").Select(p => p.Key)).Order()
            .Where(slot => !baseline.Presets.TryGetValue(slot, out var previous) || !IsPopulated(previous) ||
                !names.TryGetValue(slot, out var current) || !string.Equals(previous.Name.Trim(), current.Trim(), StringComparison.Ordinal))
            .Select(slot => new LibraryPresetDifference(slot, baseline.Presets.GetValueOrDefault(slot)?.Name,
                names.GetValueOrDefault(slot), false,
                [new("Preset name", baseline.Presets.GetValueOrDefault(slot)?.Name ?? "Empty", names.GetValueOrDefault(slot) ?? "Unavailable")])).ToArray();

    public static int[] SampleSlots(IndexScan baseline, int maximum = QuickMatchPresetCount, Random? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
        var slots = baseline.Presets.Values.Where(IsPopulated).OrderBy(p => p.Slot)
            .Select(p => p.Slot).Distinct().ToArray();
        (random ?? Random.Shared).Shuffle(slots);
        return slots.Take(maximum).ToArray();
    }

    public static LibraryMatchResult Compare(IndexScan baseline, IReadOnlyDictionary<int, IndexedPreset> observed,
        bool sample, string? connectedDeviceName, string? firmware, IReadOnlyCollection<int>? requestedSlots = null,
        IReadOnlyDictionary<int, string>? readErrors = null)
    {
        var slots = sample ? requestedSlots?.ToArray() ?? SampleSlots(baseline) :
            baseline.Presets.Values.Where(IsPopulated).Select(p => p.Slot)
                .Union(observed.Values.Where(IsPopulated).Select(p => p.Slot))
                .Union(requestedSlots?.Where(slot => !observed.ContainsKey(slot)) ?? []).ToArray();
        int matched = 0, failed = 0;
        var differences = new List<LibraryPresetDifference>();
        bool legacy = false;
        foreach (int slot in slots)
        {
            if (!observed.TryGetValue(slot, out var current))
            {
                failed++;
                differences.Add(new(slot, baseline.Presets.GetValueOrDefault(slot)?.Name, null, false, [],
                    readErrors?.GetValueOrDefault(slot) ?? "This preset could not be read; no content comparison was made."));
                continue;
            }
            baseline.Presets.TryGetValue(slot, out var previous);
            bool canIgnoreBypass = previous?.BypassIgnoredSha256?.StartsWith(BypassIgnoredFingerprint.Prefix, StringComparison.Ordinal) == true &&
                current.BypassIgnoredSha256?.StartsWith(BypassIgnoredFingerprint.Prefix, StringComparison.Ordinal) == true;
            legacy |= !canIgnoreBypass && previous is not null && IsPopulated(previous) && IsPopulated(current);
            bool contentMatches = canIgnoreBypass ? previous!.BypassIgnoredSha256 == current.BypassIgnoredSha256
                : previous?.ContentSha256 == current.ContentSha256;
            if (previous is not null && IsPopulated(previous) && IsPopulated(current) &&
                previous.Variant == current.Variant && contentMatches &&
                previous.SceneNames.SequenceEqual(current.SceneNames)) { matched++; }
            else { differences.Add(LibraryPresetDifference.Between(slot, previous, current)); }
        }
        var cautions = new List<string>();
        var metadata = new List<LibraryValueDifference>();
        if (!string.IsNullOrWhiteSpace(baseline.ConnectedDeviceName) &&
            !string.Equals(baseline.ConnectedDeviceName.Trim(), connectedDeviceName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            cautions.Add("The device name differs or is unavailable.");
            metadata.Add(new("Device name", baseline.ConnectedDeviceName, connectedDeviceName ?? "Unavailable"));
        }
        if (baseline.EffectiveFirmware is not null && firmware is not null &&
            Version.TryParse(baseline.EffectiveFirmware, out var oldVersion) && Version.TryParse(firmware, out var newVersion) && oldVersion != newVersion)
        {
            cautions.Add("Firmware differs; saved preset fingerprints may have changed.");
            metadata.Add(new("Firmware", baseline.EffectiveFirmware, firmware));
        }
        if (baseline.EffectiveFirmware is not null && firmware is null)
        {
            cautions.Add("Connected firmware is unavailable; the saved version has not been assumed.");
            metadata.Add(new("Firmware", baseline.EffectiveFirmware, "Unavailable"));
        }
        return new(sample, matched, slots.Length, failed, cautions.Count == 0 ? null : string.Join(" ", cautions))
        { Differences = differences.OrderBy(d => d.Slot).ToArray(), MetadataDifferences = [.. metadata], RequestedSlots = slots, IncludesLegacyFingerprints = legacy };
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
        var device = FractalDeviceDefinition.For(cache.Device);
        var firmware = Version.TryParse(cache.Device.Firmware, out var version) ? version : null;
        var slots = sample ? SampleSlots(baseline) : Enumerable.Range(0, device.PresetSlots).ToArray();
        var observed = new Dictionary<int, IndexedPreset>();
        var readErrors = new Dictionary<int, string>();
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
                readErrors[slot] = ex.Message;
                failed++;
                consecutiveFailures++;
            }
            progress?.Invoke(new(++checkedSlots, slots.Length, failed));
            if (consecutiveFailures >= 3)
            {
                foreach (int unread in slots.Skip(checkedSlots))
                { readErrors[unread] = "Not read: the check stopped after three consecutive read failures."; }
                break;
            }
        }
        token.ThrowIfCancellationRequested();
        return Compare(baseline, observed, sample, connectedDeviceName, cache.Device.Firmware, slots, readErrors);
    }
}

public sealed record LibraryMatchProgress(int Checked, int Total, int Failed);
