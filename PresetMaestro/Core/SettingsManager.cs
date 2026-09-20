using System.Text.Json;

namespace PresetMaestro.Core;

public static class SettingsManager
{
    public static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PresetMaestro", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            return Load(SettingsPath);
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Save(settings, SettingsPath);
        }
        catch { }
    }

    internal static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts)
               ?? new AppSettings();
    }

    internal static void Save(AppSettings settings, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOpts));
    }
}
