using System.Text.Json;

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

    private sealed record ObservedModel(int Id, string Name);

    private static Dictionary<int, AmpModelEntry> ObservedFm9Models()
    {
        using var stream = typeof(AmpModelCatalogRegistry).Assembly.GetManifestResourceStream(
            "PresetMaestro.FractalIndex.Catalog.fm9-12-candidates.json")
            ?? throw new InvalidDataException("Missing observed FM9 amp-name table.");
        var models = JsonSerializer.Deserialize<ObservedModel[]>(stream)
            ?? throw new InvalidDataException("Invalid observed FM9 amp-name table.");
        return models.ToDictionary(m => m.Id, m => new AmpModelEntry(m.Id, m.Name, null,
            "FM9 12.00 device amp-name table, read directly over USB MIDI on 2026-10-03; docs/catalog/fm9-12-device-roster.json"));
    }

    // All FM9 entries were read from the attached firmware-12.00 device. This
    // confirms ID/name pairs, not reference-amp years/circuits or other firmware.
    // FM3 and Axe-Fx III catalogs remain independent.
    public static AmpModelCatalogRegistry CreateStarter() => new(
    [
        new AmpModelCatalog("FM9", ObservedFm9Models(), minFirmware: new Version(12, 0), maxFirmware: new Version(12, 0)),
        new AmpModelCatalog("AxeFxIII", new Dictionary<int, AmpModelEntry>()),
        new AmpModelCatalog("FM3", new Dictionary<int, AmpModelEntry>()),
    ]);
}
