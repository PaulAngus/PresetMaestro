using System.Text.Json;
using System.Text.Json.Nodes;

namespace PresetMaestro.Core;

public static class SettingsManager
{
    public static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PresetMaestro", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load() => Load(SettingsPath);

    public static void Save(AppSettings settings) => Save(settings, SettingsPath);

    internal static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        string json = File.ReadAllText(path);
        json = MigrateLegacyMidiNoteMap(json);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
    }

    private static string MigrateLegacyMidiNoteMap(string json)
    {
        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null || root.ContainsKey("MidiNoteMap") || !root.ContainsKey("NoteMap"))
            {
                return json;
            }

            root["MidiNoteMap"] = root["NoteMap"]?.DeepClone();
            root.Remove("NoteMap");
            // Loading must not rewrite the file before ProfileStore can preserve its
            // pre-migration backup. Normal saves already use atomic replacement.
            return root.ToJsonString();
        }
        catch
        {
            return json;
        }
    }

    internal static void Save(AppSettings settings, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LegacyFavoritesStorage.Write(path, JsonSerializer.Serialize(settings, JsonOpts));
    }
}
