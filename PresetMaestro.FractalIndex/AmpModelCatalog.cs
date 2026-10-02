namespace PresetMaestro.FractalIndex;

public sealed record AmpModelEntry(int Id, string FractalName, string? RealAmpFamily, string Evidence);

public sealed record AmpModelResolution(int Id, string DisplayName, string? RealAmpFamily, string Evidence, bool IsKnown);

/// <summary>A catalog is scoped to a device family or a specific variant and firmware interval.</summary>
public sealed class AmpModelCatalog(
    string family,
    IReadOnlyDictionary<int, AmpModelEntry> entries,
    FractalDeviceVariant? variant = null,
    Version? minFirmware = null,
    Version? maxFirmware = null)
{
    public string Family { get; } = family;
    public FractalDeviceVariant? Variant { get; } = variant;
    public Version? MinFirmware { get; } = minFirmware;
    public Version? MaxFirmware { get; } = maxFirmware;
    public IReadOnlyDictionary<int, AmpModelEntry> Entries { get; } = entries;

    public bool AppliesTo(FractalDeviceDefinition device, Version? firmware) =>
        Family == device.CatalogFamily &&
        (Variant is null || Variant == device.Variant) &&
        (MinFirmware is null || firmware is not null && firmware >= MinFirmware) &&
        (MaxFirmware is null || firmware is not null && firmware <= MaxFirmware);
}

public sealed class AmpModelCatalogRegistry(IEnumerable<AmpModelCatalog> catalogs)
{
    private readonly AmpModelCatalog[] _catalogs = [.. catalogs];

    public AmpModelResolution Resolve(FractalDeviceDefinition device, Version? firmware, int modelId)
    {
        var matches = _catalogs.Where(catalog => catalog.AppliesTo(device, firmware))
            .OrderByDescending(catalog => catalog.Variant == device.Variant)
            .ThenByDescending(catalog => catalog.MinFirmware);
        foreach (var catalog in matches)
        {
            if (catalog.Entries.TryGetValue(modelId, out var entry))
            {
                return new AmpModelResolution(modelId, entry.FractalName, entry.RealAmpFamily, entry.Evidence, true);
            }
        }
        return new AmpModelResolution(modelId, $"Unknown Amp model #{modelId}", null, "unmapped", false);
    }

    // Only entries checked against the attached FM9 are seeded here. FM3 and Axe-Fx III
    // catalogs are independent; raw IDs remain searchable until their rosters are verified.
    public static AmpModelCatalogRegistry CreateStarter() => new(
    [
        new AmpModelCatalog("FM9", new Dictionary<int, AmpModelEntry>
        {
            [6] = new(6, "Class-A 15W TB", null, "FM9-Edit 12.00 / preset 126"),
            [17] = new(17, "Hipower Brilliant", null, "FM9-Edit 12.00 / preset 109"),
            [18] = new(18, "USA MK IV Rhythm 1", null, "FM9-Edit 12.00 / preset 109"),
            [141] = new(141, "Plexi 50W Jumped", null, "FM9-Edit 12.00 / preset 126"),
            [145] = new(145, "Plexi 100W Jumped", null, "FM9-Edit 12.00 / preset 126"),
            [163] = new(163, "Citrus A30 Dirty", null, "FM9-Edit 12.00 / preset 126"),
            [261] = new(261, "Plexi 2204", null, "FM9-Edit 12.00 / preset 126"),
            [271] = new(271, "Matchbox D-30 EF86", null, "FM9-Edit 12.00 / preset 126"),
            [277] = new(277, "Plexi 50W 6CA7", null, "FM9-Edit 12.00 / preset 126"),
            [326] = new(326, "Class-A 30W Brilliant", null, "FM9-Edit 12.00 / preset 126"),
        }, minFirmware: new Version(12, 0), maxFirmware: new Version(12, 0)),
        new AmpModelCatalog("AxeFxIII", new Dictionary<int, AmpModelEntry>()),
        new AmpModelCatalog("FM3", new Dictionary<int, AmpModelEntry>()),
    ]);
}
