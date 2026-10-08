using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class PresetDetailsHeaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-details-header-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaTheory]
    [InlineData(1000, 640, "Light")]
    [InlineData(1000, 640, "Dark")]
    [InlineData(1440, 850, "Light")]
    [InlineData(1440, 850, "Dark")]
    public void CompactHeaderKeepsActionsReachableAndTagEditingWorksWithLongNames(int width, int height, string theme)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme; settings.DisplayOffset = 1;
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        var shortName = FractalIndexWorkflowTests.Preset(142) with { Name = "Dlx Rv RAT" };
        var longName = FractalIndexWorkflowTests.Preset(143) with { Name = "Deluxe Reverb with RAT, modulation and a very long preset name" };
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex
        {
            Device = device,
            Committed = new IndexScan { Status = "Complete", Firmware = "12.00", FinishedAt = DateTimeOffset.UtcNow, Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot, slot => slot == 142 ? shortName : slot == 143 ? longName : FractalIndexWorkflowTests.Preset(slot)) },
        });
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        profile.GetOrCreate(device.Id, longName, "12.00").Tags = ["Live", "Overdrive", "Modulation"];
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = height };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var list = Find<ListBox>(window, "IndexPresetList");
            foreach (int index in new[] { 0, 1 })
            {
                list.SelectedIndex = 142 + index; Dispatcher.UIThread.RunJobs();
                var origin = (ListBoxItem)list.SelectedItem!;
                var tabs = Find<TabStrip>(window, "IndexDetailTabs");
                var close = Find<Button>(window, "IndexCloseInspector");
                var header = Find<Grid>(window, "IndexInspectorToolbar");
                Control[] controls = [Find<Panel>(window, "IndexInspectorIdentity"), tabs, Find<Button>(window, "IndexGoToPreset"), Find<Button>(window, "IndexPresetTags"), close];
                double previousRight = 0;
                foreach (var control in controls)
                {
                    var position = control.TranslatePoint(default, header)!.Value;
                    Assert.True(position.X >= previousRight - 1 && position.Y >= 0);
                    Assert.True(position.X + control.Bounds.Width <= header.Bounds.Width + 1);
                    Assert.True(position.Y + control.Bounds.Height <= header.Bounds.Height + 1);
                    previousRight = position.X + control.Bounds.Width;
                }
                Assert.True(header.Bounds.Height <= 32);
                Assert.Equal("Close details", AutomationProperties.GetName(close));
                Assert.Equal("Close details (Esc)", ToolTip.GetTip(close));
                Assert.Contains(index == 0 ? shortName.Name : longName.Name, AutomationProperties.GetName(controls[0]));
                Snapshot(index, "scenes");

                Find<TabStripItem>(window, "IndexShowScenes").Focus();
                window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.True(Find<StackPanel>(window, "IndexAmpSection").IsVisible);
                Snapshot(index, "amps");
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.Same(controls[3], window.FocusManager?.GetFocusedElement()); // Offline Go to is skipped.
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.Same(close, window.FocusManager?.GetFocusedElement());
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
                Assert.Same(origin, window.FocusManager?.GetFocusedElement());
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();

                Click(Find<Button>(window, "IndexPresetTags"));
                var editor = Assert.Single(window.OwnedWindows);
                Find<AutoCompleteBox>(editor, "IndexTagEditorInput").Text = "Header tag";
                Click(Find<Button>(editor, "IndexSaveTags"));
                var saved = IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex);
                Assert.Contains("Header tag", saved.Find(device.Id, index == 0 ? shortName : longName, "12.00")!.Tags);
                if (index == 1) { Assert.Contains("Live", saved.Find(device.Id, longName, "12.00")!.Tags); }
                Assert.Empty(midi.Sent);
            }
        }
        finally { window.Close(); }

        void Snapshot(int index, string view)
        {
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            using var bitmap = window.CaptureRenderedFrame();
            bitmap!.Save(Path.Combine(output, $"compact-preset-header-{width}-{theme.ToLowerInvariant()}-{(index == 0 ? "short" : "long")}-{view}.png"));
        }
    }
}
