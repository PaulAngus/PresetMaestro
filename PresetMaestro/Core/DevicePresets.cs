namespace PresetMaestro.Core;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DeviceModel>))]
public enum DeviceModel { FM9, FM3, AxeFxIII }

public static class DevicePresets
{
    public static int Capacity(DeviceModel model) => model switch
    {
        DeviceModel.FM9 or DeviceModel.FM3 => 512,
        DeviceModel.AxeFxIII => 1024,
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
}
