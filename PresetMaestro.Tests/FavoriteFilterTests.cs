using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaFact]
    public void CompactTagIndicatorsAlignWithLabelsAndKeepCheckedVisuals()
    {
        var window = CreateWindow(favorites: [new Favorite { Id = 1, Name = "Clean", Tags = ["Fender", "Marshall"] }]);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "FavoriteTagFilter"));
            Dispatcher.UIThread.RunJobs();
            var options = Field<StackPanel>(window, "_favTagOptionsPanel");
            foreach (var option in options.Children.OfType<CheckBox>())
            {
                var box = option.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
                var label = option.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                var boxOrigin = box.TranslatePoint(default, option)!.Value;
                var labelOrigin = label.TranslatePoint(default, option)!.Value;
                Assert.Equal(new Size(16, 16), box.Bounds.Size);
                Assert.InRange(Math.Abs(boxOrigin.Y + box.Bounds.Height / 2 - labelOrigin.Y - label.Bounds.Height / 2), 0, 0.5);
                Assert.Equal(8, labelOrigin.X - boxOrigin.X - box.Bounds.Width);
                var glyph = option.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "CheckGlyph");
                Assert.Equal(option.IsChecked == true ? 1d : 0d, glyph.Opacity);
            }
            SetTag(options, "Fender", selected: true);
            Dispatcher.UIThread.RunJobs();
            var selected = options.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Fender"));
            Assert.Equal(1d, selected.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "CheckGlyph").Opacity);
            if (Environment.GetEnvironmentVariable("TAG_PICKER_SNAPSHOT") is { Length: > 0 } path)
            {
                var popup = Field<Border>(window, "_favTagFilterPopupBorder");
                using var bitmap = TopLevel.GetTopLevel(popup)!.CaptureRenderedFrame();
                bitmap!.Save(path);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MultiTagAllAnyExactAndCombinedFiltersDoNotChangeModelsOrSend()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 1, Slot = 1, Name = "Bright Lead", Category = "Clean", Tags = ["Fender", "warm"], Preset = 1, Scene = 1 },
            new() { Id = 2, Slot = 2, Name = "Bright Rhythm", Category = "Clean", Tags = ["fender"], Preset = 2, Scene = 2 },
            new() { Id = 3, Slot = 3, Name = "Bright Lead", Category = "Crunch", Tags = ["Fender", "Marshall"], Preset = 3, Scene = 3 },
            new() { Id = 4, Slot = 4, Name = "Bright Lead", Category = "Clean", Tags = ["Fenderish"], Preset = 4, Scene = 4 },
            new() { Id = 5, Slot = 5, Name = "Dark Lead", Category = "Clean", Tags = ["Marshall"], Preset = 5, Scene = 1 },
        };
        string before = System.Text.Json.JsonSerializer.Serialize(favorites);
        var midi = new FakeMidi { OutputOpen = true };
        int saves = 0;
        var window = CreateWindow(midi, favorites, saveFavorites: _ => saves++);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();

            var list = Field<ListBox>(window, "_favListBox");
            var collections = Field<ListBox>(window, "_favCategoryTree");
            var search = Field<TextBox>(window, "_favSearchBox");
            var trigger = Find<Button>(window, "FavoriteTagFilter");
            var label = Field<TextBlock>(window, "_favTagFilterLabel");
            var options = Field<StackPanel>(window, "_favTagOptionsPanel");
            Assert.Equal("Tags: All tags", label.Text);
            Assert.Equal(new[] { "All tags", "Fender", "Fenderish", "Marshall", "warm" },
                options.Children.OfType<CheckBox>().Select(option => option.Content));

            SetTag(options, "Fender", selected: true);
            Assert.Equal("Tags: Fender", label.Text);
            SetTag(options, "Marshall", selected: true);
            Assert.Equal("Tags: 2 selected", label.Text);
            Assert.Equal(new[] { 3 }, Slots(list));
            Click(trigger);
            Assert.True(Field<Popup>(window, "_favTagFilterPopup").IsOpen);
            Capture(window, "favorites-multi-tag-filter");

            Click(Field<ToggleButton>(window, "_favTagMatchAnyButton"));
            Assert.Equal(new[] { 1, 2, 3, 5 }, Slots(list));

            collections.SelectedIndex = 1; // Clean (case-insensitive alphabetical order).
            search.Text = "LEAD";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 1, 5 }, Slots(list));
            search.Text = "none";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.Items);
            Assert.Null(list.SelectedItem);
            Assert.True(Find<TextBlock>(window, "FavoriteEmptyResults").IsVisible);
            Click(Find<Button>(window, "FavoriteSendCommand"));

            search.Text = "lead";
            collections.SelectedIndex = 0;
            SetTag(options, "All tags", selected: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Tags: All tags", label.Text);
            Assert.Equal(new[] { 1, 3, 4, 5 }, Slots(list));
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(favorites));
            Assert.Equal(0, saves);
            Assert.Equal(0, midi.TotalSendCount);

            Assert.True(Field<Popup>(window, "_favTagFilterPopup").IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RebuildingTagOptionsPreservesValidSelectionsAndDropsOnlyStaleOnes()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 1, Slot = 1, Name = "One", Category = "Clean", Tags = ["Fender", "Marshall"] },
            new() { Id = 2, Slot = 2, Name = "Two", Category = "Clean", Tags = ["FENDER", "Vox"] },
        };
        var window = CreateWindow(favorites: favorites);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();

            var options = Field<StackPanel>(window, "_favTagOptionsPanel");
            SetTag(options, "Fender", selected: true);
            SetTag(options, "Marshall", selected: true);
            favorites[0].Tags = ["Fender"];
            Invoke(window, "RefreshFavoritesList", true, null!, null!);

            Assert.Equal("Tags: Fender", Field<TextBlock>(window, "_favTagFilterLabel").Text);
            Assert.Equal(new[] { "All tags", "Fender", "Vox" },
                options.Children.OfType<CheckBox>().Select(option => option.Content));
            Assert.True(options.Children.OfType<CheckBox>().Single(option => Equals(option.Content, "Fender")).IsChecked);
            Assert.Equal(new[] { 1, 2 }, Slots(Field<ListBox>(window, "_favListBox")));

            SetTag(options, "All tags", selected: true);
            Assert.Equal("Tags: All tags", Field<TextBlock>(window, "_favTagFilterLabel").Text);
            Assert.All(options.Children.OfType<CheckBox>().Where(option => option.Tag is string), option => Assert.False(option.IsChecked));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovedSelectedTagResetsToAllTagsAndListBodyScrolls()
    {
        var favorites = Enumerable.Range(1, 60).Select(i => new Favorite
        {
            Id = i,
            Slot = i,
            Name = $"Favorite {i}",
            Category = "Clean",
            Tags = ["Fender"],
            Preset = i == 1 ? 1024 : i,
            Scene = 3,
        }).ToList();
        var window = CreateWindow(favorites: favorites);
        try
        {
            window.Show();
            window.Width = 1200;
            window.Height = 760;
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();

            var list = Field<ListBox>(window, "_favListBox");
            var trigger = Find<Button>(window, "FavoriteTagFilter");
            var options = Field<StackPanel>(window, "_favTagOptionsPanel");
            var header = Find<Grid>(window, "FavoriteTableHeader");
            var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            var scrollbar = list.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == Orientation.Vertical);
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
            Assert.True(scrollbar.IsVisible);
            Assert.InRange(scrollbar.Bounds.Width, 5, 6);
            Assert.Equal(viewer.Viewport.Width, header.Bounds.Width, 1);
            Assert.Equal(6, ((Grid)((Grid)((Grid)((ListBoxItem)list.Items[0]!).Content!).Children[0]).Children[0]).ColumnDefinitions.Count);
            Assert.Equal(5, header.Children.OfType<GridSplitter>().Count());
            Assert.All(header.Children.OfType<GridSplitter>(), handle => Assert.Equal(Brushes.Transparent, handle.Background));
            var resizeHandle = header.Children.OfType<GridSplitter>().First();
            var resizeStart = resizeHandle.TranslatePoint(new Point(resizeHandle.Bounds.Width / 2, resizeHandle.Bounds.Height / 2), window)!.Value;
            double slotWidthBeforeDrag = header.ColumnDefinitions[0].ActualWidth;
            window.MouseDown(resizeStart, MouseButton.Left);
            window.MouseMove(resizeStart + new Vector(24, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(resizeStart + new Vector(24, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(slotWidthBeforeDrag, header.ColumnDefinitions[0].ActualWidth);
            var firstRow = (Grid)((Grid)((Grid)((ListBoxItem)list.Items[0]!).Content!).Children[0]).Children[0];
            for (int index = 0; index < header.ColumnDefinitions.Count; index++)
            {
                Assert.Equal(header.ColumnDefinitions[index].ActualWidth, firstRow.ColumnDefinitions[index].ActualWidth, 1);
            }
            Assert.DoesNotContain(firstRow.Children.OfType<Border>(), border => border.Name?.StartsWith("FavoriteColumnSeparator", StringComparison.Ordinal) == true);
            var separators = header.Children.OfType<Border>().Where(border => border.Name?.StartsWith("FavoriteHeaderColumnSeparator", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(5, separators.Length);
            Assert.All(separators, separator => Assert.NotEqual(Brushes.Transparent, separator.Background));
            Capture(window, "favorites-overflow");

            SetTag(options, "Fender", selected: true);
            favorites.ForEach(favorite => favorite.Tags = []);
            Invoke(window, "RefreshFavoritesList", true, null!, null!);
            Assert.Equal("Tags: All tags", Field<TextBlock>(window, "_favTagFilterLabel").Text);
            Assert.Single(options.Children);
            Assert.True(((CheckBox)options.Children[0]).IsChecked);
            Click(trigger);
            Assert.True(Field<Popup>(window, "_favTagFilterPopup").IsOpen);
            Assert.Equal("Filter favorites by tag", AutomationProperties.GetName(trigger));
            Assert.Equal("Match all selected tags", AutomationProperties.GetName(Find<ToggleButton>(window, "FavoriteTagMatchAll")));
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Field<Popup>(window, "_favTagFilterPopup").IsOpen);
            viewer.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewer.Offset.Y > 0);
            var last = (ListBoxItem)list.Items[^1]!;
            Assert.InRange(last.TranslatePoint(default, list)!.Value.Y, 0, list.Bounds.Height - last.Bounds.Height + 1);
        }
        finally { window.Close(); }
    }

    private static int[] Slots(ListBox list) => list.Items.OfType<ListBoxItem>().Select(item => ((Favorite)item.Tag!).Slot).ToArray();

    private static void SetTag(StackPanel options, string label, bool selected)
    {
        var option = options.Children.OfType<CheckBox>().Single(item => Equals(item.Content, label));
        option.IsChecked = selected;
        Click(option);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("FAVORITES_SNAPSHOT_DIR") is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        bitmap!.Save(Path.Combine(directory, name + ".png"));
    }
}
