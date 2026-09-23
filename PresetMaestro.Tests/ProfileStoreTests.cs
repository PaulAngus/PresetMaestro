using System.Text.Json.Nodes;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-profiles-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void MigratesLegacyDataOnceAndKeepsComputerOptionsSeparate()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "settings.json");
        var legacy = new AppSettings
        {
            MidiInputPort = "Keyboard",
            MidiOutputPort = "Device",
            ThruInputPorts = ["Pedals"],
            MidiChannel = 7,
            DisplayOffset = 1,
            MaxDisplayedPreset = 400,
            SceneCc = 44,
            AutoSend = true,
            KeyboardEntryEnabled = false,
            MidiEntryEnabled = false,
            PresetNameCache = new() { [3] = "Clean" },
            SceneNameCaches = new() { ["ports"] = new() { [3] = new() { Names = ["Lead"] } } },
        };
        SettingsManager.Save(legacy, path);
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json.Remove("ActiveProfile");
        File.WriteAllText(path, json.ToJsonString());
        string original = File.ReadAllText(path);
        FavoritesManager.Save([new Favorite { Id = 1, Slot = 1, Name = "First", Preset = 4, Scene = 1 }], Path.Combine(_directory, "favorites.json"));

        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        Assert.Equal("Default", settings.ActiveProfile);
        Assert.Equal(7, settings.MidiChannel);
        Assert.Equal("Clean", settings.PresetNameCache[3]);
        Assert.Equal("Lead", settings.SceneNameCaches["ports"][3].Names[0]);
        Assert.Equal("First", store.LoadFavorites("Default").Single().Name);
        Assert.Equal(original, File.ReadAllText(path + ".pre-profiles.bak"));
        Assert.True(File.Exists(Path.Combine(_directory, "favorites.json")));

        var machine = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(_directory, "Default-settings.json")))!.AsObject();
        foreach (var property in typeof(ProfileSettings).GetProperties())
        {
            Assert.False(machine.ContainsKey(property.Name));
        }

        Assert.Equal("Keyboard", machine["MidiInputPort"]!.GetValue<string>());
        Assert.True(machine["AutoSend"]!.GetValue<bool>());
        Assert.False(profile.ContainsKey("MidiInputPort"));
        Assert.False(profile.ContainsKey("MidiNoteMap"));
        Assert.False(profile.ContainsKey("AutoSend"));
        Assert.Equal(400, profile["MaxDisplayedPreset"]!.GetValue<int>());

        store.LoadSettings();
        Assert.Single(store.ListProfiles());
    }

    [Fact]
    public void ProfilesRoundTripIndependentlyAndSelectionSurvivesRestart()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        settings.MidiOutputPort = "Shared output";
        settings.AutoSendDelayMs = 90;
        settings.PresetNameCache[8] = "Default name";
        store.SaveSettings(settings);
        store.Create("Gig");
        store.LoadProfile("Gig").ApplyTo(settings);
        settings.ActiveProfile = "Gig";
        Assert.Empty(settings.PresetNameCache);
        settings.MidiChannel = 9;
        settings.SceneNameCaches["gig"] = new() { [8] = new() { Names = ["Solo"] } };
        store.SaveSettings(settings);
        store.SaveFavorites("Gig", [new Favorite { Id = 1, Slot = 1, Name = "Gig favorite" }]);

        var reopened = new ProfileStore(_directory).LoadSettings();
        Assert.Equal("Gig", reopened.ActiveProfile);
        Assert.Equal(9, reopened.MidiChannel);
        Assert.Equal("Shared output", reopened.MidiOutputPort);
        Assert.Equal(90, reopened.AutoSendDelayMs);
        Assert.Equal("Solo", reopened.SceneNameCaches["gig"][8].Names[0]);
        Assert.Equal("Default name", store.LoadProfile("Default").PresetNameCache[8]);
        Assert.Empty(store.LoadFavorites("Default"));
        Assert.Equal("Gig favorite", store.LoadFavorites("Gig").Single().Name);
    }

    [Fact]
    public void RenameAndDeleteOperateOnBothFilesAndKeepRecoverableCopies()
    {
        var store = new ProfileStore(_directory);
        store.LoadSettings();
        store.Create("Gig", new ProfileSettings { SceneCc = 61 }, [new Favorite { Name = "Song" }]);
        store.Rename("Gig", "Tour");
        Assert.Equal(61, store.LoadProfile("Tour").SceneCc);
        Assert.Equal("Song", store.LoadFavorites("Tour").Single().Name);
        Assert.False(File.Exists(Path.Combine(_directory, "Gig-settings.json")));
        Assert.False(File.Exists(Path.Combine(_directory, "Gig-favorites.json")));
        Assert.Throws<InvalidOperationException>(() => store.Rename("Tour", "Default"));
        store.Rename("Tour", "TOUR");
        Assert.Contains("TOUR", store.ListProfiles());
        Assert.Equal("Song", store.LoadFavorites("TOUR").Single().Name);
        store.Delete("TOUR");
        Assert.Equal(["Default"], store.ListProfiles());
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_directory, "DeletedProfiles"), "*.json", SearchOption.AllDirectories).Length);
        Assert.Throws<InvalidOperationException>(() => store.Delete("Default"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("bad:name")]
    [InlineData("trailing.")]
    [InlineData(" padded ")]
    public void InvalidNamesCannotBecomePaths(string name)
    {
        var store = new ProfileStore(_directory);
        Assert.Throws<ArgumentException>(() => store.Create(name));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void MalformedLastProfileFallsBackWithoutReplacingItsData()
    {
        var store = new ProfileStore(_directory);
        store.LoadSettings();
        string path = Path.Combine(_directory, "Default-settings.json");
        File.WriteAllText(path, "{ invalid }");
        var settings = store.LoadSettings();
        Assert.NotEqual("Default", settings.ActiveProfile);
        Assert.Contains("unreadable", store.StartupMessage);
        Assert.Equal("{ invalid }", File.ReadAllText(path));
    }

    [Fact]
    public void RegistryPersistsAndRescanFindsNewPairsButSkipsIncompleteAndCorruptFiles()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        store.Create("Gig");
        store.LoadProfile("Gig").ApplyTo(settings);
        settings.ActiveProfile = "Gig";
        store.SaveSettings(settings);
        File.Copy(Path.Combine(_directory, "Gig-settings.json"), Path.Combine(_directory, "Discovered-settings.json"));
        File.Copy(Path.Combine(_directory, "Gig-favorites.json"), Path.Combine(_directory, "Discovered-favorites.json"));
        File.WriteAllText(Path.Combine(_directory, "Incomplete-settings.json"), "{}");
        File.WriteAllText(Path.Combine(_directory, "Broken-settings.json"), "not json");
        File.WriteAllText(Path.Combine(_directory, "Broken-favorites.json"), "[]");

        var restarted = new ProfileStore(_directory);
        var reopened = restarted.LoadSettings();
        Assert.Equal("Gig", reopened.ActiveProfile);
        Assert.Equal(["Default", "Gig"], reopened.Profiles);
        Assert.DoesNotContain("Discovered", restarted.ListProfiles());
        var result = restarted.Rescan(reopened);
        Assert.Equal(1, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Contains("Discovered", reopened.Profiles);
        var disk = SettingsManager.Load(Path.Combine(_directory, "settings.json"));
        Assert.Equal(reopened.Profiles, disk.Profiles);
        Assert.Equal("Gig", disk.ActiveProfile);
        Assert.Equal(0, restarted.Rescan(reopened).Added);
        restarted.Rename("Discovered", "Renamed");
        Assert.Contains("Renamed", SettingsManager.Load(Path.Combine(_directory, "settings.json")).Profiles);
        restarted.Delete("Renamed");
        Assert.DoesNotContain("Renamed", SettingsManager.Load(Path.Combine(_directory, "settings.json")).Profiles);
    }

    [Fact]
    public void ExportImportRoundTripsOnlyProfileDataAndNeverOverwritesExistingNames()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        settings.MidiInputPort = "Private computer port";
        settings.AutoSend = true;
        settings.DeviceModel = DeviceModel.AxeFxIII;
        settings.MidiChannel = 8;
        settings.DisplayOffset = 1;
        settings.MaxDisplayedPreset = 1024;
        settings.SceneCc = 55;
        settings.PresetNameCache[3] = "Preset";
        settings.SceneNameCaches["ports"] = new() { [3] = new() { Names = ["Scene"] } };
        store.SaveSettings(settings);
        store.SaveFavorites("Default", [new Favorite { Name = "Song", Preset = 3, Scene = 2, Tags = ["tag"] }]);
        string archivePath = Path.Combine(_directory, "export.zip");
        store.Export("Default", archivePath);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(archivePath))
        {
            Assert.Equal(2, zip.Entries.Count);
            using var reader = new StreamReader(zip.GetEntry("Default-settings.json")!.Open());
            string json = reader.ReadToEnd();
            Assert.DoesNotContain("MidiInputPort", json);
            Assert.DoesNotContain("AutoSend", json);
            Assert.DoesNotContain("ActiveProfile", json);
            Assert.DoesNotContain("\"Profiles\"", json);
        }
        string imported = store.Import(archivePath);
        Assert.Equal("Default (2)", imported);
        Assert.Equal(8, store.LoadProfile(imported).MidiChannel);
        Assert.Equal(DeviceModel.AxeFxIII, store.LoadProfile(imported).DeviceModel);
        Assert.Equal(1024, store.LoadProfile(imported).MaxDisplayedPreset);
        Assert.Equal(55, store.LoadProfile(imported).SceneCc);
        Assert.Equal("Scene", store.LoadProfile(imported).SceneNameCaches["ports"][3].Names[0]);
        Assert.Equal("Song", store.LoadFavorites(imported).Single().Name);
        Assert.Equal("Private computer port", store.LoadSettings().MidiInputPort);
        Assert.Equal("Default", store.LoadSettings().ActiveProfile);
        Assert.Contains(imported, store.LoadSettings().Profiles);
        string fromPair = store.Import(Path.Combine(_directory, "Default-favorites.json"), "From pair");
        Assert.Equal("From pair", fromPair);
        Assert.Equal("Preset", store.LoadProfile(fromPair).PresetNameCache[3]);
    }

    [Fact]
    public void InvalidImportsLeaveRegistryAndExistingProfilesUntouched()
    {
        var store = new ProfileStore(_directory);
        store.LoadSettings();
        string path = Path.Combine(_directory, "bad.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("../escape-settings.json").Open()))
            {
                writer.Write("{}");
            }

            archive.CreateEntry("../escape-favorites.json");
        }
        Assert.Throws<ArgumentException>(() => store.Import(path));
        Assert.Equal(["Default"], store.ListProfiles());
        File.WriteAllText(Path.Combine(_directory, "Partial-settings.json"), "{}");
        Assert.Throws<FileNotFoundException>(() => store.Import(Path.Combine(_directory, "Partial-settings.json")));
        File.WriteAllText(Path.Combine(_directory, "Partial-favorites.json"), "[]");
        Assert.Throws<InvalidDataException>(() => store.Import(Path.Combine(_directory, "Partial-settings.json")));
        Assert.Equal(["Default"], SettingsManager.Load(Path.Combine(_directory, "settings.json")).Profiles);
    }
}
