using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaFact]
    public void TableKeyboardAndCrossTableDragUseOneSelectionAndUnderlyingOrder()
    {
        var favorites = Enumerable.Range(1, 19).Select(i => new Favorite { Id = i, Slot = i, Name = $"Favorite {i}", Preset = i, Scene = 1 }).ToList();
        int saves = 0; var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi, favorites, saveFavorites: _ => saves++);
        try
        {
            window.Width = 1500; window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.Cast<ListBoxItem>().ToArray();
            list.SelectedItem = rows[6]; rows[6].Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(rows[12], list.SelectedItem);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(rows[18], list.SelectedItem);
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(rows[17], list.SelectedItem);
            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(rows[0], list.SelectedItem);
            window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(rows[18], list.SelectedItem);
            var from = rows[0].TranslatePoint(new Point(80, 12), window)!.Value;
            var to = rows[8].TranslatePoint(new Point(80, 22), window)!.Value;
            window.MouseDown(from, MouseButton.Left); window.MouseMove(to, RawInputModifiers.LeftMouseButton); window.MouseUp(to, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 2, 3, 4, 5, 6, 7, 8, 9, 1, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 }, favorites.Select(f => f.Id));
            Assert.Equal(Enumerable.Range(1, 19), favorites.Select(f => f.Slot));
            Assert.Equal(1, saves);
            CommitSearch(window, "Favorite");
            Invoke(window, "ReorderFavorite", favorites[0], favorites[5], true);
            Assert.Equal(1, saves);
            Click(Find<Button>(window, "FavoriteClearSearch"));
            Click(Find<Button>(window, "FavoriteTable2Heading1"));
            Invoke(window, "ReorderFavorite", favorites[0], favorites[5], true);
            Assert.Equal(1, saves); Assert.Equal(0, midi.TotalSendCount);
            var clicked = list.Items.Cast<ListBoxItem>().Last();
            var clickedFavorite = (Favorite)clicked.Tag!;
            var menu = clicked.ContextMenu!;
            ClickMenu((MenuItem)menu.Items[2]!);
            Assert.True(clickedFavorite.IsEmpty);
            Assert.Equal(2, saves);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SmallListsRetainEmptyTablesAndPageKeysStayWithinTheirTable()
    {
        var favorites = Enumerable.Range(1, 120).Select(i => new Favorite { Id = i, Slot = i, Name = $"Favorite {i}", Scene = 1 }).ToList();
        var window = CreateWindow(favorites: favorites);
        try
        {
            window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            list.SelectedIndex = 0; ((ListBoxItem)list.Items[0]!).Focus();
            window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.InRange(list.SelectedIndex, 1, 59);
            window.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, list.SelectedIndex);
            favorites.RemoveRange(1, 119);
            Invoke(window, "RefreshFavoritesList", true, null!, null!); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, Field<FavoriteTablesPanel>(window, "_favTablesPanel").Columns);
            Assert.Single(list.Items);
            favorites.Clear(); Invoke(window, "RefreshFavoritesList", true, null!, null!); Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.Items); Assert.Null(list.SelectedItem);
            Assert.Equal(2, Field<FavoriteTablesPanel>(window, "_favTablesPanel").Columns);
            Assert.True(Find<TextBlock>(window, "FavoriteEmptyResults").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ActualProfileSwitchResetsChipsSelectionAndTagSuggestions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "favorites-chip-profiles-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory); var settings = store.LoadSettings(); settings.Theme = "Light";
        store.SaveFavorites("Default", [new() { Id = 1, Slot = 1, Name = "First", Tags = ["Old Tag"] }]);
        store.Create("Other", favorites: [new() { Id = 2, Slot = 1, Name = "Second", Tags = ["New Tag"] }]);
        var window = new MainWindow(settings, [], new FakeMidi(), profileStore: store);
        try
        {
            window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            Field<ListBox>(window, "_favListBox").SelectedIndex = 0;
            CommitSearch(window, "Old Tag"); CommitSearch(window, "First");
            Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Other"; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            Assert.Empty(Field<WrapPanel>(window, "_favSearchPanel").Children.OfType<Border>());
            Assert.Equal(new[] { "New Tag" }, Field<List<string>>(window, "_favAvailableTags"));
            Assert.Null(Field<ListBox>(window, "_favListBox").SelectedItem);
            Assert.Equal("Second", ((Favorite)((ListBoxItem)Field<ListBox>(window, "_favListBox").Items[0]!).Tag!).Name);
            Assert.Equal(new[] { "Old Tag" }, store.LoadFavorites("Default")[0].Tags);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static void ClickMenu(MenuItem menu) => menu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaTheory]
    [InlineData(1000, "Light", false)]
    [InlineData(1100, "Light", true)]
    [InlineData(1500, "Light", true)]
    [InlineData(1950, "Light", true)]
    [InlineData(1000, "Dark", true)]
    [InlineData(1320, "Dark", true)]
    public void RenderedTablesKeepDetailsInsideBoundsAndEditorUsable(int width, string theme, bool allDetails)
    {
        string[] names = ["Plexi Low Crunch", "Bassman Crunch", "Clean Bassman", "AC-15 Boosted", "Plexi Mid Crunch", "Satriani JS410 Orange Channel", "Tweed with TS 808", "CarolAnn OD2", "FAS Hi Gain", "Metal Chugs", "Aaron Marshall", "Diezel Hard", "#34", "Mesa IIC+", "Morgan AC-20 Treble", "HBE V1", "Hiwatt DR-103", "Smallbox Friedman V30"];
        var favorites = names.Select((n, i) => new Favorite { Id = i + 1, Slot = i + 1, Name = n, Tags = i % 3 == 0 ? ["Low Gain", "Strat", "Long descriptive tag"] : i % 3 == 1 ? ["Fender"] : [], Preset = i + 1, Scene = i % 4 + 1 }).ToList();
        var settings = new AppSettings
        {
            Theme = theme,
            PresetNameCache = Enumerable.Range(1, 18).ToDictionary(i => i, i => i == 6 ? "Very long cached amplifier name that must remain fully readable within its own bordered table and wrap over multiple lines even at a generously wide two-table viewport without bleeding into adjacent favorite names or tags" : $"Cached amp {i}"),
            SceneNameCaches = new()
            {
                ["previous ports"] = Enumerable.Range(1, 18).ToDictionary(i => i, i => new SceneCacheEntry
                {
                    Names = Enumerable.Range(1, 4).Select(scene => $"Scene name {scene}").ToArray(),
                    RetrievedAt = DateTimeOffset.Parse("2026-09-20T12:00:00Z"),
                }),
            },
        };
        var window = CreateWindow(favorites: favorites, settings: settings);
        try
        {
            window.Width = width; window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox"); list.SelectedIndex = 5;
            SetField(window, "_detectedPresetSlot", 18); SetField(window, "_detectedScene", 2);
            if (allDetails) { Click(Find<Button>(window, "FavoriteDetailsToggle")); }
            Invoke(window, "UpdateFavoriteRowStates", (object)null!); Dispatcher.UIThread.RunJobs();
            var panel = Field<FavoriteTablesPanel>(window, "_favTablesPanel");
            Assert.Equal(width >= 1900 ? 4 : width >= 1450 ? 3 : 2, panel.Columns);
            foreach (var row in list.Items.Cast<ListBoxItem>())
            {
                Assert.InRange(row.Bounds.Width, 440, 670);
                var text = DetailText(row);
                if (text.IsEffectivelyVisible)
                {
                    var origin = text.TranslatePoint(default, row)!.Value;
                    Assert.InRange(origin.X, 0, row.Bounds.Width);
                    Assert.True(origin.X + text.Bounds.Width <= row.Bounds.Width);
                    Assert.Equal(new Thickness(6, 0, 12, 0), text.Margin);
                }
                Assert.Equal(Brushes.Transparent, DetailStrip(row).Background);
                var title = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "FavoriteTitle");
                var tags = row.GetVisualDescendants().OfType<WrapPanel>().Single(t => t.Name == "FavoriteInlineTags");
                var titleOrigin = title.TranslatePoint(default, row)!.Value;
                var tagOrigin = tags.TranslatePoint(default, row)!.Value;
                if (((Favorite)row.Tag!).Tags.Count > 0)
                {
                    Assert.InRange(tagOrigin.X - (titleOrigin.X + title.Bounds.Width), 7, 9);
                    Assert.True(tags.Bounds.Width > 90);
                }
                Assert.True(tagOrigin.X + tags.Bounds.Width <= row.Bounds.Width - 95);
            }
            Assert.True(DetailStrip((ListBoxItem)list.Items[5]!).Bounds.Height > 18);
            Assert.EndsWith(" | Scene name 2", DetailText((ListBoxItem)list.Items[5]!).Text);
            Capture(window, $"favorites-{width}-{theme}-{allDetails}");
            if (width == 1320)
            {
                window.ShowFavoriteEditor(favorites[0], false); Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, panel.Columns);
                Assert.InRange(Find<Button>(window, "FavoriteSave").TranslatePoint(default, window)!.Value.X, 0, window.Width - 50);
                Capture(window, "favorites-editor-dark");

            }
            // Theme reconstruction must recreate repeated headings, retaining search/selection.
            Invoke(window, "SetTheme", theme == "Light" ? "Dark" : "Light"); Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(Field<Grid>(window, "_favHeaderGroup").Children);
        }
        finally { window.Close(); }
    }
}
