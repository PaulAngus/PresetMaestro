using System.Diagnostics;
using Avalonia;
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

public sealed class PresetIndexTableInteractionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-index-tables-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ThousandPresetCatalogKeepsItsTablesSelectionAndNavigationWithSceneDock(string theme)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme; settings.DisplayOffset = 1;
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.AxeFxIIIMarkII, "12.00");
        var presets = Enumerable.Range(0, 1024).ToDictionary(i => i, i => FractalIndexWorkflowTests.Preset(i, device.Variant));
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex
        {
            Device = device,
            Committed = new IndexScan { Status = "Complete", Firmware = "12.00", FinishedAt = DateTimeOffset.UtcNow, Presets = presets },
        });
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var watch = Stopwatch.StartNew();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1440, Height = 850 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var list = Find<ListBox>(window, "IndexPresetList");
            var panel = Find<FavoriteTablesPanel>(window, "IndexTableLayout");
            Assert.Equal(1024, list.ItemCount);
            Snapshot("catalog-1440");
            Assert.True(panel.Columns == 5, $"Columns {panel.Columns}, list width {list.Bounds.Width}, panel width {panel.Bounds.Width}");
            var originalItems = list.Items.Cast<ListBoxItem>().ToArray();
            list.SelectedIndex = 125; Dispatcher.UIThread.RunJobs();
            var origin = (ListBoxItem)list.SelectedItem!;
            origin.Focus();
            var dock = Find<Border>(window, "IndexInspectorFrame");
            Assert.True(dock.IsVisible);
            Assert.Equal(5, panel.Columns);
            Assert.True(dock.TranslatePoint(default, window)!.Value.Y >= list.TranslatePoint(default, window)!.Value.Y + list.Bounds.Height);
            Assert.Equal(list.Bounds.Width, dock.Bounds.Width, 0);
            Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());

            foreach (var size in new (int Width, int Height, int Columns)[] { (1000, 640, 3), (1440, 850, 5), (1520, 850, 5), (1521, 850, 5), (1522, 850, 6), (1920, 1000, 7), (2400, 1000, 9), (1000, 640, 3) })
            {
                window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs();
                Assert.Equal(size.Columns, panel.Columns);
                Assert.Equal(size.Columns, Find<Grid>(window, "IndexTableHeaders").Children.Count);
                Assert.Same(origin, list.SelectedItem);
                Assert.Same(origin, window.FocusManager?.GetFocusedElement());
                Assert.Equal(originalItems, list.Items.Cast<ListBoxItem>());
                var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 1, $"Tables extent {viewer.Extent.Width}, viewport {viewer.Viewport.Width}");
                Assert.True(list.Bounds.Height >= 140);
                Assert.True(dock.Bounds.Bottom <= Find<Grid>(window, "IndexResultsPane").Bounds.Height + 1);
                var sceneColumns = Find<Grid>(window, "IndexScenes").Children.Select(Grid.GetColumn).Distinct().Count();
                Assert.Equal(2, sceneColumns);
                Snapshot($"dock-{size.Width}");
            }

            window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1023, list.SelectedIndex);
            AssertVisible((Control)list.SelectedItem!);
            window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(682, list.SelectedIndex);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(682, list.SelectedIndex); // Down cannot cross the end of this table.
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1023, list.SelectedIndex);
            window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.InRange(list.SelectedIndex, 683, 1022);
            AssertVisible((Control)list.SelectedItem!);

            var selected = (ListBoxItem)list.SelectedItem!;
            var close = Find<Button>(window, "IndexCloseInspector");
            var closePosition = close.TranslatePoint(default, window);
            var tabs = Find<TabStrip>(window, "IndexDetailTabs");
            tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.True(Find<StackPanel>(window, "IndexAmpSection").IsVisible);
            Assert.Equal(closePosition, close.TranslatePoint(default, window));
            tabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            var lastScene = Find<Grid>(window, "IndexScene8Row"); lastScene.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(closePosition, close.TranslatePoint(default, window));
            Assert.True(close.IsEffectivelyVisible);
            Assert.True(close.TranslatePoint(default, window)!.Value.Y + close.Bounds.Height <= window.Bounds.Height);
            lastScene.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.False(dock.IsVisible);
            Assert.Same(selected, list.SelectedItem);
            Assert.Same(selected, window.FocusManager?.GetFocusedElement());
            Assert.Equal(3, panel.Columns);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.True(dock.IsVisible);
            AssertVisible(selected);
            Click(Find<Button>(window, "IndexCloseInspector"));
            window.CaptureRenderedFrame()?.Dispose();
            var selectedPosition = selected.TranslatePoint(new Point(55, 12), window)!.Value;
            window.MouseDown(selectedPosition, MouseButton.Left); window.MouseUp(selectedPosition, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(dock.IsVisible);
            Assert.Same(selected, list.SelectedItem);
            Assert.Empty(midi.Sent);
            watch.Stop();
            // Diagnostic timing is recorded without a machine-dependent pass threshold.
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null) { File.WriteAllText(Path.Combine(output, $"adaptive-index-{theme.ToLowerInvariant()}-timing.txt"), $"1024-preset load, 8 reflows, navigation and dock interactions: {watch.Elapsed.TotalSeconds:F2}s"); }
        }
        finally { window.Close(); }

        void Snapshot(string name)
        {
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(output, $"adaptive-index-{name}-{theme.ToLowerInvariant()}.png"));
        }
        void AssertVisible(Control control)
        {
            var viewer = Find<ListBox>(window, "IndexPresetList").GetVisualDescendants().OfType<ScrollViewer>().Single();
            var position = control.TranslatePoint(default, viewer)!.Value;
            Assert.True(position.Y >= -1 && position.Y + control.Bounds.Height <= viewer.Viewport.Height + 1, $"Selected row at {position.Y}, height {control.Bounds.Height}, viewport {viewer.Viewport.Height}");
        }
    }
}
