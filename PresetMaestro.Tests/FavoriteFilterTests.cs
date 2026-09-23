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
using System.Text.Json;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    private static int[] Slots(ListBox list) => list.Items.OfType<ListBoxItem>().Select(item => ((Favorite)item.Tag!).Slot).ToArray();

    private static void CommitSearch(MainWindow window, string value)
    {
        var input = Find<TextBox>(window, "FavoriteSearchInput");
        input.Focus(); input.Text = value;
        Dispatcher.UIThread.RunJobs();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ChipsCombineExactTagsAllAnyAndTextAndWithoutSavingOrSending()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 1, Slot = 2, Name = "Bright Lead", Tags = ["Low Gain", "warm"], Preset = 1, Scene = 1 },
            new() { Id = 2, Slot = 6, Name = "Bright Rhythm", Tags = ["low gain"], Preset = 2, Scene = 2 },
            new() { Id = 3, Slot = 18, Name = "Bright Lead", Tags = ["Low Gain", "Marshall"], Preset = 3, Scene = 3 },
            new() { Id = 4, Slot = 22, Name = "Bright Lead", Tags = ["Low Gainish"], Preset = 4, Scene = 4 },
            new() { Id = 5, Slot = 25, Name = "Dark Lead", Tags = ["Marshall"], Preset = 5, Scene = 1 },
        };
        string original = JsonSerializer.Serialize(favorites);
        int saves = 0;
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi, favorites, saveFavorites: _ => saves++);
        try
        {
            window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            list.SelectedIndex = 1;
            CommitSearch(window, "LOW GAIN");
            Assert.Equal(new[] { 2, 6, 18 }, Slots(list));
            CommitSearch(window, "Marshall");
            Assert.Equal(new[] { 18 }, Slots(list));
            Assert.Null(list.SelectedItem);
            Click(Find<Button>(window, "FavoriteSendCommand"));
            Click(Find<ToggleButton>(window, "FavoriteTagMatchAny"));
            Assert.Equal(new[] { 2, 6, 18, 25 }, Slots(list));
            Assert.Equal(6, ((Favorite)((ListBoxItem)list.SelectedItem!).Tag!).Slot);
            CommitSearch(window, "Bright"); CommitSearch(window, "lead");
            Assert.Equal(new[] { 2, 18 }, Slots(list));
            CommitSearch(window, "LEAD");
            Assert.Equal(4, Field<WrapPanel>(window, "_favSearchPanel").Children.OfType<Border>().Count());
            CommitSearch(window, "no results");
            Assert.Empty(list.Items);
            Assert.True(Find<TextBlock>(window, "FavoriteEmptyResults").IsVisible);
            window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 2, 18 }, Slots(list));
            Click(Find<Button>(window, "FavoriteSearchRemove2"));
            Assert.Equal(new[] { 2, 18, 25 }, Slots(list));
            Click(Find<Button>(window, "FavoriteClearSearch"));
            Assert.Equal(new[] { 2, 6, 18, 22, 25 }, Slots(list));
            Assert.Equal(original, JsonSerializer.Serialize(favorites));
            Assert.Equal(0, saves); Assert.Equal(0, midi.TotalSendCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SuggestionsExplicitTextPreviewKeyboardProtectionAndProfileReset()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi, [new() { Id = 1, Slot = 2, Name = "Low Gain lead", Tags = ["Low Gain"] }, new() { Id = 2, Slot = 6, Name = "Low Gain rhythm", Tags = ["Other"] }],
            settings: new AppSettings { Theme = "Light", KeyboardEntryEnabled = true, PresetNameCache = new() { [0] = "Cached amp" } });
        try
        {
            window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            var input = Find<TextBox>(window, "FavoriteSearchInput");
            var list = Field<ListBox>(window, "_favListBox");
            SetField(window, "_enteredDigits", "2");
            input.Focus(); input.Text = "low gain"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 2 }, Slots(list));
            Assert.True(Field<Popup>(window, "_favSearchPopup").IsOpen);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 2, 6 }, Slots(list));
            Assert.Contains(Field<WrapPanel>(window, "_favSearchPanel").GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Text: low gain");
            CommitSearch(window, "2");
            Assert.Equal(new[] { 2 }, Slots(list));
            Assert.Equal("2", Field<string>(window, "_enteredDigits"));
            Assert.Equal(0, midi.TotalSendCount);
            Click(Find<Button>(window, "FavoriteClearSearch"));
            CommitSearch(window, "cached amp");
            Assert.Equal(new[] { 2, 6 }, Slots(list));
            Invoke(window, "ResetFavoriteSearch");
            Invoke(window, "RefreshFavoritesList", true, null!, null!);
            Assert.Empty(input.Text!);
            Assert.True(Find<ToggleButton>(window, "FavoriteTagMatchAll").IsChecked);
            input.Text = "Other"; Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(Field<Popup>(window, "_favSearchPopup").IsOpen);
            CommitSearch(window, "Other");
            Field<List<Favorite>>(window, "_favorites")[1].Tags.Clear();
            Invoke(window, "RefreshFavoritesList", true, null!, null!);
            Assert.Equal(new[] { 2, 6 }, Slots(list));
            Assert.Empty(Field<WrapPanel>(window, "_favSearchPanel").Children.OfType<Border>());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TablesShareScrollHeadersWidthsAndKeepSelectionThroughResizeSortAndRefresh()
    {
        var favorites = Enumerable.Range(1, 60).Select(i => new Favorite { Id = i, Slot = i, Name = $"Favorite {i:00}", Tags = ["Fender"], Preset = i == 1 ? 1024 : i, Scene = 3 }).ToList();
        var window = CreateWindow(favorites: favorites);
        try
        {
            window.Show(); window.Width = 1100; Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var panel = Field<FavoriteTablesPanel>(window, "_favTablesPanel");
            var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var header = Find<Grid>(window, "FavoriteTableHeader");
            Assert.Equal(2, panel.Columns);
            Assert.True(viewer.Extent.Height > viewer.Viewport.Height);
            Assert.InRange(list.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical).Bounds.Width, 5, 6);
            Assert.Equal(4, header.ColumnDefinitions.Count);
            Assert.Equal(3, header.Children.OfType<GridSplitter>().Count());
            Assert.Equal(new[] { "Slot", "Name", "Preset", "Scene" }, header.Children.OfType<Button>().Select(b => b.Content));
            var rows = list.Items.OfType<ListBoxItem>().ToArray();
            Assert.Equal(rows[0].Bounds.Y, rows[30].Bounds.Y);
            Assert.True(rows[30].Bounds.X > rows[0].Bounds.X);
            list.SelectedItem = rows[37];
            window.Width = 1500; Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, panel.Columns); Assert.Same(rows[37], list.SelectedItem);
            window.Width = 1950; Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, panel.Columns); Assert.Same(rows[37], list.SelectedItem);
            Assert.Equal(rows, list.Items.OfType<ListBoxItem>());
            window.Width = 1100; Dispatcher.UIThread.RunJobs();
            var sendOrigin = Find<Button>(window, "FavoriteSendCommand").TranslatePoint(default, window)!.Value;
            Assert.InRange(sendOrigin.X, 0, window.Width - 58);
            Assert.InRange(Find<Button>(window, "NavFavorites").TranslatePoint(default, window)!.Value.X, 350, 650);
            header = Find<Grid>(window, "FavoriteTableHeader");
            var handle = header.Children.OfType<GridSplitter>().First();
            var point = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            double before = header.ColumnDefinitions[0].ActualWidth;
            window.MouseDown(point, MouseButton.Left); window.MouseMove(point + new Vector(18, 0), RawInputModifiers.LeftMouseButton); window.MouseUp(point + new Vector(18, 0), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, header.ColumnDefinitions[0].ActualWidth);
            foreach (var row in rows)
            {
                var fields = (Grid)((Grid)((Grid)row.Content!).Children[0]).Children[0];
                Assert.Empty(fields.Children.OfType<Border>());
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal(header.ColumnDefinitions[i].ActualWidth, fields.ColumnDefinitions[i].ActualWidth, 1);
                }
            }
            var otherHeader = Find<Grid>(window, "FavoriteTableHeader1");
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(header.ColumnDefinitions[i].Width, otherHeader.ColumnDefinitions[i].Width);
            }

            Click(Find<Button>(window, "FavoriteTable1Heading1")); Click(Find<Button>(window, "FavoriteTable1Heading1"));
            Assert.Equal(Enumerable.Range(1, 60).Reverse(), Slots(list));
            Assert.Equal(38, ((Favorite)((ListBoxItem)list.SelectedItem!).Tag!).Id);
            Invoke(window, "RefreshFavoritesList", true, null!, null!);
            Assert.Equal(38, ((Favorite)((ListBoxItem)list.SelectedItem!).Tag!).Id);
            Assert.Equal(Enumerable.Range(1, 60), favorites.Select(f => f.Slot));
            Click(Find<Button>(window, "FavoriteTable1Heading2"));
            Assert.Equal(Enumerable.Range(2, 59).Append(1), Slots(list));
            Click(Find<Button>(window, "FavoriteTable1Heading3"));
            Assert.Equal(Enumerable.Range(1, 60), Slots(list));
            viewer.ScrollToEnd(); Dispatcher.UIThread.RunJobs(); Assert.True(viewer.Offset.Y > 0);
            Capture(window, "favorites-scroll");
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("FAVORITES_SNAPSHOT_DIR") is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(directory, name + ".png"));
    }
}
