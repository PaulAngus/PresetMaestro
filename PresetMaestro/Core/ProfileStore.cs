using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;

namespace PresetMaestro.Core;

public sealed class ProfileStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private List<string>? _profiles;
    public string DirectoryPath => Path.GetFullPath(directory);
    public string? StartupMessage { get; private set; }
    private string MachinePath => Path.Combine(directory, "settings.json");
    private string SettingsPath(string name) => Path.Combine(directory, ValidateName(name) + "-settings.json");
    private string FavoritesPath(string name) => Path.Combine(directory, ValidateName(name) + "-favorites.json");

    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 80 ||
            name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            name.Any(char.IsControl))
        {
            throw new ArgumentException("Use a profile name of 1–80 characters without filename symbols or leading/trailing spaces.");
        }

        return name;
    }

    public List<string> ListProfiles()
    {
        if (_profiles is null)
        {
            var json = File.Exists(MachinePath) ? JsonNode.Parse(File.ReadAllText(MachinePath)) : null;
            _profiles = json?[nameof(AppSettings.Profiles)]?.Deserialize<List<string>>() ?? ScanProfiles().Names;
        }
        return [.. _profiles];
    }

    private (List<string> Names, int Skipped) ScanProfiles()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                string filename = Path.GetFileName(file);
                foreach (string suffix in new[] { "-settings.json", "-favorites.json" })
                {
                    if (filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(filename[..^suffix.Length]);
                    }
                }
            }
        }
        var valid = names.Where(IsReadable).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return (valid, names.Count - valid.Count);
    }

    private bool IsReadable(string name)
    {
        try { LoadProfile(name); LoadFavorites(name); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
        { return false; }
    }

    public (int Added, int Skipped) Rescan(AppSettings settings)
    {
        var previous = ListProfiles();
        var scan = ScanProfiles();
        int added = scan.Names.Except(previous, StringComparer.OrdinalIgnoreCase).Count();
        PersistCatalog(scan.Names);
        settings.Profiles = ListProfiles();
        return (added, scan.Skipped);
    }

    private void PersistCatalog(IEnumerable<string> names)
    {
        var updated = names.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var json = File.Exists(MachinePath) ? JsonNode.Parse(File.ReadAllText(MachinePath))!.AsObject() : new JsonObject();
        json[nameof(AppSettings.Profiles)] = JsonSerializer.SerializeToNode(updated);
        WriteJson(MachinePath, json);
        _profiles = updated;
    }

    public AppSettings LoadSettings()
    {
        var settings = SettingsManager.Load(MachinePath);
        // A missing ActiveProfile identifies the pre-profile format. Preserve both originals.
        bool legacy = !File.Exists(MachinePath) ||
            JsonNode.Parse(File.ReadAllText(MachinePath))?[nameof(AppSettings.ActiveProfile)] is null;
        if (legacy)
        {
            if (File.Exists(MachinePath) && !File.Exists(MachinePath + ".pre-profiles.bak"))
            {
                File.Copy(MachinePath, MachinePath + ".pre-profiles.bak", false);
            }

            string legacyFavorites = Path.Combine(directory, "favorites.json");
            string name = "Default";
            for (int suffix = 2; Exists(name); suffix++)
            {
                name = $"Default {suffix}";
            }

            Create(name, ProfileSettings.From(settings), FavoritesManager.Load(legacyFavorites));
            settings.ActiveProfile = name;
            SaveMachine(settings);
        }
        if (!IsReadable(settings.ActiveProfile))
        {
            string unavailable = settings.ActiveProfile;
            string? fallback = ListProfiles().FirstOrDefault(IsReadable);
            if (fallback is null)
            {
                fallback = AvailableName("Default");
                Create(fallback);
            }
            settings.ActiveProfile = fallback;
            StartupMessage = $"Profile '{unavailable}' is missing or unreadable. Loaded '{fallback}'; the original files were preserved.";
        }
        if (!ListProfiles().Contains(settings.ActiveProfile, StringComparer.OrdinalIgnoreCase))
        {
            PersistCatalog(ListProfiles().Append(settings.ActiveProfile));
        }
        LoadProfile(settings.ActiveProfile).ApplyTo(settings);
        SaveMachine(settings);
        return settings;
    }

    public ProfileSettings LoadProfile(string name) =>
        ReadProfileSettings(File.ReadAllText(SettingsPath(name)));

    public List<Favorite> LoadFavorites(string name)
    {
        if (!File.Exists(FavoritesPath(name)))
        {
            throw new FileNotFoundException("The profile favorites file is missing.");
        }

        ValidateFavorites(File.ReadAllText(FavoritesPath(name)));
        return FavoritesManager.Load(FavoritesPath(name));
    }

    public void SaveSettings(AppSettings settings)
    {
        WriteJson(SettingsPath(settings.ActiveProfile), ProfileSettings.From(settings));
        SaveMachine(settings);
    }

    public void SaveFavorites(string name, List<Favorite> favorites) => WriteJson(FavoritesPath(name), favorites);

    private void SaveMachine(AppSettings settings)
    {
        settings.Profiles = ListProfiles();
        var json = JsonSerializer.SerializeToNode(settings)!.AsObject();
        foreach (var property in typeof(ProfileSettings).GetProperties())
        {
            json.Remove(property.Name);
        }

        WriteJson(MachinePath, json);
    }

    private bool Exists(string name) => File.Exists(SettingsPath(name)) || File.Exists(FavoritesPath(name));

    public void Create(string name, ProfileSettings? settings = null, List<Favorite>? favorites = null)
    {
        ValidateName(name);
        if (Exists(name))
        {
            throw new InvalidOperationException("A profile with that name already exists.");
        }

        WriteJson(SettingsPath(name), settings ?? new ProfileSettings());
        try
        {
            WriteJson(FavoritesPath(name), favorites ?? []);
            PersistCatalog(ListProfiles().Append(name));
        }
        catch { File.Delete(SettingsPath(name)); File.Delete(FavoritesPath(name)); throw; }
    }

    public void Rename(string name, string newName)
    {
        ValidateName(newName);
        if (name == newName)
        {
            return;
        }

        // Windows considers these the same path; use an intermediate name to change casing.
        if (string.Equals(name, newName, StringComparison.OrdinalIgnoreCase))
        {
            string temporaryName = "ProfileRename-" + Guid.NewGuid().ToString("N");
            Rename(name, temporaryName);
            try { Rename(temporaryName, newName); }
            catch { Rename(temporaryName, name); throw; }
            return;
        }

        if (Exists(newName))
        {
            throw new InvalidOperationException("A profile with that name already exists.");
        }

        File.Move(SettingsPath(name), SettingsPath(newName));
        try { File.Move(FavoritesPath(name), FavoritesPath(newName)); }
        catch { File.Move(SettingsPath(newName), SettingsPath(name)); throw; }
        try { PersistCatalog(ListProfiles().Where(profile => !string.Equals(profile, name, StringComparison.OrdinalIgnoreCase)).Append(newName)); }
        catch
        {
            File.Move(SettingsPath(newName), SettingsPath(name));
            File.Move(FavoritesPath(newName), FavoritesPath(name));
            throw;
        }
    }

    public void Delete(string name)
    {
        if (ListProfiles().Count <= 1)
        {
            throw new InvalidOperationException("Keep at least one profile.");
        }
        // Retain a recoverable copy of exactly this pair outside the active profile list.
        string backup = Path.Combine(directory, "DeletedProfiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        string settingsBackup = Path.Combine(backup, Path.GetFileName(SettingsPath(name)));
        File.Move(SettingsPath(name), settingsBackup);
        try { File.Move(FavoritesPath(name), Path.Combine(backup, Path.GetFileName(FavoritesPath(name)))); }
        catch { File.Move(settingsBackup, SettingsPath(name)); throw; }
        try { PersistCatalog(ListProfiles().Where(profile => !string.Equals(profile, name, StringComparison.OrdinalIgnoreCase))); }
        catch
        {
            File.Move(settingsBackup, SettingsPath(name));
            File.Move(Path.Combine(backup, Path.GetFileName(FavoritesPath(name))), FavoritesPath(name));
            throw;
        }
    }

    public string AvailableName(string preferred)
    {
        ValidateName(preferred);
        string name = preferred;
        for (int suffix = 2; Exists(name) || ListProfiles().Contains(name, StringComparer.OrdinalIgnoreCase); suffix++)
        {
            name = preferred[..Math.Min(preferred.Length, 65)] + $" ({suffix})";
        }
        return name;
    }

    public void Export(string name, string path)
    {
        var settings = LoadProfile(name);
        var favorites = LoadFavorites(name);
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Export profiles as a .zip file.");
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                using (var stream = archive.CreateEntry(name + "-settings.json").Open())
                {
                    JsonSerializer.Serialize(stream, settings, JsonOptions);
                }

                using (var stream = archive.CreateEntry(name + "-favorites.json").Open())
                {
                    JsonSerializer.Serialize(stream, favorites, JsonOptions);
                }
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public string Import(string path, string? preferredName = null)
    {
        string sourceName;
        string settingsJson;
        string favoritesJson;
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(path);
            var settingsEntry = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith("-settings.json", StringComparison.OrdinalIgnoreCase));
            if (archive.Entries.Count != 2 || settingsEntry is null)
            {
                throw new InvalidDataException("Choose an export containing one profile settings/favorites pair.");
            }

            sourceName = settingsEntry.FullName[..^14];
            ValidateName(sourceName);
            var favoritesEntry = archive.GetEntry(sourceName + "-favorites.json") ?? throw new InvalidDataException("The matching favorites file is missing.");
            if (settingsEntry.Length > 32 * 1024 * 1024 || favoritesEntry.Length > 32 * 1024 * 1024)
            {
                throw new InvalidDataException("The profile files are too large.");
            }

            using var settingsReader = new StreamReader(settingsEntry.Open());
            using var favoritesReader = new StreamReader(favoritesEntry.Open());
            settingsJson = settingsReader.ReadToEnd();
            favoritesJson = favoritesReader.ReadToEnd();
        }
        else
        {
            string filename = Path.GetFileName(path);
            string suffix = filename.EndsWith("-settings.json", StringComparison.OrdinalIgnoreCase) ? "-settings.json" : "-favorites.json";
            if (!filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Choose a profile ZIP or a named settings/favorites JSON pair.");
            }

            sourceName = filename[..^suffix.Length];
            ValidateName(sourceName);
            string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
            settingsJson = File.ReadAllText(Path.Combine(folder, sourceName + "-settings.json"));
            favoritesJson = File.ReadAllText(Path.Combine(folder, sourceName + "-favorites.json"));
        }
        var settings = ReadProfileSettings(settingsJson);
        var favorites = ValidateFavorites(favoritesJson);
        string name = AvailableName(string.IsNullOrWhiteSpace(preferredName) ? sourceName : preferredName);
        Create(name, settings, favorites);
        return name;
    }

    private static ProfileSettings ReadProfileSettings(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("MidiChannel", out _))
        {
            throw new InvalidDataException("This is not a profile settings file.");
        }

        var settings = JsonSerializer.Deserialize<ProfileSettings>(json, JsonOptions)!;
        if (settings.MidiChannel is < 0 or > 16 || settings.DisplayOffset is < 0 or > 1 ||
            settings.MaxDisplayedPreset is < 1 or > 1024 || settings.SceneCc is < 0 or > 127 ||
            settings.CategoryOrder is null || settings.PresetNameCache is null || settings.SceneNameCaches is null ||
            settings.SceneNameCaches.Values.Any(cache => cache is null || cache.Values.Any(entry => entry is null || entry.Names is null)))
        {
            throw new InvalidDataException("The profile contains invalid mapping or cache values.");
        }

        return settings;
    }

    private static List<Favorite> ValidateFavorites(string json)
    {
        var favorites = JsonSerializer.Deserialize<List<Favorite>>(json, JsonOptions) ?? throw new InvalidDataException("The favorites file is empty.");
        if (favorites.Any(favorite => favorite is null || favorite.Tags is null || favorite.Name is null || favorite.Category is null))
        {
            throw new InvalidDataException("The favorites file contains invalid entries.");
        }

        return favorites;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
