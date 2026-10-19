using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

internal sealed record AmpCompatibilityRow(int Id, string DeviceName, string CodeName, string Status)
{
    private static readonly AmpModelCatalogRegistry Catalogues = AmpModelCatalogRegistry.CreateStarter();
    public static AmpCompatibilityRow Compare(FractalDeviceDefinition definition, Version firmware, int id, string name)
    {
        var known = Catalogues.Resolve(definition, firmware, id);
        return new(id, name, known.DisplayName, !known.IsKnown ? "Unknown ID or firmware scope" : known.DisplayName != name ? "Name differs" : "Match");
    }
}
