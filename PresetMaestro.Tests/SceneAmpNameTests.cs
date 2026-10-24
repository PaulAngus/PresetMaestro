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

// Constructed library data and a headless UI: component evidence only.
public sealed class SceneAmpNameTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Maestro-amp-names-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaTheory]
    [InlineData(1000, "Light", 0)]
    [InlineData(1000, "Dark", 0)]
    [InlineData(1440, "Light", 0)]
    [InlineData(1440, "Dark", 0)]
    [InlineData(1000, "Light", 1)]
    [InlineData(1000, "Dark", 1)]
    [InlineData(1440, "Light", 1)]
    [InlineData(1440, "Dark", 1)]
    public void NamingModePreservesScenesPaneGeometrySelectionAndNavigation(int width, string theme, int view)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        int[] ids = [268, 267, 266, 284];
        string[] names = ["USA JP IIC+ Green", "USA JP IIC+ Yellow", "USA JP IIC+ Red", "USA JP IIC+ Red Shred"];
        var amp = new IndexedAmpBlock(1, ids.Select((id, i) => new IndexedAmpChannel(i, new(id, names[i], null, "component fixture", true))).ToArray(),
            Enumerable.Range(0, 8).Select(i => new AmpSceneState(i % 4, i == 7)).ToArray());
        var sample = FractalIndexWorkflowTests.Preset(82) with { Name = "Petrucci Rig 2025 FM9", Amps = [amp] };
        var unknown = amp with { BlockNumber = 2, Channels = amp.Channels.Select(c => c with { Model = new(9999, "Unknown model with a deliberately long name", null, "component fixture", false) }).ToArray() };
        var presets = Enumerable.Range(0, 512).ToDictionary(i => i, i => FractalIndexWorkflowTests.Preset(i));
        presets[82] = sample; presets[83] = sample with { Slot = 83, Amps = [amp, unknown] };
        presets[84] = sample with { Slot = 84, Amps = [] };
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex
        {
            Device = device,
            Committed = new IndexScan { Status = "Complete", Firmware = "12.00", FinishedAt = DateTimeOffset.UtcNow, Presets = presets },
        });
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = width == 1000 ? 640 : 850 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            Find<TabStrip>(window, "IndexViewTabs").SelectedIndex = view; Dispatcher.UIThread.RunJobs();
            var list = Find<ListBox>(window, view == 0 ? "IndexPresetList" : "IndexSceneList");
            list.SelectedIndex = view == 0 ? 82 : 82 * 8; Dispatcher.UIThread.RunJobs();
            foreach (int slot in new[] { 82, 83, 84 })
            {
                list.SelectedIndex = view == 0 ? slot : slot * 8; Dispatcher.UIThread.RunJobs();
                if (view == 1) { Click(Find<Button>(window, "IndexSceneDetails")); }
                var sceneRows = Find<Grid>(window, "IndexScenes").Children.OfType<Grid>().ToArray();
                var fractal = Find<ToggleButton>(window, "IndexAmpNameFractal");
                var real = Find<ToggleButton>(window, "IndexAmpNameReal");
                Click(fractal);
                Assert.True(fractal.IsChecked); Assert.False(real.IsChecked);
                var frame = Find<Border>(window, "IndexInspectorFrame");
                var geometry = frame.Bounds;
                var rowSizes = sceneRows.Select(r => r.Bounds).ToArray();
                var selection = list.SelectedItem;
                var viewer = Find<ScrollViewer>(window, "IndexInspectorScroll");
                var offset = viewer.Offset;
                var action = Find<Button>(window, "IndexScene8GoTo");
                bool canGo = action.IsEnabled;
                real.Focus();
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.True(real.IsChecked); Assert.False(fractal.IsChecked);
                Assert.Same(real, window.FocusManager?.GetFocusedElement());
                Assert.Same(selection, list.SelectedItem); Assert.Equal(offset, viewer.Offset);
                Assert.Equal(geometry, frame.Bounds); Assert.Equal(rowSizes, sceneRows.Select(r => r.Bounds));
                Assert.Same(action, Find<Button>(window, "IndexScene8GoTo")); Assert.Equal(canGo, action.IsEnabled);
                Assert.Equal(8, sceneRows.Length); Assert.Equal(2, sceneRows.Select(Grid.GetColumn).Distinct().Count());
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "IndexDetailTabs" or "IndexAmpSection");
                for (int scene = 1; scene <= 8; scene++)
                {
                    var summary = Find<StackPanel>(window, $"IndexScene{scene}AmpSummary");
                    string text = AutomationProperties.GetName(summary)!;
                    Assert.Contains(slot == 84 ? "No Amp blocks" : $"Mesa/Boogie JP-2C · {(char)('A' + (scene - 1) % 4)}", text);
                    if (slot == 83) { Assert.Contains("Real amp not mapped (Unknown model with a deliberately long name)", text); }
                    if (slot != 84 && scene == 8) { Assert.Contains("D (off)", text); }
                    foreach (var line in summary.Children.OfType<SceneAmpNamePanel>())
                    {
                        var suffix = line.Children.OfType<TextBlock>().Last();
                        Assert.True(suffix.Bounds.Right <= line.Bounds.Width + 1);
                        Assert.True(suffix.TextLayout.Width <= suffix.Bounds.Width + 1);
                    }
                }
                if (slot == 82)
                {
                    string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
                    if (output is not null)
                    {
                        Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
                        using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(output, $"scene-amp-names-{width}-{theme.ToLowerInvariant()}-{view}-real.png"));
                    }
                }
                // Clicking the selected mode cannot leave the group with no selection.
                Click(real); Assert.True(real.IsChecked); Assert.False(fractal.IsChecked);
                window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.True(fractal.IsChecked); Assert.False(real.IsChecked);
                Assert.Same(fractal, window.FocusManager?.GetFocusedElement());
                if (slot != 84) { Assert.Contains(names[0], AutomationProperties.GetName(Find<StackPanel>(window, "IndexScene1AmpSummary"))); }
                Click(real);
                Click(Find<Button>(window, "IndexCloseInspector"));
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.True(Find<ToggleButton>(window, "IndexAmpNameReal").IsChecked);
            }
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }
}
