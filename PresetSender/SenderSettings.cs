using System.Text.Json;

namespace PresetSender;

internal sealed class SenderSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string? InputId { get; set; }
    public string? OutputName { get; set; }
    public int OutputOccurrence { get; set; }
    public bool? VirtualOutput { get; set; }
    public int Offset { get; set; }
    public int OutputChannel { get; set; } = 1;
    public int InputChannel { get; set; }
    public bool ForwardProgramChanges { get; set; }
    public List<MidiBinding> Bindings { get; set; } = [];

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PresetSender", "settings.json");

    public static SenderSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? FromJson(File.ReadAllText(FilePath)) : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal static SenderSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<SenderSettings>(json) ?? new();
        settings.Offset = settings.Offset == 1 ? 1 : 0;
        settings.OutputChannel = Math.Clamp(settings.OutputChannel, 0, 16);
        settings.InputChannel = Math.Clamp(settings.InputChannel, 0, 16);
        settings.Bindings ??= [];
        settings.Bindings.RemoveAll(b => b is null || b.Kind is not (0x90 or 0xb0 or 0xc0) || b.Channel is < 1 or > 16 || b.Number is < 0 or > 127
            || !(b.Command is "Send" or "Clear" || (b.Command is { Length: 1 } && char.IsAsciiDigit(b.Command[0]))));
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporary, FilePath, true);
    }
}
