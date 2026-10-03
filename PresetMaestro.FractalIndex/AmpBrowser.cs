using System.Text.Json;

namespace PresetMaestro.FractalIndex;

public sealed record RealAmpFamily(string Id, string Manufacturer, string Name, string[] Aliases,
    string Source, string Confidence, int[] ModelIds);

public sealed record BrowserAmpVariant(string Id, int ModelId, string Name, string SpecificModel,
    string IdentityEvidence, bool IdentityVerified, string MappingEvidence = "");

public sealed record BrowserAmpFamily(RealAmpFamily Family, BrowserAmpVariant[] Variants, int MatchingPresets,
    bool UsageComplete)
{
    public string UsageLabel => UsageComplete ? $"{MatchingPresets} presets" : MatchingPresets > 0
        ? $"≥{MatchingPresets} presets · usage unknown" : "usage unknown";
    public bool Matches(string search) => string.IsNullOrWhiteSpace(search) ||
        new[] { Family.Manufacturer, Family.Name }.Concat(Family.Aliases)
            .Concat(Variants.SelectMany(v => new[] { v.Name, v.SpecificModel, v.ModelId.ToString() }))
            .Any(value => value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record AmpBrowserDirectory(string Version, string Coverage, bool IsComplete, BrowserAmpFamily[] Families,
    bool IsReferencePreview = false, bool HasUsageData = false);

/// <summary>Curated mapping and candidate roster are independent of a saved scan and of profile tags.</summary>
public static class AmpBrowserCatalog
{
    public const string CatalogVersion = "2026-10-02.2";
    public const string RosterSource = "https://github.com/TheAndrewStaker/mcp-midi-control/blob/12e3bb735f8e8527b277e9567f3f70196879541a/packages/fractal-gen3/src/gen3BodyTables.ts";
    public const string Wiki = "https://wiki.fractalaudio.com/wiki/index.php?title=Amp_models";
    private sealed record RosterEntry(int Id, string Name);
    private sealed record AmpDetail(int ModelId, string SpecificModel, string Relationship, string Notes, string MappingEvidence);
    private static readonly RosterEntry[] Roster = Read<RosterEntry[]>("fm9-12-candidates.json");
    private static readonly RealAmpFamily[] Mappings = Read<RealAmpFamily[]>("real-amp-families.json");
    private static readonly Dictionary<int, AmpDetail> Details = Read<AmpDetail[]>("real-amp-details.json").ToDictionary(d => d.ModelId);
    private static readonly AmpModelCatalogRegistry VerifiedNames = AmpModelCatalogRegistry.CreateStarter();
    private static readonly Lazy<Dictionary<int, (RealAmpFamily Family, BrowserAmpVariant Variant)>> DisplayModels = new(() =>
        Build(new DeviceIndex { Device = new(Guid.Empty, "", FractalDeviceVariant.FM9, "12.00") }).Families
            .SelectMany(f => f.Variants.Select(v => (Family: f.Family, Variant: v))).GroupBy(x => x.Variant.ModelId)
            .ToDictionary(g => g.Key, g => g.First()));

    private static T Read<T>(string file)
    {
        using var stream = typeof(AmpBrowserCatalog).Assembly.GetManifestResourceStream($"PresetMaestro.FractalIndex.Catalog.{file}")
            ?? throw new InvalidDataException("Missing Amp catalogue: " + file);
        return JsonSerializer.Deserialize<T>(stream)!;
    }

    public static bool HasCandidateRoster(FractalDeviceVariant variant, string? firmware) =>
        variant == FractalDeviceVariant.FM9 && Version.TryParse(firmware, out var version) && version == new Version(12, 0);

    public static AmpBrowserDirectory Build(DeviceIndex? cache)
    {
        if (cache is null) { return new(CatalogVersion, "Assign a device library in Config to browse its amp catalogue.", false, []); }
        var device = FractalDeviceDefinition.For(cache.Device.Variant);
        string? firmware = cache.Device.Firmware;
        bool preview = cache.Device.Variant == FractalDeviceVariant.FM9 && string.IsNullOrWhiteSpace(firmware);
        bool candidates = preview || HasCandidateRoster(cache.Device.Variant, firmware);
        var version = Version.TryParse(firmware, out var parsed) ? parsed : null;
        // A scan from an earlier firmware cannot be joined to the current roster's raw IDs.
        var scan = cache.Browsable;
        bool compatibleScan = !preview && SameFirmware(scan?.Firmware, firmware);
        var presets = compatibleScan ? scan?.Presets.Values.Where(p => p.Variant == cache.Device.Variant).ToArray() ?? [] : [];
        bool complete = compatibleScan && cache.Committed is { Status: "Complete", FinishedAt: not null } committed &&
            committed.Errors.Count == 0 && committed.Presets.Count == device.PresetSlots &&
            Enumerable.Range(0, device.PresetSlots).All(committed.Presets.ContainsKey);
        var names = candidates ? Roster.ToDictionary(e => e.Id, e => e.Name) : new Dictionary<int, string>();
        if (candidates) { names[271] = "Matchbox D-30 EF86"; }
        foreach (var channel in presets.SelectMany(p => p.Amps).SelectMany(a => a.Channels))
        {
            if (!names.ContainsKey(channel.Model.Id))
            { names[channel.Model.Id] = channel.Model.IsKnown ? channel.Model.DisplayName : $"Unknown Amp model #{channel.Model.Id}"; }
        }
        var families = new List<BrowserAmpFamily>();
        var assigned = new HashSet<int>();
        if (candidates)
        {
            foreach (var mapping in Mappings)
            {
                var variants = mapping.ModelIds.Where(names.ContainsKey).Select(id => Variant(id, names[id])).ToArray();
                if (variants.Length == 0) { continue; }
                assigned.UnionWith(variants.Select(v => v.ModelId));
                families.Add(Family(mapping, variants));
            }
        }
        foreach (var (id, name) in names.Where(p => !assigned.Contains(p.Key)))
        {
            families.Add(Family(new($"unmapped:{device.CatalogFamily}:{firmware ?? "unknown"}:{id}", "Unmapped", name,
                [], "Real amplifier mapping has not been reviewed.", "unmapped", [id]), [Variant(id, name)]));
        }
        string coverage = preview
            ? "FM9 12.00 reference catalogue preview · Library firmware is missing. Preset usage and matching are unavailable until firmware is recorded and a matching scan is saved."
            : candidates
            ? "Catalogue incomplete · FM9 12.00 candidates; firmware availability needs review. Verified names are marked in details."
            : $"Catalogue incomplete · No validated roster for {device.CatalogFamily} {firmware ?? "unknown firmware"}. Showing models observed in this library only.";
        if (!preview && !compatibleScan && scan is not null) { coverage += " Scan firmware differs; sync again to check usage."; }
        return new(CatalogVersion, coverage, false, families.OrderBy(f => f.Family.Manufacturer == "Unmapped" ? 2 : f.Family.Manufacturer == "Fractal originals" ? 1 : 0)
            .ThenBy(f => f.Family.Manufacturer, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Family.Name, StringComparer.OrdinalIgnoreCase).ToArray(), preview, presets.Length > 0);

        BrowserAmpVariant Variant(int id, string name)
        {
            var verified = VerifiedNames.Resolve(device, version, id);
            // Attribution research never promotes a candidate DSP ID to a verified identity.
            AmpDetail? detail = candidates ? Details.GetValueOrDefault(id) : null;
            string specific = detail?.SpecificModel ?? name;
            return new($"{device.CatalogFamily}:{(preview ? "12.00" : firmware ?? "unknown")}:{id}", id, name, specific,
                preview ? "FM9 12.00 reference only; not matched to this library's unknown firmware. " + RosterSource :
                verified.IsKnown ? verified.Evidence : candidates ? "Candidate identity from open roster; device confirmation pending. " + RosterSource : "Saved scan identity; mapping needs review.", !preview && verified.IsKnown,
                detail is null ? "Real amplifier attribution has not been joined to this identity." :
                    $"{detail.Relationship}. {detail.Notes}\nManufacturer/model sources (shared lineage): {detail.MappingEvidence}");
        }
        BrowserAmpFamily Family(RealAmpFamily family, BrowserAmpVariant[] variants) => new(family, variants,
            presets.Count(p => p.Amps.Any(a => a.Channels.Any(c => variants.Any(v => v.ModelId == c.Model.Id)))), complete);
    }

    public static bool SameFirmware(string? a, string? b) => Version.TryParse(a, out var av) && Version.TryParse(b, out var bv)
        ? av == bv : string.Equals(a, b, StringComparison.Ordinal);

    public static IndexedPreset ResolveForDisplay(IndexedPreset preset, string? firmware)
    {
        if (!HasCandidateRoster(preset.Variant, firmware)) { return preset; }
        var variants = DisplayModels.Value;
        return preset with
        {
            Amps = preset.Amps.Select(a => a with
            {
                Channels = a.Channels.Select(c =>
            variants.TryGetValue(c.Model.Id, out var entry) ? c with
            {
                Model = new(c.Model.Id, entry.Variant.Name,
                entry.Family.Confidence == "unmapped" ? null : entry.Family.Manufacturer + " " + entry.Family.Name,
                entry.Variant.IdentityEvidence, true)
            } : c).ToArray()
            }).ToArray()
        };
    }
}

public sealed record AmpContainsFilter(Guid DeviceId, FractalDeviceVariant Variant, string? Firmware, string Label, int[] ModelIds)
{
    public bool AppliesTo(DeviceIndex? cache) => !string.IsNullOrWhiteSpace(Firmware) && cache is not null && cache.Device.Id == DeviceId && cache.Device.Variant == Variant &&
        AmpBrowserCatalog.SameFirmware(Firmware, cache.Device.Firmware) && AmpBrowserCatalog.SameFirmware(Firmware, cache.Browsable?.Firmware);
    public bool Matches(IndexedPreset preset) => preset.Variant == Variant && ModelIds.Any(preset.Contains);
    public string[] MatchingChannels(IndexedPreset preset) => preset.Amps.SelectMany(a => a.Channels
        .Where(c => ModelIds.Contains(c.Model.Id)).Select(c => $"Amp {a.BlockNumber} / {(char)('A' + c.Channel)} · {c.Model.DisplayName}")).ToArray();
}
