using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class ProfileLibraryImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-library-import-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    private static DeviceIndex Cache(string name = "Default", FractalDeviceVariant variant = FractalDeviceVariant.FM9) => new()
    {
        Device = new(Guid.NewGuid(), name, variant, "12.00"),
        PresetMapping = new(3, 1, 35),
        Committed = new IndexScan
        {
            Status = "Complete",
            Firmware = "12.00",
            FinishedAt = DateTimeOffset.UtcNow,
            Presets = Enumerable.Range(0, 512).ToDictionary(s => s, s => FractalIndexWorkflowTests.Preset(s, variant)),
        },
    };

    private static (ProfileStore Store, AppSettings Settings, IndexLibrary Library) Setup(string folder, DeviceIndex cache)
    {
        var store = new ProfileStore(folder);
        var settings = store.LoadSettings();
        var library = new IndexLibrary(Path.Combine(folder, "FractalIndex")); library.Save(cache);
        var profile = new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id };
        profile.GetOrCreate(cache.Device.Id, cache.Committed!.Presets[125], "12.00").SceneTags[2] = ["Lead"];
        profile.Reattach(profile.Annotations.Single().Id, cache.Committed.Presets[126], "12.00", new Dictionary<int, int> { [2] = 4 });
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        return (store, settings, library);
    }

    private string ExportForeign(DeviceIndex cache)
    {
        var source = Setup(Path.Combine(_directory, "Source"), cache).Store;
        string path = Path.Combine(_directory, "export.zip"); source.Export("Default", path); return path;
    }

    [Fact]
    public void SameLibraryImportReusesCurrentDataMappingAndIndependentTagHistory()
    {
        var cache = Cache();
        var (store, settings, library) = Setup(_directory, cache);
        string path = Path.Combine(_directory, "export.zip"); store.Export("Default", path);
        var data = ProfileStore.ReadImport(path);
        Assert.Equal(cache.Device.Id, IndexJson.ReadProfile(data.Settings.FractalIndex).SelectedDeviceId);
        cache.Device = cache.Device with { Name = "Stage FM9" };
        cache.PresetMapping = new(8, 0, 58);
        cache.Committed!.Presets[0] = cache.Committed.Presets[0] with { Name = "Newer local preset" };
        library.Save(cache);
        string before = JsonSerializer.Serialize(library.Load(cache.Device.Id), IndexJson.Options);
        string name = store.Import(data, "Gig");
        var importedSettings = store.LoadProfile(name);
        var imported = IndexJson.ReadProfile(importedSettings.FractalIndex);
        Assert.Equal(cache.Device.Id, imported.SelectedDeviceId);
        Assert.Equal("Stage FM9", imported.AssignedDevice!.Name);
        Assert.Empty(imported.PortableSnapshots);
        Assert.NotEqual(IndexJson.ReadProfile(settings.FractalIndex).ProfileId, imported.ProfileId);
        Assert.Equal(8, importedSettings.MidiChannel); Assert.Equal(0, importedSettings.DisplayOffset); Assert.Equal(58, importedSettings.SceneCc);
        Assert.Equal("Lead", imported.Find(cache.Device.Id, cache.Committed.Presets[126], "12.00")!.SceneTags[4].Single());
        imported.UndoReview();
        Assert.Equal("Lead", imported.Find(cache.Device.Id, cache.Committed.Presets[125], "12.00")!.SceneTags[2].Single());
        Assert.Single(library.ListDevices());
        Assert.Equal(before, JsonSerializer.Serialize(library.Load(cache.Device.Id), IndexJson.Options));
        Assert.Equal("Default", store.LoadSettings().ActiveProfile);
    }

    [Fact]
    public void ForeignLibrarySharesOnlyWhenChosenAndRetainsHistoricalTags()
    {
        var local = Cache(); var foreign = Cache();
        var (store, _, library) = Setup(_directory, local);
        string path = ExportForeign(foreign);
        string before = JsonSerializer.Serialize(library.Load(local.Device.Id), IndexJson.Options);
        string name = store.Import(path, "Gig", libraryId: local.Device.Id);
        var profile = IndexJson.ReadProfile(store.LoadProfile(name).FractalIndex);
        Assert.Equal(local.Device.Id, profile.SelectedDeviceId);
        Assert.Empty(profile.PortableSnapshots);
        Assert.Equal("Lead", profile.Find(local.Device.Id, local.Committed!.Presets[126], "12.00")!.SceneTags[4].Single());
        profile.UndoReview();
        Assert.Equal("Lead", profile.Find(local.Device.Id, local.Committed.Presets[125], "12.00")!.SceneTags[2].Single());
        Assert.Equal(before, JsonSerializer.Serialize(library.Load(local.Device.Id), IndexJson.Options));
        Assert.Single(library.ListDevices());
    }

    [Fact]
    public void ForeignImportDoesNotGuessIdentityFromSameNameAndModelAndNamesCopiesClearly()
    {
        var local = Cache(); var foreign = Cache();
        var (store, _, library) = Setup(_directory, local);
        var data = ProfileStore.ReadImport(ExportForeign(foreign));
        string name = store.Import(data, "Gig");
        var settings = store.LoadProfile(name);
        var imported = IndexJson.ReadProfile(settings.FractalIndex);
        var snapshot = Assert.Single(imported.PortableSnapshots);
        Assert.NotEqual(local.Device.Id, snapshot.Device.Id); Assert.NotEqual(foreign.Device.Id, snapshot.Device.Id);
        Assert.Equal("Default · imported for Gig", snapshot.Device.Name); Assert.True(snapshot.Imported);
        Assert.Equal(512, snapshot.Committed!.Presets.Count);
        Assert.Equal("Lead", imported.Find(snapshot.Device.Id, snapshot.Committed.Presets[126], "12.00")!.SceneTags[4].Single());
        // Materialising and repeating an import must not collide with an earlier copy.
        library.Save(snapshot);
        var repeat = IndexJson.ReadProfile(store.LoadProfile(store.Import(data, "Gig", overwrite: true)).FractalIndex);
        Assert.Equal("Default · imported for Gig (2)", repeat.AssignedDevice!.Name);
        Assert.Equal(foreign.Device.Id, IndexJson.ReadProfile(data.Settings.FractalIndex).SelectedDeviceId);
    }

    [Fact]
    public void KnownLibraryCanBeKeptAsASeparateCopyAndInvalidChoicesWriteNothing()
    {
        var cache = Cache(); var (store, _, library) = Setup(_directory, cache);
        string path = Path.Combine(_directory, "export.zip"); store.Export("Default", path);
        string name = store.Import(path, "Backup", keepImportedLibrary: true);
        var profile = IndexJson.ReadProfile(store.LoadProfile(name).FractalIndex);
        Assert.NotEqual(cache.Device.Id, profile.SelectedDeviceId);
        Assert.Equal("Default · imported for Backup", Assert.Single(profile.PortableSnapshots).Device.Name);
        Assert.Throws<InvalidOperationException>(() => store.Import(path, "Missing", libraryId: Guid.NewGuid()));
        var other = Cache("FM3", FractalDeviceVariant.FM3); library.Save(other);
        Assert.Throws<InvalidOperationException>(() => store.Import(path, "Wrong model", libraryId: other.Device.Id));
        Assert.False(store.ContainsName("Missing")); Assert.False(store.ContainsName("Wrong model"));
        Assert.Equal(2, library.ListDevices().Count);
    }

    [AvaloniaTheory]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    public async Task ConnectedImportRecommendsCurrentLibraryAndExplainsExistingDuplicates(int width, string theme)
    {
        var local = Cache(); var foreign = Cache();
        var (store, settings, library) = Setup(_directory, local);
        string path = ExportForeign(foreign);
        var duplicate = IndexJson.Clone(foreign); duplicate.Imported = true; library.Save(duplicate);
        store.Create("Old import", new ProfileSettings { FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [duplicate.Device], SelectedDeviceId = duplicate.Device.Id }) });
        library.Save(Cache("FM3", FractalDeviceVariant.FM3));
        settings.Theme = theme; settings.MidiInputPort = "FM9"; settings.MidiOutputPort = "FM9";
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-fm9")); }
        };
        var window = new MainWindow(settings, [], midi, profileStore: store, profileFilePicker: _ => Task.FromResult<string?>(path))
        { Width = width, Height = 850, ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync();
            foreach (var sync in window.OwnedWindows.ToArray()) { sync.Close(); }
            Click(window, "ManageProfiles");
            int sentBeforeImport = midi.Sent.Count;
            var importButton = Find<Button>(window, "ProfileImport"); importButton.Focus(); Click(window, "ProfileImport");
            var dialog = Assert.Single(window.OwnedWindows);
            var selection = Find<ComboBox>(dialog, "ProfileImportLibrary");
            Assert.Contains("Used by Default (recommended)", selection.SelectedItem!.ToString());
            Assert.Contains("device selected for the current profile", Find<TextBlock>(dialog, "ProfileImportLibraryDetail").Text);
            Assert.DoesNotContain(selection.Items, item => item!.ToString()!.Contains("FM3", StringComparison.Ordinal));
            Find<TextBox>(dialog, "ProfileName").Text = "Gig"; Dispatcher.UIThread.RunJobs();
            FavoriteEditorTests.CaptureSendConfirmation(dialog, $"import-device-current-{width}-{theme}");
            selection.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
            FavoriteEditorTests.CaptureSendConfirmation(dialog, $"import-device-options-{width}-{theme}");
            selection.IsDropDownOpen = false;
            Click(dialog, "ProfileNameSave"); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            var imported = IndexJson.ReadProfile(store.LoadProfile("Gig").FractalIndex);
            Assert.Equal(local.Device.Id, imported.SelectedDeviceId); Assert.Empty(imported.PortableSnapshots);
            Assert.Equal("Default", settings.ActiveProfile); Assert.Equal(3, library.ListDevices().Count);
            Assert.Contains("Shared device", Find<TextBlock>(window, "ProfileNoticeDetail").Text);
            Assert.Equal(sentBeforeImport, midi.Sent.Count);
            Click(window, "ProfilesBackToConfig");
            var assigned = Find<ComboBox>(window, "IndexAvailableDevices"); assigned.BringIntoView(); Dispatcher.UIThread.RunJobs();
            assigned.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
            FavoriteEditorTests.CaptureSendConfirmation(window, $"import-device-legacy-labels-{width}-{theme}");
            assigned.IsDropDownOpen = false; Click(window, "ManageLibraries");
            var list = Find<ListBox>(window, "ManagedLibraries");
            string[] names = list.Items.OfType<ListBoxItem>().SelectMany(i => ((Grid)i.Content!).GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text ?? "").ToArray();
            Assert.Contains(names, n => n.Contains("Used by Default", StringComparison.Ordinal));
            Assert.Contains(names, n => n.Contains("Used by Old import", StringComparison.Ordinal));
            Assert.DoesNotContain(names, n => n.Contains(local.Device.Id.ToString("N")[..8], StringComparison.Ordinal));
            FavoriteEditorTests.CaptureSendConfirmation(window, $"import-device-manager-{width}-{theme}");
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) { dialog.Close(); } window.Close(); }
    }

    [AvaloniaFact]
    public async Task OfflineImportCanKeepCopyAndEscapeCancelsWithoutCreatingIt()
    {
        var local = Cache(); var (store, settings, library) = Setup(_directory, local);
        string path = Path.Combine(_directory, "export.zip"); store.Export("Default", path);
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store, profileFilePicker: _ => Task.FromResult<string?>(path));
        window.Show();
        try
        {
            Click(window, "ManageProfiles"); var origin = Find<Button>(window, "ProfileImport"); origin.Focus(); Click(window, "ProfileImport");
            var dialog = Assert.Single(window.OwnedWindows);
            var selection = Find<ComboBox>(dialog, "ProfileImportLibrary");
            selection.Focus(); dialog.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Keep a separate imported device", selection.SelectedItem!.ToString());
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows); Assert.Same(origin, window.FocusManager!.GetFocusedElement());
            Assert.Single(store.ListProfiles()); Assert.Single(library.ListDevices());
            Click(window, "ProfileImport"); dialog = Assert.Single(window.OwnedWindows);
            selection = Find<ComboBox>(dialog, "ProfileImportLibrary"); selection.SelectedIndex = selection.ItemCount - 1;
            Find<TextBox>(dialog, "ProfileName").Text = "Backup"; Click(dialog, "ProfileNameSave"); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            var imported = IndexJson.ReadProfile(store.LoadProfile("Backup").FractalIndex);
            Assert.Equal("Default · imported for Backup", imported.AssignedDevice!.Name);
            Assert.NotEqual(local.Device.Id, imported.SelectedDeviceId); Assert.Single(imported.PortableSnapshots);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReferenceOnlyImportExplainsMissingSavedDataAndAllowsChoosingLater()
    {
        var foreign = Cache();
        var source = Setup(Path.Combine(_directory, "Source"), foreign);
        source.Library.Delete(foreign.Device.Id);
        string path = Path.Combine(_directory, "reference.zip"); source.Store.Export("Default", path);
        var store = new ProfileStore(Path.Combine(_directory, "Target")); var settings = store.LoadSettings();
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store, profileFilePicker: _ => Task.FromResult<string?>(path));
        window.Show();
        try
        {
            Click(window, "ManageProfiles"); Click(window, "ProfileImport");
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Choose a device later", Find<ComboBox>(dialog, "ProfileImportLibrary").SelectedItem!.ToString());
            Assert.Contains("No saved presets", Find<TextBlock>(dialog, "ProfileImportLibraryDetail").Text);
            Find<TextBox>(dialog, "ProfileName").Text = "Offline"; Click(dialog, "ProfileNameSave"); await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("No saved device data was included", Find<TextBlock>(window, "ProfileNoticeDetail").Text);
            Assert.Empty(IndexJson.ReadProfile(store.LoadProfile("Offline").FractalIndex).PortableSnapshots);
            Assert.Empty(new IndexLibrary(Path.Combine(store.DirectoryPath, "FractalIndex")).ListDevices());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UnreadableProfileCannotHideSavedLibraryChoicesOrBreakAssignment()
    {
        var local = Cache(); var (store, settings, library) = Setup(_directory, local);
        var copy = Cache(); copy.Imported = true; library.Save(copy);
        store.Create("Unreadable");
        string invalidPath = Path.Combine(_directory, "Unreadable-settings.json"); File.WriteAllText(invalidPath, "{invalid");
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store);
        window.Show();
        try
        {
            var choices = Find<ComboBox>(window, "IndexAvailableDevices");
            Assert.Equal(2, choices.ItemCount);
            choices.SelectedItem = choices.Items.OfType<IndexDevice>().Single(d => d.Id == copy.Device.Id); Dispatcher.UIThread.RunJobs();
            Assert.Equal(copy.Device.Id, IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).SelectedDeviceId);
            Assert.Equal("{invalid", File.ReadAllText(invalidPath));
        }
        finally { window.Close(); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string name)
    { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
}
