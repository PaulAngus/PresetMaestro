#if FRACTAL_INDEX
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Midi;

internal static class FractalIndexDeviceAdapter
{
    public static DeviceModel ToDeviceModel(this FractalDeviceVariant variant) => variant switch
    {
        FractalDeviceVariant.FM9 => DeviceModel.FM9,
        FractalDeviceVariant.FM3 => DeviceModel.FM3,
        FractalDeviceVariant.AxeFxIII or FractalDeviceVariant.AxeFxIIIOriginal or
        FractalDeviceVariant.AxeFxIIIMarkII or
        FractalDeviceVariant.AxeFxIIIMarkIITurbo => DeviceModel.AxeFxIII,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };
}
#endif
