using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed class ProfileOverwriteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-overwrite-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    private (ProfileStore Store, string Archive) Setup()
    {
        var store = new ProfileStore(_directory);
        store.LoadSettings();
        store.SaveFavorites("Default", [new Favorite { Name = "Original", Scene = 1 }]);
        store.Create("Source", new ProfileSettings { MidiChannel = 8 }, [new Favorite { Name = "Imported", Scene = 2 }]);
        string archive = Path.Combine(_directory, "source.zip");
        store.Export("Source", archive);
        return (store, archive);
    }

    [Fact]
    public void ConflictingImportRequiresExplicitOverwriteAndPreservesBackup()
    {
        var (store, archive) = Setup();
        Assert.Throws<InvalidOperationException>(() => store.Import(archive, "default"));
        Assert.Equal("Original", store.LoadFavorites("Default").Single().Name);
        Assert.Equal(["Default", "Source"], store.ListProfiles());
        string name = store.Import(archive, "default", overwrite: true);
        Assert.Equal("Default", name);
        Assert.Equal(["Default", "Source"], store.ListProfiles());
        Assert.Equal(8, store.LoadProfile(name).MidiChannel);
        Assert.Equal("Imported", store.LoadFavorites(name).Single().Name);
        string backup = Assert.Single(Directory.GetDirectories(Path.Combine(_directory, "OverwrittenProfiles")));
        Assert.Equal("Original", FavoritesManager.Load(Path.Combine(backup, "Default-favorites.json")).Single().Name);
        Assert.Equal("Default", new ProfileStore(_directory).LoadSettings().ActiveProfile);
    }

    [WindowsFileLockTheory]
    [InlineData("Default-favorites.json")]
    [InlineData("settings.json")]
    public void FailedOverwriteRestoresOriginalPairAndCatalog(string lockedFile)
    {
        var (store, archive) = Setup();
        var before = Directory.GetFiles(_directory, "*.json").ToDictionary(path => path, File.ReadAllBytes);
        using (var locked = new FileStream(Path.Combine(_directory, lockedFile), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => store.Import(archive, "Default", overwrite: true));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            foreach (var (path, bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        }
        Assert.Equal(["Default", "Source"], store.ListProfiles());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
}
