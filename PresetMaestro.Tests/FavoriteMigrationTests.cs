using System.IO.Compression;
using System.Text.Json;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed class FavoriteMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "favorites-migration-" + Guid.NewGuid().ToString("N"));
    private const string Legacy = """
        [
          {"Id":41,"Slot":2,"Name":"First","Category":" Low Gain ","Tags":["Warm"],"Preset":101,"Scene":3},
          {"Id":99,"Slot":6,"Name":"Second","Category":"LOW GAIN","Tags":["low gain","Dry"],"Preset":5,"Scene":1},
          {"Id":12,"Slot":18,"Name":"","Category":"  ","Tags":[],"Preset":0,"Scene":0}
        ]
        """;

    public FavoriteMigrationTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private static void Check(List<Favorite> favorites)
    {
        Assert.Equal(new[] { 41, 99, 12 }, favorites.Select(f => f.Id));
        Assert.Equal(new[] { 2, 6, 18 }, favorites.Select(f => f.Slot));
        Assert.Equal(new[] { "Warm", "Low Gain" }, favorites[0].Tags);
        Assert.Equal(new[] { "low gain", "Dry" }, favorites[1].Tags);
        Assert.True(favorites[2].IsEmpty);
        Assert.Equal(("First", 101, 3), (favorites[0].Name, favorites[0].Preset, favorites[0].Scene));
    }

    [Fact]
    public void StandaloneMigrationPreservesOriginalSlotsAndIsIdempotent()
    {
        string path = Path.Combine(_directory, "favorites.json");
        File.WriteAllText(path, Legacy);
        for (int i = 0; i < 3; i++)
        {
            var favorites = FavoritesManager.Load(path); Check(favorites);
            FavoritesManager.Save(favorites, path);
            Assert.DoesNotContain("Category", File.ReadAllText(path));
            Assert.Equal(Legacy, File.ReadAllText(path + ".pre-tags.bak"));
        }
    }

    [Fact]
    public void ProfileLoadSaveZipFolderAndLegacyInitializationUseSameMigration()
    {
        string settingsPath = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settingsPath, """{"MidiChannel":4,"CategoryOrder":["Low Gain"]}""");
        File.WriteAllText(Path.Combine(_directory, "favorites.json"), Legacy);
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        Check(store.LoadFavorites(settings.ActiveProfile));
        Assert.Equal(Legacy, File.ReadAllText(Path.Combine(_directory, "favorites.json")));
        string name = settings.ActiveProfile;
        string profileSettings = Path.Combine(_directory, name + "-settings.json");
        string profileFavorites = Path.Combine(_directory, name + "-favorites.json");
        File.WriteAllText(profileSettings, """{"MidiChannel":4,"CategoryOrder":["Low Gain"]}""");
        File.WriteAllText(profileFavorites, Legacy);
        Check(store.LoadFavorites(name));
        store.SaveFavorites(name, store.LoadFavorites(name));
        store.SaveSettings(settings);
        Assert.Equal(Legacy, File.ReadAllText(profileFavorites + ".pre-tags.bak"));
        Assert.Contains("CategoryOrder", File.ReadAllText(profileSettings + ".pre-tags.bak"));
        Assert.DoesNotContain("Category", File.ReadAllText(profileSettings));
        string zipPath = Path.Combine(_directory, "export.zip");
        store.Export(name, zipPath);
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                Assert.DoesNotContain("Category", reader.ReadToEnd());
            }
        }
        Check(store.LoadFavorites(store.Import(zipPath)));
        string source = Path.Combine(_directory, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "Old-settings.json"), """{"MidiChannel":4,"CategoryOrder":["Low Gain"]}""");
        File.WriteAllText(Path.Combine(source, "Old-favorites.json"), Legacy);
        Check(store.LoadFavorites(store.Import(Path.Combine(source, "Old-settings.json"))));
        string oldZip = Path.Combine(_directory, "legacy.zip");
        ZipFile.CreateFromDirectory(source, oldZip);
        Check(store.LoadFavorites(store.Import(oldZip)));
        Assert.Equal(Legacy, File.ReadAllText(Path.Combine(source, "Old-favorites.json")));
        Assert.DoesNotContain("Category", JsonSerializer.Serialize(store.LoadFavorites(name)));
    }

    [Theory]
    [InlineData("[{\"Category\":42}]")]
    [InlineData("[{\"Category\":\"Lead\",\"Tags\":null}]")]
    [InlineData("[{\"Category\":\"Lead\",\"Tags\":[null]}]")]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("[null]")]
    public void MalformedMigrationCannotOverwriteOriginal(string malformed)
    {
        string path = Path.Combine(_directory, "favorites.json"); File.WriteAllText(path, malformed);
        Assert.ThrowsAny<Exception>(() => FavoritesManager.Load(path));
        Assert.ThrowsAny<Exception>(() => FavoritesManager.Save([], path));
        Assert.Equal(malformed, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }
}

public sealed class FavoriteTableLayoutTests
{
    [Theory]
    [InlineData(18, 2, "9,9")]
    [InlineData(18, 3, "6,6,6")]
    [InlineData(19, 3, "7,6,6")]
    [InlineData(10, 4, "3,3,2,2")]
    [InlineData(0, 2, "0,0")]
    [InlineData(1, 4, "1,0,0,0")]
    [InlineData(2, 4, "1,1,0,0")]
    public void BalancedRanges(int count, int columns, string expected)
    {
        var ranges = FavoriteTableLayout.Distribute(count, columns);
        Assert.Equal(expected, string.Join(',', ranges.Select(r => r.Count)));
        Assert.Equal(Enumerable.Range(0, count), ranges.SelectMany(r => Enumerable.Range(r.Start, r.Count)));
    }

    [Fact]
    public void LargeListsAndResizeBreakpointsNeverLoseOrDuplicateItems()
    {
        for (int columns = 2; columns <= 10; columns++)
        {
            var ranges = FavoriteTableLayout.Distribute(10001, columns);
            Assert.Equal(Enumerable.Range(0, 10001), ranges.SelectMany(r => Enumerable.Range(r.Start, r.Count)));
            Assert.InRange(ranges.Max(r => r.Count) - ranges.Min(r => r.Count), 0, 1);
        }
        Assert.Equal(2, FavoriteTableLayout.TableCount(897));
        Assert.Equal(2, FavoriteTableLayout.TableCount(1355));
        Assert.Equal(3, FavoriteTableLayout.TableCount(1356));
        Assert.Equal(3, FavoriteTableLayout.TableCount(1813));
        Assert.Equal(4, FavoriteTableLayout.TableCount(1814));
    }
}
