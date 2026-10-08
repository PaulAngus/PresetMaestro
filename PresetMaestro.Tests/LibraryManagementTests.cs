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

public sealed partial class LibraryManagementTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-libraries-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    [Fact]
    public void NewAndRenamedLibrariesRequireUniqueNamesButLegacyDuplicatesRemainEditable()
    {
        var library = new IndexLibrary(_directory);
        var first = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9);
        var duplicate = first with { Id = Guid.NewGuid() };
        library.Save(new DeviceIndex { Device = first });
        library.Save(new DeviceIndex { Device = duplicate });
        Assert.Throws<ArgumentException>(() => library.SaveDevice(first with { Id = Guid.NewGuid(), Name = " default " }));
        library.SaveDevice(duplicate with { Firmware = "12.00" });
        library.SaveDevice(duplicate with { Name = "Backup" });
        Assert.Throws<ArgumentException>(() => library.SaveDevice(duplicate));
        Assert.Equal("Backup", library.Load(duplicate.Id)!.Device.Name);
        Assert.Equal(2, library.ListDevices().Count);
    }

    [AvaloniaTheory]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    public void AssignmentAndManagementOfferSavedLibrariesWithoutDroppingHistoricalReferences(int width, string theme)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var saved = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9);
        var historical = saved with { Id = Guid.NewGuid(), Name = "Defautt" };
        library.SaveDevice(saved, new(1, 0, 34));
        var profile = new IndexProfile { Devices = [historical, saved], SelectedDeviceId = saved.Id };
        profile.GetOrCreate(historical.Id, FractalIndexWorkflowTests.Preset(0), null).Tags = ["Keep history"];
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = 850 };
        window.Show();
        try
        {
            var choices = Find<ComboBox>(window, "IndexAvailableDevices");
            Assert.Equal(saved.Id, Assert.Single(choices.Items.OfType<IndexDevice>()).Id);
            Assert.Equal(saved.Id, ((IndexDevice)choices.SelectedItem!).Id);
            choices.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
            FavoriteEditorTests.CaptureSendConfirmation(window, $"saved-device-choices-{width}-{theme}");
            choices.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs();
            Click(window, "ManageLibraries");
            var list = Find<ListBox>(window, "ManagedLibraries");
            Assert.Equal(saved.Id, (Guid)Assert.Single(list.Items.OfType<ListBoxItem>()).Tag!);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"saved-device-manager-{width}-{theme}");

            Click(window, "IndexCreateDevice");
            Find<TextBox>(window, "IndexDeviceName").Text = "Studio";
            Click(window, "IndexUpdateDevice");
            var created = library.ListDevices().Single(d => d.Name == "Studio");
            var managedIds = list.Items.OfType<ListBoxItem>().Select(item => (Guid)item.Tag!).ToArray();
            Click(window, "LibrariesBackToConfig");
            choices = Find<ComboBox>(window, "IndexAvailableDevices");
            Assert.Equal(managedIds, choices.Items.OfType<IndexDevice>().Select(d => d.Id));
            Assert.DoesNotContain(IndexJson.ReadProfile(settings.FractalIndex).Devices, d => d.Id == created.Id);
            choices.Focus(); window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(created.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            var persisted = IndexJson.ReadProfile(store.LoadProfile(settings.ActiveProfile).FractalIndex);
            Assert.Contains(persisted.Devices, d => d.Id == historical.Id);
            Assert.Equal("Keep history", Assert.Single(persisted.Annotations).Tags.Single());
            Assert.Null(library.Load(historical.Id));
            Assert.Equal(2, library.ListDevices().Count);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LibraryRemovedAfterChoicesLoadCannotReplaceAssignmentWithAnEmptyLibrary()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var assigned = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9);
        var other = assigned with { Id = Guid.NewGuid(), Name = "Studio" };
        library.SaveDevice(assigned, new(1, 0, 34)); library.SaveDevice(other, new(2, 1, 55));
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [assigned], SelectedDeviceId = assigned.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store);
        window.Show();
        try
        {
            var choices = Find<ComboBox>(window, "IndexAvailableDevices");
            var staleChoice = choices.Items.OfType<IndexDevice>().Single(d => d.Id == other.Id);
            library.Delete(other.Id);
            choices.SelectedItem = staleChoice; Dispatcher.UIThread.RunJobs();
            Assert.Equal(assigned.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            Assert.Equal(assigned.Id, ((IndexDevice)choices.SelectedItem!).Id);
            Assert.Equal(assigned.Id, IndexJson.ReadProfile(store.LoadProfile(settings.ActiveProfile).FractalIndex).SelectedDeviceId);
            Assert.Equal(assigned.Id, Assert.Single(choices.Items.OfType<IndexDevice>()).Id);
            Assert.Null(library.Load(other.Id));
            Assert.Contains("no longer available", Find<TextBlock>(window, "IndexDeviceStatus").Text!);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LibraryMappingPreservesLegacyValuesAndOnlyAppliesSavedEditsToTheAssignedDevice()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = "Light";
        settings.MidiChannel = 7; settings.DisplayOffset = 1; settings.SceneCc = 58;
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var first = new IndexDevice(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9);
        var second = first with { Id = Guid.NewGuid(), Name = "Studio" };
        library.SaveDevice(first);
        library.SaveDevice(second, new(3, 0, 40));
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [first], SelectedDeviceId = first.Id });
        store.SaveSettings(settings);
        store.Create("Shared", ProfileSettings.From(settings));
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store, confirm: (_, _, _, _) => Task.FromResult(true));
        window.Show();
        try
        {
            Assert.Equal(new DevicePresetMapping(7, 1, 58), library.Load(first.Id)!.PresetMapping);
            Click(window, "NavConfig");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "MidiChannel" or "DisplayOffset" or "SceneCc");
            Click(window, "ManageLibraries");
            var list = Find<ListBox>(window, "ManagedLibraries");
            Select(list, second.Id);
            var channel = Find<ComboBox>(window, "MidiChannel");
            var offset = Find<ComboBox>(window, "DisplayOffset");
            var sceneCc = Find<NumericUpDown>(window, "SceneCc");
            Assert.Equal(3, channel.SelectedIndex);
            channel.SelectedIndex = 9; offset.SelectedIndex = 1; sceneCc.Value = 62;
            Assert.Equal(7, settings.MidiChannel);
            Assert.Equal(new DevicePresetMapping(3, 0, 40), library.Load(second.Id)!.PresetMapping);
            Select(list, first.Id); Select(list, second.Id);
            Assert.Equal(3, channel.SelectedIndex); // Selecting another library discards the draft.
            channel.SelectedIndex = 9; offset.SelectedIndex = 1; sceneCc.Value = 62;
            Click(window, "IndexUpdateDevice");
            Assert.Equal(new DevicePresetMapping(9, 1, 62), library.Load(second.Id)!.PresetMapping);
            Assert.Equal(7, settings.MidiChannel);
            Assert.Equal(58, settings.SceneCc);
            Select(list, first.Id);
            channel.SelectedIndex = 11; offset.SelectedIndex = 0; sceneCc.Value = 70;
            Click(window, "IndexUpdateDevice");
            Assert.Equal(11, settings.MidiChannel); Assert.Equal(0, settings.DisplayOffset); Assert.Equal(70, settings.SceneCc);
            Assert.Equal(511, settings.MaxDisplayedPreset);
            Assert.Equal(11, store.LoadProfile("Shared").MidiChannel);
            Click(window, "LibrariesBackToConfig");
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Shared";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Shared", settings.ActiveProfile);
            Assert.Equal(70, settings.SceneCc);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
        var restored = new ProfileStore(_directory).LoadSettings();
        Assert.Equal(11, restored.MidiChannel); Assert.Equal(0, restored.DisplayOffset); Assert.Equal(70, restored.SceneCc);
    }

    [AvaloniaFact]
    public void AssigningALegacyLibraryAdoptsItsPreviousProfilesMapping()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var first = new IndexDevice(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9);
        var second = first with { Id = Guid.NewGuid(), Name = "Studio" };
        library.SaveDevice(first, new(9, 0, 34)); library.SaveDevice(second);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [first], SelectedDeviceId = first.Id });
        store.SaveSettings(settings);
        store.Create("Studio profile", new ProfileSettings
        {
            MidiChannel = 4,
            DisplayOffset = 1,
            SceneCc = 55,
            FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [second], SelectedDeviceId = second.Id }),
        });
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store);
        window.Show();
        try
        {
            Click(window, "NavConfig");
            var choices = Find<ComboBox>(window, "IndexAvailableDevices");
            choices.SelectedItem = choices.Items.OfType<IndexDevice>().Single(d => d.Id == second.Id);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new DevicePresetMapping(4, 1, 55), library.Load(second.Id)!.PresetMapping);
            Assert.Equal(4, settings.MidiChannel); Assert.Equal(1, settings.DisplayOffset); Assert.Equal(55, settings.SceneCc);
            Assert.Equal(second.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MappingSaveFailureKeepsLibraryAndLiveMappingUnchangedAndAllowsRetry()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var device = new IndexDevice(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9);
        library.SaveDevice(device, new(5, 0, 34));
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store);
        window.Show();
        try
        {
            Click(window, "NavConfig"); Click(window, "ManageLibraries");
            Find<ComboBox>(window, "MidiChannel").SelectedIndex = 8;
            Find<ComboBox>(window, "DisplayOffset").SelectedIndex = 1;
            Find<NumericUpDown>(window, "SceneCc").Value = 60;
            using (var locked = new FileStream(Path.Combine(library.DirectoryPath, device.Id.ToString("N") + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click(window, "IndexUpdateDevice");
                Assert.Equal(new DevicePresetMapping(5, 0, 34), library.Load(device.Id)!.PresetMapping);
                Assert.Equal(5, settings.MidiChannel); Assert.Equal(0, settings.DisplayOffset); Assert.Equal(34, settings.SceneCc);
                Assert.NotEqual("Device changes saved.", Find<TextBlock>(window, "LibraryManagementStatus").Text);
                Assert.Equal(8, Find<ComboBox>(window, "MidiChannel").SelectedIndex);
            }
            Click(window, "IndexUpdateDevice");
            Assert.Equal(new DevicePresetMapping(8, 1, 60), library.Load(device.Id)!.PresetMapping);
            Assert.Equal(8, settings.MidiChannel); Assert.Equal(1, settings.DisplayOffset); Assert.Equal(60, settings.SceneCc);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void LibraryMappingTravelsWithExportsAndSurvivesRenameAndFirmwareUpdates()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var device = new IndexDevice(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9);
        var mapping = new DevicePresetMapping(12, 1, 64);
        library.SaveDevice(device, mapping);
        library.SaveDevice(device with { Name = "Renamed", Firmware = "12.00" });
        Assert.Equal(mapping, library.Load(device.Id)!.PresetMapping);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        store.SaveSettings(settings);
        string archive = Path.Combine(_directory, "mapping.zip");
        store.Export(settings.ActiveProfile, archive);
        var target = new ProfileStore(Path.Combine(_directory, "Imported"));
        target.LoadSettings();
        string name = target.Import(archive, "Imported mapping");
        var imported = target.LoadProfile(name);
        Assert.Equal(12, imported.MidiChannel); Assert.Equal(1, imported.DisplayOffset); Assert.Equal(64, imported.SceneCc);
        Assert.Equal(mapping, IndexJson.ReadProfile(imported.FractalIndex).PortableSnapshots.Single().PresetMapping);
        Assert.Throws<InvalidDataException>(() => library.SaveDevice(device with { Name = "Renamed", Firmware = "12.00" }, new(17, 0, 34)));
        Assert.Equal(mapping, library.Load(device.Id)!.PresetMapping);
    }

    [AvaloniaFact]
    public async Task ManagerSeparatesCreateSaveAndAssignAndDeletesUnusedLibrariesOnlyAfterConfirmation()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = "Light";
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var first = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9);
        var second = first with { Id = Guid.NewGuid() };
        foreach (var device in new[] { first, second }) { library.Save(new DeviceIndex { Device = device }); }
        var profile = new IndexProfile { Devices = [first, second], SelectedDeviceId = first.Id };
        profile.GetOrCreate(first.Id, FractalIndexWorkflowTests.Preset(0), null).Tags = ["Keep"];
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        store.Create("Other", new ProfileSettings { FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [second], SelectedDeviceId = second.Id }) });
        var midi = new DeviceMidi();
        bool approve = false;
        var window = new MainWindow(settings, [], midi, profileStore: store, confirm: (_, _, _, _) => Task.FromResult(approve)) { Width = 1200, Height = 800 };
        window.Show();
        try
        {
            Click(window, "NavConfig");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), c => c.Name == "IndexDeviceName");
            Click(window, "ManageLibraries");
            var list = Find<ListBox>(window, "ManagedLibraries");
            var names = list.Items.OfType<ListBoxItem>().Select(item => ((Grid)item.Content!).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text).ToArray();
            Assert.Equal(2, names.Distinct().Count());
            Assert.All(names, name => Assert.StartsWith("Default · ", name));
            Click(window, "IndexCreateDevice");
            Find<TextBox>(window, "IndexDeviceName").Text = "default";
            Click(window, "IndexUpdateDevice");
            Assert.Equal(2, library.ListDevices().Count);
            Assert.Contains("already exists", Find<TextBlock>(window, "LibraryManagementStatus").Text!);
            Find<TextBox>(window, "IndexDeviceName").Text = "Stage FM9";
            Click(window, "IndexUpdateDevice");
            var created = library.ListDevices().Single(d => d.Name == "Stage FM9");
            Assert.Equal(first.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            Assert.Equal("Save changes", Find<Button>(window, "IndexUpdateDevice").Content);
            Click(window, "IndexUpdateDevice");
            Assert.Equal(3, library.ListDevices().Count);
            Click(window, "IndexLinkDevice");
            Assert.Equal(created.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            Select(list, second.Id);
            Click(window, "IndexDeleteDevice");
            Assert.Contains("Other", Find<TextBlock>(window, "LibraryManagementStatus").Text!);
            Assert.NotNull(library.Load(second.Id));
            Select(list, first.Id);
            Click(window, "IndexDeleteDevice");
            Assert.NotNull(library.Load(first.Id));
            Assert.Equal("Keep", IndexJson.ReadProfile(settings.FractalIndex).Annotations.Single().Tags.Single());
            approve = true;
            Click(window, "IndexDeleteDevice");
            Assert.Null(library.Load(first.Id));
            Assert.Equal(2, library.ListDevices().Count);
            Assert.Empty(IndexJson.ReadProfile(settings.FractalIndex).Annotations);
            Select(list, second.Id);
            await RenameLibrary(window, "Studio FM9");
            Assert.Equal("Studio FM9", library.Load(second.Id)!.Device.Name);
            Assert.Equal(created.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            Assert.Empty(midi.Sent);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var capture = window.CaptureRenderedFrame(); capture!.Save(Path.Combine(output, "manage-libraries.png"));
                window.Width = 1000; window.Height = 680; Dispatcher.UIThread.RunJobs();
                using var narrow = window.CaptureRenderedFrame(); narrow!.Save(Path.Combine(output, "manage-libraries-narrow.png"));
                Click(window, "LibrariesBackToConfig");
                Find<Button>(window, "ManageLibraries").BringIntoView(); Dispatcher.UIThread.RunJobs();
                using var config = window.CaptureRenderedFrame(); config!.Save(Path.Combine(output, "library-config.png"));
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void DeletionRemovesHistoricalTagsReviewAndPortableSnapshotsFromEveryProfile()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var device = new IndexDevice(Guid.NewGuid(), "Unused", FractalDeviceVariant.FM9);
        var cache = new DeviceIndex { Device = device };
        library.Save(cache); library.Save(cache);
        var profile = new IndexProfile { Devices = [device], PortableSnapshots = [cache] };
        var tagged = profile.GetOrCreate(device.Id, FractalIndexWorkflowTests.Preset(0), null);
        tagged.Tags = ["Remove with device"];
        profile.Reattach(tagged.Id, FractalIndexWorkflowTests.Preset(1), null, new Dictionary<int, int>());
        // Keep an explicit different assignment so the legacy single-library fallback does not assign this one.
        var other = device with { Id = Guid.NewGuid(), Name = "Keep" };
        library.Save(new DeviceIndex { Device = other }); profile.AssignDevice(other);
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        store.Create("Second", new ProfileSettings { FractalIndex = IndexJson.ToElement(profile) });
        using (var locked = new FileStream(Path.Combine(library.DirectoryPath, device.Id.ToString("N") + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => store.DeleteIndexLibrary(device.Id, settings.ActiveProfile, settings.FractalIndex));
            foreach (string name in store.ListProfiles())
            { Assert.Single(IndexJson.ReadProfile(store.LoadProfile(name).FractalIndex).Annotations); }
            Assert.NotNull(library.Load(device.Id));
        }
        settings.FractalIndex = store.DeleteIndexLibrary(device.Id, settings.ActiveProfile, settings.FractalIndex);
        foreach (string name in store.ListProfiles())
        {
            var saved = IndexJson.ReadProfile(store.LoadProfile(name).FractalIndex);
            Assert.DoesNotContain(saved.Devices, d => d.Id == device.Id);
            Assert.Empty(saved.Annotations); Assert.Empty(saved.ReviewHistory); Assert.Empty(saved.PortableSnapshots);
            Assert.Equal(other.Id, saved.SelectedDeviceId);
        }
        Assert.Null(library.Load(device.Id));
        Assert.False(File.Exists(Path.Combine(library.DirectoryPath, device.Id.ToString("N") + ".json.bak")));
        Assert.Throws<InvalidOperationException>(() => store.DeleteIndexLibrary(other.Id, settings.ActiveProfile, settings.FractalIndex));
        Assert.NotNull(library.Load(other.Id));
    }

    [AvaloniaFact]
    public async Task DetectionStoresReadOnlyLibraryIdentityAndNameEditsCannotChangeIt()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = "Light";
        settings.MidiInputPort = "FM9"; settings.MidiOutputPort = "FM9";
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var device = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9);
        library.SaveDevice(device);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-fm9")); }
        };
        var window = new MainWindow(settings, [], midi, profileStore: store)
        { DeviceInformationTimeout = TimeSpan.FromMilliseconds(20), ThruInputRetryDelay = TimeSpan.Zero, Width = 1200, Height = 800 };
        typeof(MainWindow).GetField("_fractalIndexReader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(window, new PresetIndexReader(new FractalIndexWorkflowTests.NameSource(_ => "<EMPTY>"), AmpModelCatalogRegistry.CreateStarter()));
        window.Show();
        try
        {
            await window.ConnectAsync();
            Assert.Equal("12.00", library.Load(device.Id)!.Device.Firmware);
            Assert.Equal("12.00", IndexJson.ReadProfile(settings.FractalIndex).AssignedDevice!.Firmware);
            await window.SyncIndexAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Click(window, "NavConfig"); Click(window, "ManageLibraries");
            Assert.Equal("12.00", Find<TextBlock>(window, "IndexFirmware").Text);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), c => c.Name == "IndexFirmware");
            var choices = Find<ComboBox>(window, "IndexDeviceVariant");
            Assert.False(choices.IsVisible); Assert.False(choices.IsEnabled);
            choices.SelectedIndex = 1; // Programmatic stale UI cannot change the detected/saved model.
            Find<TextBlock>(window, "IndexFirmware").Text = "99.00";
            Find<TextBox>(window, "IndexDeviceName").Text = "Renamed";
            Click(window, "IndexUpdateDevice");
            Assert.Equal("Default", library.Load(device.Id)!.Device.Name);
            await RenameLibrary(window, "Renamed");
            var saved = library.Load(device.Id)!.Device;
            Assert.Equal("Renamed", saved.Name);
            Assert.Equal(FractalDeviceVariant.FM9, saved.Variant); Assert.Equal("12.00", saved.Firmware);
            Click(window, "IndexCreateDevice");
            Assert.False(choices.IsVisible); Assert.False(choices.IsEnabled);
            Assert.Single(choices.Items);
            Find<TextBox>(window, "IndexDeviceName").Text = "New connected device";
            Click(window, "IndexUpdateDevice");
            var created = library.ListDevices().Single(d => d.Name == "New connected device");
            Assert.Equal(FractalDeviceVariant.FM9, created.Variant); Assert.Equal("12.00", created.Firmware);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var capture = window.CaptureRenderedFrame(); capture!.Save(Path.Combine(output, "library-detected-firmware.png"));
            }
        }
        finally { window.Close(); }
        Assert.Equal("12.00", new IndexLibrary(library.DirectoryPath).Load(device.Id)!.Device.Firmware);
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string name)
    { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Select(ListBox list, Guid id)
    { list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (Guid)i.Tag! == id); Dispatcher.UIThread.RunJobs(); }
}
