using Avalonia;
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
    public void CollectionTagAndSearchFiltersCombineWithoutSendingOrChangingSlots()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 1, Slot = 1, Name = "Bright Lead", Category = "Clean", Tags = ["Fender", "warm"], Preset = 1, Scene = 1 },
            new() { Id = 2, Slot = 2, Name = "Bright Rhythm", Category = "Clean", Tags = ["fender"], Preset = 2, Scene = 2 },
            new() { Id = 3, Slot = 3, Name = "Bright Lead", Category = "Crunch", Tags = ["Fender"], Preset = 3, Scene = 3 },
            new() { Id = 4, Slot = 4, Name = "Bright Lead", Category = "Clean", Tags = ["Fenderish"], Preset = 4, Scene = 4 },
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
            var tags = Find<ComboBox>(window, "FavoriteTagFilter");
            Assert.Equal(4, tags.ItemCount); // All tags, Fender, Fenderish, warm

            tags.SelectedIndex = 1; // Fender; must not match Fenderish.
            collections.SelectedIndex = 1; // Clean
            search.Text = "LEAD";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { 1 }, Slots(list));
            search.Text = "none";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.Items);
            Assert.Null(list.SelectedItem);
            Assert.True(Find<TextBlock>(window, "FavoriteEmptyResults").IsVisible);
            Click(Find<Button>(window, "FavoriteSendCommand"));

            search.Text = "lead";
            collections.SelectedIndex = 0;
            tags.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 1, 3, 4 }, Slots(list));
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(favorites));
            Assert.Equal(0, saves);
            Assert.Equal(0, midi.TotalSendCount);
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
            var tags = Find<ComboBox>(window, "FavoriteTagFilter");
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

            tags.SelectedIndex = 1;
            favorites.ForEach(favorite => favorite.Tags = []);
            Invoke(window, "RefreshFavoritesList", true, null!, null!);
            Assert.Equal(0, tags.SelectedIndex);
            Assert.Equal(1, tags.ItemCount);
            viewer.ScrollToEnd();
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewer.Offset.Y > 0);
            var last = (ListBoxItem)list.Items[^1]!;
            Assert.InRange(last.TranslatePoint(default, list)!.Value.Y, 0, list.Bounds.Height - last.Bounds.Height + 1);
        }
        finally { window.Close(); }
    }

    private static int[] Slots(ListBox list) => list.Items.OfType<ListBoxItem>().Select(item => ((Favorite)item.Tag!).Slot).ToArray();

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
