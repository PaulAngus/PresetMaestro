namespace PresetMaestro.FractalIndex;

public enum FractalDeviceVariant
{
    FM9,
    AxeFxIIIOriginal,
    AxeFxIIIMarkII,
    AxeFxIIIMarkIITurbo,
    FM3,
}

/// <summary>Capacity and decoding rules for one connected device. FM3 and Axe-Fx III remain hardware candidates.</summary>
public sealed record FractalDeviceDefinition(
    FractalDeviceVariant Variant,
    byte ModelByte,
    int PresetSlots,
    int AmpColumns,
    bool AmpTypeInHeader,
    int AmpTypeParameterWordOffset,
    int GridRows,
    int GridColumns,
    int MaxAmpBlocks,
    string CatalogFamily)
{
    public static FractalDeviceDefinition For(FractalDeviceVariant variant) => variant switch
    {
        FractalDeviceVariant.FM9 => new(variant, 0x12, 512, 147, false, 4, 6, 14, 2, "FM9"),
        FractalDeviceVariant.FM3 => new(variant, 0x11, 512, 144, false, 0, 4, 12, 1, "FM3"),
        FractalDeviceVariant.AxeFxIIIOriginal => new(variant, 0x10, 512, 142, true, 0, 6, 14, 2, "AxeFxIII"),
        FractalDeviceVariant.AxeFxIIIMarkII => new(variant, 0x10, 1024, 142, true, 0, 6, 14, 2, "AxeFxIII"),
        FractalDeviceVariant.AxeFxIIIMarkIITurbo => new(variant, 0x10, 1024, 142, true, 0, 6, 14, 2, "AxeFxIII"),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };
}
