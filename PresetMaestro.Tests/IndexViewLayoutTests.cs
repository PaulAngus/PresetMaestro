using System.Text.Json;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class IndexViewLayoutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-view-layout-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    [AvaloniaTheory]
    [InlineData(1000, 640, "Light")]
    [InlineData(1000, 640, "Dark")]
    [InlineData(1440, 850, "Light")]
    [InlineData(1440, 850, "Dark")]
    public void IndexHeaderAndResultsRetainTheirExpectedVerticalFootprint(int width, int height, string theme)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.AxeFxIIIMarkII, "12.00");
        var presets = Enumerable.Range(0, 1024).ToDictionary(i => i, i => FractalIndexWorkflowTests.Preset(i, device.Variant));
        presets[0] = presets[0] with { Name = "A very long preset name with repeated clean scenes", SceneNames = ["Clean", "Clean", "Warm clean", "Lead", "Rhythm", "Solo", "Ambient", ""] };
        var cache = new DeviceIndex { Device = device, Committed = new IndexScan { Status = "Complete", Firmware = "12.00", FinishedAt = DateTimeOffset.UtcNow, Presets = presets } };
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        profile.GetOrCreate(device.Id, presets[0], "12.00").SceneTags[0] = ["Clean", "Stage"];
        // Deliberately keep a review action visible, as well as a four-digit selected preset.
        profile.Annotations.Add(new PresetAnnotation { DeviceId = device.Id, Slot = 1, Name = "Previous", ContentSha256 = "old", Firmware = "12.00", Tags = ["Review"] });
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = height };
        window.Show();
        try
        {
            Find<Button>("NavPresetIndex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Capture("closed");
            var list = Find<ListBox>("IndexPresetList"); list.SelectedIndex = 1023; Dispatcher.UIThread.RunJobs();
            Capture("open");
            Find<Button>("IndexCloseInspector").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var chooser = Find<TabStrip>("IndexViewTabs");
            chooser.SelectedIndex = 1; Dispatcher.UIThread.RunJobs(); Capture("scenes-closed");
            Find<TextBox>("IndexSearchInput").Text = "clean"; Dispatcher.UIThread.RunJobs();
            Find<ListBox>("IndexSceneList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); Capture("scenes-selected");
            Assert.False(Find<Border>("IndexInspectorFrame").IsVisible);
            var details = Find<Button>("IndexSceneDetails");
            details.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); Capture("scenes-open");
            Assert.Equal(28, chooser.Bounds.Height);
            var header = Find<StackPanel>("IndexSendControls").GetVisualAncestors().OfType<Grid>().First();
            Assert.True(chooser.Bounds.Width + Find<StackPanel>("IndexSendControls").Bounds.Width < header.Bounds.Width);
            // Resume/Cancel can share the row with review and the four-digit readout.
            Find<TextBox>("IndexSearchInput").Text = ""; Dispatcher.UIThread.RunJobs();
            Find<ListBox>("IndexSceneList").SelectedIndex = 8191; Dispatcher.UIThread.RunJobs();
            var liveCache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            liveCache.LastAttempt = new IndexScan { Status = "Partial" };
            typeof(MainWindow).GetField("_indexScanning", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            typeof(MainWindow).GetMethod("UpdateIndexButtons", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Dispatcher.UIThread.RunJobs(); Capture("scenes-sync-actions");
            var commands = (StackPanel)Find<StackPanel>("IndexSendControls").Parent!;
            Assert.True(chooser.TranslatePoint(default, header)!.Value.X + chooser.Bounds.Width + 12 <= details.TranslatePoint(default, header)!.Value.X);
            Assert.True(details.TranslatePoint(default, header)!.Value.X + details.Bounds.Width + 12 <= commands.TranslatePoint(default, header)!.Value.X);
            Assert.True(commands.Bounds.Right <= header.Bounds.Width);
            Assert.Equal(28, header.Bounds.Height);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }

        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        void Capture(string state)
        {
            window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
            var send = Find<StackPanel>("IndexSendControls");
            var title = send.GetVisualAncestors().OfType<Grid>().First();
            var search = Find<Border>("IndexSearchEntry");
            var results = Find<Grid>("IndexResultsPane");
            var measurements = new[] { title.Bounds.Height, search.TranslatePoint(default, window)!.Value.Y, search.Bounds.Width, results.TranslatePoint(default, window)!.Value.Y, results.Bounds.Height };
            // Measured Windows baselines: Presets reclaims the removed 28-pixel
            // instruction footer; Scenes still displays its result-count footer.
            if (OperatingSystem.IsWindows() && state is "closed" or "open" or "scenes-closed" or "scenes-selected" or "scenes-open")
            {
                bool presetView = state is "closed" or "open";
                double[] before = width == 1000
                    ? [28, 108, 627, 148, presetView ? 459 : 431]
                    : [28, 108, 1067, 148, presetView ? 669 : 641];
                Assert.Equal(before, measurements);
            }
            var nav = Find<Button>("NavPresetIndex");
            Assert.True(nav.Bounds.Width >= nav.DesiredSize.Width);
            var text = nav.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.True(text.Bounds.Right <= nav.Bounds.Width);
            Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            var name = $"view-{width}-{theme.ToLowerInvariant()}-{state}";
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(measurements));
            using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(output, name + ".png"));
            string? baseline = Environment.GetEnvironmentVariable("PRESET_MAESTRO_LAYOUT_BASELINE");
            if (baseline is not null && state is "closed" or "open")
            {
                var before = JsonSerializer.Deserialize<double[]>(File.ReadAllText(Path.Combine(baseline, name + ".json")))!;
                Assert.Equal(before, measurements);
            }
        }
    }
}
