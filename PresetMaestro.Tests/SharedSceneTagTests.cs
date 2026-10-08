using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class SharedSceneTagTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-scene-tags-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    private static (IndexProfile Profile, DeviceIndex Cache, List<Favorite> Favorites) Fixture(int offset = 1)
    {
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        var cache = new DeviceIndex
        {
            Device = device,
            Committed = new IndexScan
            {
                Status = "Complete",
                Firmware = "12.00",
                FinishedAt = DateTimeOffset.UtcNow,
                Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot, slot => FractalIndexWorkflowTests.Preset(slot)),
            }
        };
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        var annotation = profile.GetOrCreate(device.Id, cache.Committed.Presets[125], "12.00");
        annotation.Tags = ["Preset only"]; annotation.SceneTags[2] = ["Lead"];
        annotation.SceneTags[3] = ["Rhythm"];
        return (profile, cache, [
            new Favorite { Id = 10, Slot = 1, Name = "Solo", Preset = 125 + offset, Scene = 3, Tags = [" live ", "LEAD"] },
            new Favorite { Id = 20, Slot = 2, Name = "Encore", Preset = 125 + offset, Scene = 3, Tags = ["Bright"] },
            new Favorite { Id = 30, Slot = 3, Name = "Verse", Preset = 125 + offset, Scene = 4 },
            new Favorite { Id = 40, Slot = 4, Name = "Other preset", Preset = 126 + offset, Scene = 3 },
            new Favorite { Id = 50, Slot = 5, Scene = 0 },
        ]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void LegacyTagsMergeOnceAndRemovedTagsStayRemovedAfterReload(int offset)
    {
        var (profile, cache, favorites) = Fixture(offset);
        Assert.True(SharedSceneTags.Reconcile(profile, cache, favorites, offset));
        Assert.Equal(["Lead", "live", "Bright"], favorites[0].Tags);
        Assert.Equal(favorites[0].Tags, favorites[1].Tags);
        Assert.Equal(["Rhythm"], favorites[2].Tags);
        Assert.Empty(favorites[3].Tags); Assert.True(favorites[4].IsEmpty);
        Assert.Equal(["Preset only"], profile.Annotations.Single().Tags);
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, offset));
        var target = SharedSceneTags.Resolve(profile, cache, offset, 125 + offset, 3)!;
        SharedSceneTags.Set(profile, target, []);
        Assert.True(SharedSceneTags.Reconcile(profile, cache, favorites, offset));
        favorites = IndexJson.Clone(favorites); profile = IndexJson.Clone(profile);
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, offset));
        Assert.Empty(favorites[0].Tags); Assert.Empty(favorites[1].Tags);
        Assert.Equal(profile.Annotations.Single().Id, favorites[0].SceneTagSourceId);
    }

    [Fact]
    public void ChangedContentAndOtherDevicesDoNotInheritLinkedTags()
    {
        var (profile, cache, favorites) = Fixture();
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        var original = IndexJson.Clone(cache.Committed!.Presets[125]);
        cache.Committed.Presets[125] = original with { ContentSha256 = "replacement" };
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.Empty(favorites[0].Tags); Assert.Empty(favorites[1].Tags);
        Assert.Equal(["Lead", "live", "Bright"], profile.Pending(cache).Single().SceneTags[2]);
        Assert.Null(profile.Find(cache.Device.Id, cache.Committed.Presets[125], "12.00"));
        cache.Committed.Presets[125] = original;
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.Equal(["Lead", "live", "Bright"], favorites[0].Tags);
        var other = IndexJson.Clone(cache); other.Device = other.Device with { Id = Guid.NewGuid() };
        profile.AssignDevice(other.Device);
        SharedSceneTags.Reconcile(profile, other, favorites, 1);
        Assert.Empty(favorites[0].Tags);
        profile.AssignDevice(cache.Device);
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.Equal(["Lead", "live", "Bright"], favorites[0].Tags);
    }

    [Fact]
    public void MissingEmptyAndInvalidScenesKeepStandaloneTagsAndNeverCreateAnnotations()
    {
        var (profile, cache, favorites) = Fixture();
        cache.Committed!.Presets[125] = cache.Committed.Presets[125] with { NameOnlyEmpty = true };
        Assert.Null(SharedSceneTags.Resolve(profile, cache, 1, 126, 3));
        Assert.Null(SharedSceneTags.Resolve(profile, cache, 1, 700, 3));
        Assert.Null(SharedSceneTags.Resolve(profile, cache, 1, 127, 9));
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, 1));
        Assert.Equal([" live ", "LEAD"], favorites[0].Tags);
        Assert.Single(profile.Annotations);
    }

    [Theory]
    [InlineData("Default-favorites.json")]
    [InlineData("Default-settings.json")]
    [InlineData("settings.json")]
    public void FailedSharedSaveRestoresBothFilesAndFavoriteObjects(string lockedFile)
    {
        var (profile, cache, favorites) = Fixture();
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        settings.FractalIndex = IndexJson.ToElement(profile);
        store.SaveSettingsAndFavorites(settings, favorites);
        var before = Directory.GetFiles(_directory, "*.json").ToDictionary(path => path, File.ReadAllBytes);
        var favorite = favorites[0]; var tags = favorite.Tags.ToList(); var source = favorite.SceneTagSourceId;
        var updated = IndexJson.Clone(profile);
        SharedSceneTags.Set(updated, SharedSceneTags.Resolve(updated, cache, 1, 126, 3)!, ["Replacement"]);
        settings.FractalIndex = IndexJson.ToElement(updated);
        using (var locked = new FileStream(Path.Combine(_directory, lockedFile), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => new FavoriteCommands(favorites, values =>
            {
                SharedSceneTags.Reconcile(updated, cache, values, 1, importLegacy: false);
                store.SaveSettingsAndFavorites(settings, values);
            }).Save(10, "Edited", 126, 3, ["Replacement"]));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            foreach (var (path, bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        }
        Assert.Same(favorite, favorites[0]); Assert.Equal("Solo", favorite.Name);
        Assert.Equal(tags, favorite.Tags); Assert.Equal(source, favorite.SceneTagSourceId);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void RemovingShortcutsLeavesSceneTagsAvailableToNewFavorites()
    {
        var (profile, cache, favorites) = Fixture();
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        var commands = new FavoriteCommands(favorites, _ => { });
        commands.Clear(favorites[0]); commands.Remove(favorites[1]);
        commands.Save(0, "New solo", 126, 3, []);
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.True(favorites[0].IsEmpty);
        Assert.Equal(["Lead", "live", "Bright"], favorites[^1].Tags);
        Assert.Equal(["Preset only"], profile.Annotations.Single().Tags);
    }

    [Fact]
    public void ExportImportAndProfileCopyPreserveLinksWithIndependentTags()
    {
        var (profile, cache, favorites) = Fixture();
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        settings.DisplayOffset = 1; settings.FractalIndex = IndexJson.ToElement(profile);
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        store.SaveSettingsAndFavorites(settings, favorites);
        store.Create("Copy", store.LoadProfile("Default"), store.LoadFavorites("Default"));
        string archive = Path.Combine(_directory, "export.zip"); store.Export("Default", archive);
        string imported = store.Import(archive, "Imported");
        foreach (string name in new[] { "Copy", imported })
        {
            var copied = IndexJson.ReadProfile(store.LoadProfile(name).FractalIndex);
            var copiedFavorites = store.LoadFavorites(name);
            Assert.NotEqual(profile.ProfileId, copied.ProfileId);
            Assert.Equal(profile.Annotations.Single().Id, copiedFavorites[0].SceneTagSourceId);
            SharedSceneTags.Set(copied, SharedSceneTags.Resolve(copied, cache, 1, 126, 3)!, [name]);
            SharedSceneTags.Reconcile(copied, cache, copiedFavorites, 1);
            Assert.Equal([name], copiedFavorites[0].Tags);
        }
        Assert.Equal(["Lead", "live", "Bright"], store.LoadFavorites("Default")[0].Tags);
    }

    [AvaloniaTheory]
    [InlineData("Light", 1000, 640)]
    [InlineData("Dark", 1000, 640)]
    [InlineData("Light", 1440, 850)]
    [InlineData("Dark", 1440, 850)]
    public void EditingEitherViewUpdatesBothAndSceneDraftsStayWithTheirTargets(string theme, int width, int height)
    {
        var (profile, cache, favorites) = Fixture();
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        settings.Theme = theme; settings.DisplayOffset = 1; settings.FractalIndex = IndexJson.ToElement(profile);
        store.SaveSettings(settings); store.SaveFavorites("Default", favorites);
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = height };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 125; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexScene3Tags")); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Find<AutoCompleteBox>(dialog, "IndexTagEditorInput").Text = "Shared from index";
            Click(Find<Button>(dialog, "IndexSaveTags")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Lead", "live", "Bright", "Shared from index"], store.LoadFavorites("Default")[0].Tags);
            Capture(window, $"shared-tags-index-{theme}-{width}");
            Click(Find<Button>(window, "NavFavorites"));
            var live = Field<List<Favorite>>(window, "_favorites");
            Invoke(window, "ShowFavoriteEditor", live[0], false); Dispatcher.UIThread.RunJobs();
            Assert.Equal(live[0].Tags, Field<List<string>>(window, "_favEditingTags"));
            Assert.Equal("Add shared scene tag", AutomationProperties.GetName(Find<TextBox>(window, "FavoriteTagInput")));
            Find<TextBox>(window, "FavoriteTagInput").Text = "Draft solo";
            Field<NumericUpDown>(window, "_favSceneSpinner").Value = 4;
            Assert.Equal(["Rhythm"], Field<List<string>>(window, "_favEditingTags"));
            Assert.Equal("", Find<TextBox>(window, "FavoriteTagInput").Text);
            Field<NumericUpDown>(window, "_favSceneSpinner").Value = 3;
            Assert.Equal("Draft solo", Find<TextBox>(window, "FavoriteTagInput").Text);
            Assert.DoesNotContain("Draft solo", store.LoadFavorites("Default")[0].Tags);
            while (Find<Border>(window, "FavoriteEditorCard").GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => AutomationProperties.GetName(button)?.StartsWith("Remove tag ", StringComparison.Ordinal) == true) is { } remove)
            { Click(remove); }
            Assert.Empty(Field<List<string>>(window, "_favEditingTags"));
            Find<TextBox>(window, "FavoriteTagInput").Text = "Shared from favorite";
            Capture(window, $"shared-tags-editor-{theme}-{width}");
            Click(Find<Button>(window, "FavoriteSave")); Dispatcher.UIThread.RunJobs();
            var saved = IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex);
            Assert.Equal(["Shared from favorite"], saved.Find(cache.Device.Id, cache.Committed!.Presets[125], "12.00")!.SceneTags[2]);
            Assert.Equal(["Shared from favorite"], live[0].Tags); Assert.Equal(live[0].Tags, live[1].Tags);
            Assert.Equal(["Rhythm"], live[2].Tags); Assert.Empty(live[3].Tags);
            Find<TextBox>(window, "FavoriteSearchInput").Text = "Shared from favorite"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, Field<ListBox>(window, "_favListBox").ItemCount);
            Capture(window, $"shared-tags-favorites-{theme}-{width}");
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
        var reopened = new MainWindow(store.LoadSettings(), [], midi, profileStore: store);
        try { Assert.Equal(["Shared from favorite"], Field<List<Favorite>>(reopened, "_favorites")[0].Tags); }
        finally { reopened.Close(); }
    }

    [AvaloniaFact]
    public void RetargetingNewFavoritesAndProfileSwitchingUseTheSelectedScenesTags()
    {
        var (profile, cache, favorites) = Fixture();
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        settings.DisplayOffset = 1; settings.FractalIndex = IndexJson.ToElement(profile);
        store.SaveSettings(settings); store.SaveFavorites("Default", favorites);
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        store.Create("Second", new ProfileSettings
        {
            DisplayOffset = 1,
            FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }),
        }, [new Favorite { Id = 1, Slot = 1, Name = "Studio", Preset = 126, Scene = 3, Tags = ["Studio"] }]);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store, confirm: (_, _, _, _) => Task.FromResult(true));
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavFavorites"));
            var live = Field<List<Favorite>>(window, "_favorites");
            Invoke(window, "ShowFavoriteEditor", live[0], false);
            Field<NumericUpDown>(window, "_favSceneSpinner").Value = 4;
            Assert.Equal(["Rhythm"], Field<List<string>>(window, "_favEditingTags"));
            Find<TextBox>(window, "FavoriteTagInput").Text = "Verse";
            Click(Find<Button>(window, "FavoriteSave"));
            Assert.Equal(4, live[0].Scene); Assert.Equal(["Rhythm", "Verse"], live[0].Tags);
            Assert.Equal(["Lead", "live", "Bright"], live[1].Tags);
            Assert.Equal(live[0].Tags, live[2].Tags);

            typeof(MainWindow).GetField("_currentPreset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 126);
            typeof(MainWindow).GetField("_activeScene", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 3);
            Invoke(window, "ShowFavoriteEditor", new Favorite(), true);
            Assert.Equal(live[1].Tags, Field<List<string>>(window, "_favEditingTags"));
            Find<TextBox>(window, "FavoriteName").Text = "New solo";
            Click(Find<Button>(window, "FavoriteSave"));
            Assert.Equal(live[1].Tags, live[^1].Tags);
            Invoke(window, "ShowFavoriteEditor", live[^1], false);
            Find<TextBox>(window, "FavoriteTagInput").Text = "Cancelled";
            Click(Find<Button>(window, "FavoriteCancel"));
            Assert.DoesNotContain("Cancelled", live[^1].Tags);

            Click(Find<Button>(window, "NavConfig"));
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Second"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", settings.ActiveProfile); Assert.Equal(["Studio"], live.Single().Tags);
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Default"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Lead", "live", "Bright"], live[1].Tags);
            Assert.Equal(["Rhythm", "Verse"], live[0].Tags);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoriteSharedSaveFailureKeepsTheDraftAndRollsBackAnnotations()
    {
        var (profile, cache, favorites) = Fixture();
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        settings.DisplayOffset = 1; settings.FractalIndex = IndexJson.ToElement(profile);
        store.SaveSettings(settings); store.SaveFavorites("Default", favorites);
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store);
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavFavorites"));
            var live = Field<List<Favorite>>(window, "_favorites");
            Invoke(window, "ShowFavoriteEditor", live[0], false);
            var original = settings.FractalIndex!.Value.GetRawText();
            var before = Directory.GetFiles(_directory, "*.json").ToDictionary(path => path, File.ReadAllBytes);
            Find<TextBox>(window, "FavoriteTagInput").Text = "Unsaved";
            using (var locked = new FileStream(Path.Combine(_directory, "Default-settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click(Find<Button>(window, "FavoriteSave")); Dispatcher.UIThread.RunJobs();
                Assert.Equal(original, settings.FractalIndex!.Value.GetRawText());
                Assert.Equal(["Lead", "live", "Bright"], live[0].Tags);
                Assert.Equal(live[0].Tags, live[1].Tags);
                Assert.True(Find<Border>(window, "FavoriteEditorCard").IsVisible);
                Assert.Contains("Unsaved", Field<List<string>>(window, "_favEditingTags"));
                foreach (var (path, bytes) in before) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
            }
            Click(Find<Button>(window, "FavoriteSave"));
            Assert.Contains("Unsaved", live[0].Tags); Assert.Equal(live[0].Tags, live[1].Tags);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Invoke(MainWindow window, string method, params object[] args)
    {
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window window, string name) => FavoriteEditorTests.CaptureSendConfirmation(window, name);
}
