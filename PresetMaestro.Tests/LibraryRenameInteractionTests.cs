using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed partial class LibraryManagementTests
{
    private (MainWindow Window, ProfileStore Store, AppSettings Settings, IndexLibrary Library, IndexDevice Device, DeviceMidi Midi) RenameWindow(int width = 1440, int height = 850, string theme = "Light")
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var device = new IndexDevice(Guid.NewGuid(), "Stage FM9", FractalDeviceVariant.FM9, "12.00");
        var preset = FractalIndexWorkflowTests.Preset(0);
        library.Save(new DeviceIndex
        {
            Device = device,
            PresetMapping = new(7, 1, 58),
            Committed = new IndexScan { Status = "Complete", Firmware = "12.00", FinishedAt = DateTimeOffset.UtcNow, Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot, slot => FractalIndexWorkflowTests.Preset(slot)) },
        });
        library.SaveDevice(device with { Id = Guid.NewGuid(), Name = "Studio" });
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        profile.GetOrCreate(device.Id, preset, "12.00").Tags = ["Keep"];
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        store.Create("Shared", ProfileSettings.From(settings));
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = height };
        window.Show(); Dispatcher.UIThread.RunJobs();
        Click(window, "ManageLibraries");
        return (window, store, settings, library, device, midi);
    }

    private static async Task RenameLibrary(Window window, string name)
    {
        Click(window, "IndexRenameDevice");
        var dialog = Assert.Single(window.OwnedWindows);
        Find<TextBox>(dialog, "LibraryName").Text = name;
        Click(dialog, "LibraryNameSave");
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);
    }

    [AvaloniaTheory]
    [InlineData(1440, 850, "Light")]
    [InlineData(1440, 850, "Dark")]
    [InlineData(1000, 640, "Light")]
    [InlineData(1000, 640, "Dark")]
    public async Task RenameDialogValidatesAndPreservesLibraryDataAssignmentsAndMappingDrafts(int width, int height, string theme)
    {
        var (window, store, settings, library, device, midi) = RenameWindow(width, height, theme);
        try
        {
            Assert.Equal(device.Name, Find<TextBlock>(window, "ManagedLibrary").Text);
            Assert.False(Find<TextBox>(window, "IndexDeviceName").IsEffectivelyVisible);
            var channel = Find<ComboBox>(window, "MidiChannel");
            var offset = Find<ComboBox>(window, "DisplayOffset");
            var scene = Find<NumericUpDown>(window, "SceneCc");
            channel.SelectedIndex = 9; offset.SelectedIndex = 0; scene.Value = 62;
            var rename = Find<Button>(window, "IndexRenameDevice");
            rename.BringIntoView(); rename.Focus(); Dispatcher.UIThread.RunJobs();
            FavoriteEditorTests.CaptureSendConfirmation(window, $"device-static-name-{width}-{theme}");
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Rename device", dialog.Title);
            var input = Find<TextBox>(dialog, "LibraryName");
            Assert.Equal(device.Name, input.Text);
            Assert.Equal(device.Name, input.SelectedText);
            Assert.Same(input, dialog.FocusManager!.GetFocusedElement());
            Assert.Equal("Device name", AutomationProperties.GetName(input));
            FavoriteEditorTests.CaptureSendConfirmation(dialog, $"device-rename-dialog-{width}-{theme}");

            input.Text = " studio ";
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Contains("already exists", Find<TextBlock>(dialog, "LibraryNameError").Text);
            Assert.Equal(device.Name, library.Load(device.Id)!.Device.Name);
            input.Text = "";
            Click(dialog, "LibraryNameSave");
            Assert.Contains("1–80", Find<TextBlock>(dialog, "LibraryNameError").Text);
            FavoriteEditorTests.CaptureSendConfirmation(dialog, $"device-rename-error-{width}-{theme}");

            string newName = "Rehearsal device with a long name for the stage rig and alternate MIDI routing";
            input.Text = newName;
            dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Assert.Same(rename, window.FocusManager!.GetFocusedElement());
            var saved = library.Load(device.Id)!;
            Assert.Equal(device with { Name = newName }, saved.Device);
            Assert.Equal(new DevicePresetMapping(7, 1, 58), saved.PresetMapping);
            Assert.Equal(FractalIndexWorkflowTests.Preset(0).Name, saved.Committed!.Presets[0].Name);
            Assert.Equal(9, channel.SelectedIndex); Assert.Equal(0, offset.SelectedIndex); Assert.Equal(62, scene.Value);
            Assert.Equal(7, settings.MidiChannel);
            Assert.Equal(newName, Find<TextBlock>(window, "ManagedLibrary").Text);
            Assert.Equal(device.Id, (Guid)((ListBoxItem)Find<ListBox>(window, "ManagedLibraries").SelectedItem!).Tag!);
            foreach (string name in new[] { "Default", "Shared" })
            {
                var profile = IndexJson.ReadProfile(store.LoadProfile(name).FractalIndex);
                Assert.Equal(device.Id, profile.SelectedDeviceId);
                Assert.Equal("Keep", Assert.Single(profile.Annotations).Tags.Single());
            }
            FavoriteEditorTests.CaptureSendConfirmation(window, $"device-renamed-{width}-{theme}");

            // Saving mapping edits cannot rename the library through stale hidden input.
            Find<TextBox>(window, "IndexDeviceName").Text = "Ignored stale name";
            Click(window, "IndexUpdateDevice");
            Assert.Equal(newName, library.Load(device.Id)!.Device.Name);
            Assert.Equal(new DevicePresetMapping(9, 0, 62), library.Load(device.Id)!.PresetMapping);
            Click(window, "LibrariesBackToConfig");
            Assert.Equal(newName, ((IndexDevice)Find<ComboBox>(window, "IndexAvailableDevices").SelectedItem!).Name);
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Cancel")]
    [InlineData("CancelEnter")]
    [InlineData("Escape")]
    [InlineData("Window")]
    public async Task CancellingLibraryRenameKeepsSavedNameDraftMappingAndReturnsFocus(string dismissal)
    {
        var (window, _, _, library, device, midi) = RenameWindow();
        try
        {
            string path = Path.Combine(library.DirectoryPath, device.Id.ToString("N") + ".json");
            byte[] before = File.ReadAllBytes(path);
            Find<ComboBox>(window, "MidiChannel").SelectedIndex = 11;
            var rename = Find<Button>(window, "IndexRenameDevice"); rename.Focus();
            Click(window, "IndexRenameDevice");
            var dialog = Assert.Single(window.OwnedWindows);
            Find<TextBox>(dialog, "LibraryName").Text = "Discarded name";
            if (dismissal == "Cancel") { Click(dialog, "LibraryNameCancel"); }
            else if (dismissal == "CancelEnter") { Find<Button>(dialog, "LibraryNameCancel").Focus(); dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); }
            else if (dismissal == "Escape") { dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); }
            else { dialog.Close(); }
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(11, Find<ComboBox>(window, "MidiChannel").SelectedIndex);
            Assert.Same(rename, window.FocusManager!.GetFocusedElement());
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [WindowsFileLockAvaloniaFact]
    public async Task LibraryRenameSaveFailureKeepsDialogOpenAndAllowsRetry()
    {
        var (window, _, _, library, device, midi) = RenameWindow();
        try
        {
            Click(window, "IndexRenameDevice");
            var dialog = Assert.Single(window.OwnedWindows);
            Find<TextBox>(dialog, "LibraryName").Text = "Renamed stage";
            using (var locked = new FileStream(Path.Combine(library.DirectoryPath, device.Id.ToString("N") + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Click(dialog, "LibraryNameSave");
                Assert.Single(window.OwnedWindows);
                Assert.True(Find<TextBlock>(dialog, "LibraryNameError").IsVisible);
                Assert.Equal(device.Name, library.Load(device.Id)!.Device.Name);
            }
            Click(dialog, "LibraryNameSave");
            await Task.Yield(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            Assert.Equal("Renamed stage", library.Load(device.Id)!.Device.Name);
            Assert.Equal(new DevicePresetMapping(7, 1, 58), library.Load(device.Id)!.PresetMapping);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }
}
