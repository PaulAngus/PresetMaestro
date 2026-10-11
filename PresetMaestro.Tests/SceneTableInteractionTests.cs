using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed partial class DeviceNavigationTests
{
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void SceneTagsDoNotMoveColumnsAndOverflowRetainsTheFullSceneIdentity(string theme)
    {
        var (window, midi) = Window(theme: theme, width: 1320);
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var cache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var profile = (IndexProfile)typeof(MainWindow).GetField("_indexProfile", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var preset = cache.Committed!.Presets[1] with
            {
                Name = "65 Bassman Blackface",
                SceneNames = ["Normal", "Bass", "Normal Drive", "Bass Drive", "Normal FX", "Bass FX", "Normal Dry", "Bass Dry"],
            };
            cache.Committed.Presets[1] = preset;
            var annotation = profile.GetOrCreate(cache.Device.Id, preset, "12.00");
            annotation.SceneTags[2] = ["Fender", "Mid Gain"];
            annotation.SceneTags[3] = ["A very long tag that cannot fit", "Fender", "Mid Gain", "Live"];
            IndexView(window, 1);
            var list = Find<ListBox>(window, "IndexSceneList");
            foreach (int width in new[] { 1000, 1298, 1320, 1360, 1440, 1920 })
            {
                window.Width = width; Dispatcher.UIThread.RunJobs();
                var header = Find<Grid>(window, "IndexSceneTable0Header");
                var headings = header.Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).ToArray();
                var baseline = list.ContainerFromIndex(8)!.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 5)
                    .Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).ToArray();
                foreach (int index in new[] { 8, 10, 11 })
                {
                    var item = (ListBoxItem)list.ContainerFromIndex(index)!;
                    var row = item.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 5);
                    var labels = row.Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).ToArray();
                    for (int column = 0; column < labels.Length; column++)
                    {
                        Assert.InRange(Math.Abs(headings[column].TranslatePoint(default, list)!.Value.X -
                            labels[column].TranslatePoint(default, list)!.Value.X), 0, 1);
                        Assert.InRange(Math.Abs(headings[column].Bounds.Width - labels[column].Bounds.Width), 0, 1);
                        Assert.Equal(baseline[column].TranslatePoint(default, list)!.Value.X,
                            labels[column].TranslatePoint(default, list)!.Value.X, 3);
                        Assert.Equal(baseline[column].Bounds.Width, labels[column].Bounds.Width, 3);
                    }
                    var tags = row.Children.OfType<SceneTagSummaryPanel>().Single();
                    if (index == 10) { Assert.Equal(width == 1000 ? 2 : 1, tags.VisibleTagCount); }
                    if (index == 11) { Assert.Equal(0, tags.VisibleTagCount); }
                    Assert.All(tags.Children.Take(tags.VisibleTagCount), chip => Assert.True(chip.Bounds.Right <= tags.Bounds.Width));
                    if (index == 10) { Assert.Equal("Fender, Mid Gain", ToolTip.GetTip(tags)); }
                    Assert.Contains("65 Bassman Blackface", Avalonia.Automation.AutomationProperties.GetName(item));
                    if (index == 10) { Assert.Contains("Fender, Mid Gain", Avalonia.Automation.AutomationProperties.GetName(item)); }
                }
                string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
                    using var bitmap = window.CaptureRenderedFrame();
                    bitmap!.Save(Path.Combine(output, $"scene-tags-{width}-{theme.ToLowerInvariant()}.png"));
                }
            }
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void SceneTablesReflowAtTheirMinimumWidthAndKeepSelectionFocusAndNavigation(string theme)
    {
        var (window, midi) = Window(theme: theme, capacity: 1024);
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var cache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var profile = (IndexProfile)typeof(MainWindow).GetField("_indexProfile", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            foreach (int slot in cache.Committed!.Presets.Keys.ToArray())
            {
                var preset = cache.Committed.Presets[slot] with
                {
                    Name = "Plexi 100W Jumped + 4x12",
                    SceneNames = ["Clean + chorus + delay", "Edge of breakup", "Warm clean with a very long scene name", "Lead", "Rhythm", "Solo", "Ambient", ""],
                };
                cache.Committed.Presets[slot] = preset;
                profile.GetOrCreate(cache.Device.Id, preset, "12.00").SceneTags[0] = ["Clean", "Stage"];
            }
            IndexView(window, 1);
            var list = Find<ListBox>(window, "IndexSceneList");
            var panel = Find<SceneTablesPanel>(window, "IndexSceneTableLayout");
            Assert.Equal(8192, list.ItemCount);
            var originalItems = list.Items.Cast<IndexedScene>().ToArray();
            list.SelectedIndex = 4097; Dispatcher.UIThread.RunJobs();
            var selected = (ListBoxItem)list.ContainerFromIndex(list.SelectedIndex)!;
            selected.Focus();
            var selection = list.SelectedItem;
            foreach (var size in new (int Width, int Height, int Columns)[]
            {
                (1000, 640, 1), (1297, 850, 1), (1298, 850, 2), (1440, 850, 2),
                (1917, 850, 2), (1918, 850, 3), (1920, 1000, 3), (2560, 1000, 4), (1000, 640, 1), (1920, 1000, 3),
            })
            {
                window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs();
                Assert.Equal(size.Columns, panel.Columns);
                Assert.Equal(size.Columns, Find<Grid>(window, "IndexSceneHeaders").Children.Count);
                Assert.Same(selection, list.SelectedItem);
                Assert.Same(selected, window.FocusManager?.GetFocusedElement());
                Assert.Equal(originalItems, list.Items.Cast<IndexedScene>());
                Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
                var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 1);
                AssertVisible(selected);
                foreach (var header in Find<Grid>(window, "IndexSceneHeaders").Children.OfType<Grid>())
                {
                    Assert.True(header.Bounds.Width >= 594);
                    foreach (var label in header.Children.OfType<TextBlock>()) { Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1); }
                }
                // Inspect the full five-field headers and representative names/tags
                // at the actual transition widths, with the first row in each table.
                viewer.Offset = default; Dispatcher.UIThread.RunJobs();
                Snapshot($"{size.Width}-closed");
                var ranges = FavoriteTableLayout.Distribute(list.ItemCount, panel.Columns);
                foreach (var range in ranges)
                {
                    var first = (ListBoxItem)list.ContainerFromIndex(range.Start)!;
                    Assert.Same(originalItems[range.Start], first.DataContext);
                    var row = first.GetVisualDescendants().OfType<Grid>().First(g => g.ColumnDefinitions.Count == 5);
                    var labels = row.Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).ToArray();
                    Assert.True(labels[0].TextLayout.Width <= labels[0].Bounds.Width + 1);
                    Assert.True(labels[2].TextLayout.Width <= labels[2].Bounds.Width + 1);
                    Assert.InRange(row.Children.OfType<SceneTagSummaryPanel>().Single().Bounds.Width, 110, double.MaxValue);
                }
                Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, size.Columns * 40 + 1);
                list.ScrollIntoView(list.SelectedIndex); Dispatcher.UIThread.RunJobs();
                AssertVisible(selected);
            }
            var three = FavoriteTableLayout.Distribute(list.ItemCount, 3);
            selected.Focus(); Press(window, PhysicalKey.Home); Assert.Equal(0, list.SelectedIndex);
            Press(window, PhysicalKey.ArrowRight); Assert.Equal(three[1].Start, list.SelectedIndex);
            Press(window, PhysicalKey.ArrowDown); Assert.Equal(three[1].Start + 1, list.SelectedIndex);
            Press(window, PhysicalKey.ArrowRight); Assert.Equal(three[2].Start + 1, list.SelectedIndex);
            Press(window, PhysicalKey.End); Assert.Equal(8191, list.SelectedIndex);
            Press(window, PhysicalKey.ArrowDown); Assert.Equal(8191, list.SelectedIndex);
            Press(window, PhysicalKey.ArrowLeft); Assert.Equal(three[1].Start + three[2].Count - 1, list.SelectedIndex);
            Press(window, PhysicalKey.PageUp); Assert.InRange(list.SelectedIndex, three[1].Start, three[1].Start + three[1].Count - 2);
            AssertVisible((Control)list.ContainerFromIndex(list.SelectedIndex)!);
            Press(window, PhysicalKey.Enter); Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal(3, panel.Columns);
            foreach (var size in new (int Width, int Height, int Columns)[] { (1000, 640, 1), (1440, 850, 2) })
            {
                window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs();
                Assert.Equal(size.Columns, panel.Columns);
                Assert.Same(list.ContainerFromIndex(list.SelectedIndex), window.FocusManager?.GetFocusedElement());
                AssertVisible((Control)list.ContainerFromIndex(list.SelectedIndex)!);
                Snapshot($"{size.Width}-open");
            }
            Find<Button>(window, "IndexCloseInspector").Focus(); Press(window, PhysicalKey.Escape);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Same(list.ContainerFromIndex(list.SelectedIndex), window.FocusManager?.GetFocusedElement());
            AssertVisible((Control)list.ContainerFromIndex(list.SelectedIndex)!);
            IndexView(window, 0);
            Assert.Equal(5, Find<FavoriteTablesPanel>(window, "IndexTableLayout").Columns);
            IndexView(window, 1); Assert.Equal(2, panel.Columns);
            Find<TextBox>(window, "IndexSearchInput").Text = "No matching scene"; Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.Items); Assert.Empty(panel.GetVisualChildren().OfType<ListBoxItem>());
            Assert.True(Find<TextBlock>(window, "IndexEmptyState").IsVisible);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }

        void AssertVisible(Control control)
        {
            var viewer = Find<ListBox>(window, "IndexSceneList").GetVisualDescendants().OfType<ScrollViewer>().Single();
            var position = control.TranslatePoint(default, viewer)!.Value;
            Assert.InRange(position.Y, -1, viewer.Viewport.Height - control.Bounds.Height + 1);
        }
        void Snapshot(string state)
        {
            window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(output, $"scene-tables-{state}-{theme.ToLowerInvariant()}.png"));
        }
    }
}
