using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class LibraryManagementTests : IDisposable
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

    [AvaloniaFact]
    public void ManagerSeparatesCreateSaveAndAssignAndDeletesUnusedLibrariesOnlyAfterConfirmation()
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
            Find<TextBox>(window, "IndexDeviceName").Text = "Studio FM9";
            Click(window, "IndexUpdateDevice");
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
        tagged.Tags = ["Remove with library"];
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

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string name)
    { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Select(ListBox list, Guid id)
    { list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => (Guid)i.Tag! == id); Dispatcher.UIThread.RunJobs(); }
}
