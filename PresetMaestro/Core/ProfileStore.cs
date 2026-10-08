using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;

namespace PresetMaestro.Core;

public sealed partial class ProfileStore(string directory)
{
    static partial void PrepareIndexCopy(ProfileSettings settings);
    partial void PrepareIndexExport(ProfileSettings settings);
    static partial void ValidateIndexImport(ProfileSettings settings);
    partial void PrepareIndexImport(ProfileSettings settings, string profileName, Guid? libraryId, bool keepImportedLibrary);
    partial void ApplyLibraryMapping(ProfileSettings settings);
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
        return ApplyProfileScan(settings, ScanProfiles());
    }

    // Discovery only reads files on the worker. Publish the catalog and settings
    // on the caller's UI thread so other settings writes cannot race the commit.
    internal Task<(List<string> Names, int Skipped)> ScanProfileFilesAsync() => Task.Run(ScanProfiles);

    internal (int Added, int Skipped) ApplyProfileScan(AppSettings settings, (List<string> Names, int Skipped) scan)
    {
        var previous = ListProfiles();
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
        json.Remove("CategoryOrder");
        WriteJson(MachinePath, json);
        _profiles = updated;
    }

    public AppSettings LoadSettings()
    {
        AppSettings settings;
        bool recoveredMachineSettings = false;
        try
        {
            settings = SettingsManager.Load(MachinePath);
        }
        catch (JsonException) when (File.Exists(MachinePath))
        {
            // Keep the unreadable file intact so computer-specific settings can be recovered.
            string backup = MachinePath + ".unreadable." + Guid.NewGuid().ToString("N") + ".bak";
            File.Move(MachinePath, backup);
            _profiles = ScanProfiles().Names;
            settings = new AppSettings
            {
                Profiles = [.. _profiles],
                ActiveProfile = _profiles.FirstOrDefault() ?? "Default",
            };
            StartupMessage = $"Computer settings were unreadable and preserved as '{Path.GetFileName(backup)}'. Loaded available profiles with default computer settings.";
            recoveredMachineSettings = true;
        }
        // A missing ActiveProfile identifies the pre-profile format. Preserve both originals.
        bool legacy = (!recoveredMachineSettings || _profiles?.Count == 0) && (!File.Exists(MachinePath) ||
            JsonNode.Parse(File.ReadAllText(MachinePath))?[nameof(AppSettings.ActiveProfile)] is null);
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
            string message = $"Profile '{unavailable}' is missing or unreadable. Loaded '{fallback}'; the original files were preserved.";
            StartupMessage = StartupMessage is null ? message : StartupMessage + " " + message;
        }
        if (!ListProfiles().Contains(settings.ActiveProfile, StringComparer.OrdinalIgnoreCase))
        {
            PersistCatalog(ListProfiles().Append(settings.ActiveProfile));
        }
        LoadProfile(settings.ActiveProfile).ApplyTo(settings);
        SaveMachine(settings);
        return settings;
    }

    public ProfileSettings LoadProfile(string name)
    {
        var settings = ReadProfileSettings(File.ReadAllText(SettingsPath(name)));
        ApplyLibraryMapping(settings);
        return settings;
    }

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
        string path = SettingsPath(settings.ActiveProfile);
        string? previous = File.Exists(path) ? File.ReadAllText(path) : null;
        WriteJson(path, ProfileSettings.From(settings));
        try { SaveMachine(settings); }
        catch (Exception error)
        {
            try
            {
                if (previous is null) { File.Delete(path); }
                else { WriteText(path, previous); }
            }
            catch (Exception recovery)
            { throw new IOException("Settings could not be saved and the previous profile could not be restored.", new AggregateException(error, recovery)); }
            throw;
        }
    }

    public void SaveFavorites(string name, List<Favorite> favorites) => WriteJson(FavoritesPath(name), favorites);

    internal void SaveSettingsAndFavorites(AppSettings settings, List<Favorite> favorites)
    {
        string[] paths = [SettingsPath(settings.ActiveProfile), FavoritesPath(settings.ActiveProfile), MachinePath];
        var originals = paths.Select(path => (Path: path, Contents: File.Exists(path) ? File.ReadAllText(path) : null)).ToArray();
        try
        {
            SaveFavorites(settings.ActiveProfile, favorites);
            SaveSettings(settings);
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<Exception>();
            foreach (var original in originals)
            {
                try
                {
                    // A failed write may have left this file untouched (and locked).
                    if (original.Contents is null) { if (File.Exists(original.Path)) { File.Delete(original.Path); } }
                    else if (!File.Exists(original.Path) || File.ReadAllText(original.Path) != original.Contents) { WriteText(original.Path, original.Contents); }
                }
                catch (Exception recovery) { recoveryErrors.Add(recovery); }
            }
            if (recoveryErrors.Count > 0)
            { throw new IOException("Scene tags could not be saved and some profile files could not be restored.", new AggregateException(new[] { error }.Concat(recoveryErrors))); }
            throw;
        }
    }

    private void SaveMachine(AppSettings settings)
    {
        settings.Profiles = ListProfiles();
        var json = JsonSerializer.SerializeToNode(settings)!.AsObject();
        foreach (var property in typeof(ProfileSettings).GetProperties())
        {
            json.Remove(property.Name);
        }

        json.Remove("CategoryOrder");
        WriteJson(MachinePath, json);
    }

    public bool ContainsName(string name) => Exists(name) || ListProfiles().Contains(name, StringComparer.OrdinalIgnoreCase);

    private bool Exists(string name) => File.Exists(SettingsPath(name)) || File.Exists(FavoritesPath(name));

    public void Create(string name, ProfileSettings? settings = null, List<Favorite>? favorites = null)
    {
        ValidateName(name);
        if (Exists(name))
        {
            throw new InvalidOperationException("A profile with that name already exists.");
        }

        settings = JsonSerializer.Deserialize<ProfileSettings>(JsonSerializer.Serialize(settings ?? new ProfileSettings(), JsonOptions), JsonOptions)!;
        PrepareIndexCopy(settings);
        WriteJson(SettingsPath(name), settings);
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
        PrepareIndexExport(settings);
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
            LegacyFavoritesStorage.PreserveOriginal(path);
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

    public sealed record ImportData(string SourceName, ProfileSettings Settings, List<Favorite> Favorites);

    public string Import(string path, string? preferredName = null, bool overwrite = false,
        Guid? libraryId = null, bool keepImportedLibrary = false) =>
        Import(ReadImport(path), preferredName, overwrite, libraryId, keepImportedLibrary);

    public static ImportData ReadImport(string path)
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

            settingsJson = ReadImportJson(settingsEntry.Open());
            favoritesJson = ReadImportJson(favoritesEntry.Open());
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
            settingsJson = ReadImportJson(File.OpenRead(Path.Combine(folder, sourceName + "-settings.json")));
            favoritesJson = ReadImportJson(File.OpenRead(Path.Combine(folder, sourceName + "-favorites.json")));
        }
        var settings = ReadProfileSettings(settingsJson);
        var favorites = ValidateFavorites(favoritesJson);
        ValidateIndexImport(settings);
        return new ImportData(sourceName, settings, favorites);
    }

    public string Import(ImportData data, string? preferredName = null, bool overwrite = false,
        Guid? libraryId = null, bool keepImportedLibrary = false)
    {
        string name = ValidateName(preferredName ?? data.SourceName);
        name = ListProfiles().FirstOrDefault(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        bool exists = ContainsName(name);
        if (exists && !overwrite) { throw new InvalidOperationException("A profile with that name already exists. Choose a different name or overwrite it."); }
        // Prepare a fresh copy after the user chooses a name and library. Reading an
        // export must retain its library identities so we can recognise local data.
        var importedSettings = ReadProfileSettings(JsonSerializer.Serialize(data.Settings, JsonOptions));
        PrepareIndexImport(importedSettings, name, libraryId, keepImportedLibrary);
        if (!exists)
        {
            Create(name, importedSettings, data.Favorites);
            return name;
        }

        // Preserve the original pair before replacing either file. Restore any changed file
        // if the second write or catalog save fails, so a failed import cannot mix profiles.
        string backup = Path.Combine(directory, "OverwrittenProfiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        string settingsPath = SettingsPath(name), favoritesPath = FavoritesPath(name);
        string settingsBackup = Path.Combine(backup, Path.GetFileName(settingsPath));
        string favoritesBackup = Path.Combine(backup, Path.GetFileName(favoritesPath));
        if (File.Exists(settingsPath)) { File.Copy(settingsPath, settingsBackup); }
        if (File.Exists(favoritesPath)) { File.Copy(favoritesPath, favoritesBackup); }
        bool settingsWritten = false, favoritesWritten = false;
        try
        {
            WriteJson(settingsPath, importedSettings); settingsWritten = true;
            WriteJson(favoritesPath, data.Favorites); favoritesWritten = true;
            PersistCatalog(ListProfiles().Append(name));
        }
        catch (Exception error)
        {
            try
            {
                if (settingsWritten) { Restore(settingsPath, settingsBackup); }
                if (favoritesWritten) { Restore(favoritesPath, favoritesBackup); }
            }
            catch (Exception recovery)
            { throw new IOException("Import failed and the previous profile could not be restored. Its files are preserved in " + backup + ".", new AggregateException(error, recovery)); }
            throw;
        }
        return name;

        static void Restore(string path, string backupPath)
        {
            if (File.Exists(backupPath)) { File.Copy(backupPath, path, true); }
            else { File.Delete(path); }
        }
    }

    private static string ReadImportJson(Stream stream)
    {
        using var reader = new StreamReader(stream);
        const int limit = 32 * 1024 * 1024;
        if (stream.CanSeek && stream.Length > limit) { throw new InvalidDataException("The profile files are too large."); }
        // Bound actual decompressed text too, rather than trusting ZIP metadata alone.
        var text = new System.Text.StringBuilder();
        char[] buffer = new char[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (text.Length > limit - read) { throw new InvalidDataException("The profile files are too large."); }
            text.Append(buffer, 0, read);
        }
        return text.ToString();
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
            settings.PresetNameCache is null || settings.SceneNameCaches is null ||
            settings.SceneNameCaches.Values.Any(cache => cache is null || cache.Values.Any(entry => entry is null || entry.Names is null)))
        {
            throw new InvalidDataException("The profile contains invalid mapping or cache values.");
        }

        return settings;
    }

    private static List<Favorite> ValidateFavorites(string json)
    {
        var favorites = JsonSerializer.Deserialize<List<Favorite>>(json, JsonOptions) ?? throw new InvalidDataException("The favorites file is empty.");
        if (favorites.Any(favorite => favorite is null || favorite.Tags is null || favorite.Name is null ||
            (!favorite.IsEmpty && favorite.Scene is < 1 or > 8)))
        {
            throw new InvalidDataException("The favorites file contains invalid entries or scene numbers.");
        }

        return favorites;
    }

    private static void WriteJson<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            LegacyFavoritesStorage.PreserveOriginal(path);
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
