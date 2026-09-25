using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1200, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1200, "Dark")]
    public void CompactDesktopKeepsControlsContainedAndFavoriteRowsUnchanged(int width, string theme)
    {
        var midi = new DeviceMidi();
        midi.InputPorts.Add("SMC-PAD Pocket (Bluetooth MIDI IN)");
        var settings = new AppSettings
        {
            Theme = theme,
            ActiveProfile = "A very long rehearsal profile name with extra information",
            MidiInputPort = "FM9",
            MidiOutputPort = "FM9",
        };
        var favorites = Enumerable.Range(1, 19).Select(slot => new Favorite
        {
            Id = slot,
            Slot = slot,
            Name = $"Preset {slot}",
            Preset = slot,
            Scene = 1,
            Tags = ["Crunch", "Low Gain"],
        }).ToList();
        var window = new MainWindow(settings, favorites, midi, saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Width = width;
            window.Height = 640;
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Find<TextBlock>(window, "HeaderConnectionStatus").Text = "FM9 · A long connected device name";
            Dispatcher.UIThread.RunJobs();

            var title = Find<StackPanel>(window, "AppIdentity");
            var firstTab = Find<Button>(window, "NavPresetSender");
            var lastTab = Find<Button>(window, "NavConfig");
            var status = Find<TextBlock>(window, "HeaderConnectionStatus");
            Assert.True(Origin(title).X + title.Bounds.Width <= Origin(firstTab).X);
            Assert.True(Origin(lastTab).X + lastTab.Bounds.Width < Origin(status).X);
            Assert.True(Origin(Find<TextBlock>(window, "HeaderActiveProfile")).X < width);

            var connection = Find<Border>(window, "MidiConnectionCard");
            foreach (var control in connection.GetVisualDescendants().OfType<Control>().Where(c => c is Button or ComboBox or CheckBox))
            {
                AssertContainedHorizontally(control, connection);
            }
            foreach (string name in new[] { "ProfileSelector", "ProfileName", "ProfileCreate", "ProfileExport", "DisplayOffset", "OpenMidiMapping" })
            {
                AssertContainedHorizontally(Find<Control>(window, name), window);
            }
            var create = Find<Button>(window, "ProfileCreate");
            var rename = Find<Button>(window, "ProfileRename");
            var delete = Find<Button>(window, "ProfileDelete");
            Assert.Equal(Origin(create).Y, Origin(rename).Y);
            Assert.True(Origin(delete).Y >= Origin(create).Y + create.Bounds.Height + 7);
            Capture(window, $"desktop-config-{width}-{theme}");

            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.OfType<ListBoxItem>().ToArray();
            Assert.Equal(2, Field<FavoriteTablesPanel>(window, "_favTablesPanel").Columns);
            Assert.All(rows, row => Assert.Equal(26, row.Bounds.Height));
            var originalWidths = rows.Select(row => row.Bounds.Width).ToArray();
            Capture(window, $"desktop-favorites-{width}-{theme}");

            double originalSearchHeight = Find<Border>(window, "FavoriteSearchEntry").Bounds.Height;
            foreach (string term in new[] { "Crunch", "Low Gain", "Long search phrase that wraps in a narrow search field", "Another long search phrase" })
            {
                CommitSearch(window, term);
            }
            Assert.True(Find<Border>(window, "FavoriteSearchEntry").Bounds.Height > originalSearchHeight);
            var search = Find<Border>(window, "FavoriteSearchEntry");
            var count = Find<TextBlock>(window, "FavoriteResultCount");
            Assert.True(Origin(count).Y >= Origin(search).Y + search.Bounds.Height);
            AssertContainedHorizontally(Find<Button>(window, "FavoriteClearSearch"), window);
            Click(Find<Button>(window, "FavoriteClearSearch"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalWidths, list.Items.OfType<ListBoxItem>().Select(row => row.Bounds.Width).ToArray());
            Assert.All(list.Items.OfType<ListBoxItem>(), row => Assert.Equal(26, row.Bounds.Height));
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }

        Point Origin(Control control) => control.TranslatePoint(default, window)!.Value;
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void CompactFavoriteEditorFitsShortWindowsAndScrollsWrappedTags(string theme)
    {
        var favorite = Clone(Sample);
        var window = CreateWindow(favorites: [favorite], settings: new AppSettings { Theme = theme });
        try
        {
            window.Width = 1320;
            window.Height = 640;
            ShowEditor(window, favorite);
            var card = Find<Border>(window, "FavoriteEditorCard");
            var scroll = Find<ScrollViewer>(window, "FavoriteEditorScroll");
            var save = Find<Button>(window, "FavoriteSave");
            foreach (string name in new[] { "FavoriteNew", "FavoritePresetSync", "FavoriteName", "FavoritePresetPicker", "FavoriteScenePicker", "FavoriteUseCurrent", "FavoriteSave", "FavoriteCancel", "FavoriteDelete" })
            {
                var control = Find<Control>(window, name);
                AssertContainedHorizontally(control, card);
                Assert.InRange(control.Bounds.Height, 32, 34);
                var origin = control.TranslatePoint(default, card)!.Value;
                Assert.True(origin.Y >= 0 && origin.Y + control.Bounds.Height <= card.Bounds.Height);
            }
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height);
            Assert.All(Field<ListBox>(window, "_favListBox").Items.OfType<ListBoxItem>(), row => Assert.Equal(26, row.Bounds.Height));
            Capture(window, $"desktop-favorite-edit-{theme}");

            Click(Find<Button>(window, "FavoriteNew"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Button>(window, "FavoriteDelete").IsVisible);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height);
            Capture(window, $"desktop-favorite-new-{theme}");

            favorite.Name = "A long favorite name that wraps inside the compact editor pane";
            favorite.Tags = Enumerable.Range(1, 24).Select(index => $"Rehearsal tag {index}").ToList();
            window.ShowFavoriteEditor(favorite, isNew: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Field<Border>(window, "_favTagsEditor").Bounds.Height > 32);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            save.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            var saveOrigin = save.TranslatePoint(default, scroll)!.Value;
            Assert.True(saveOrigin.Y >= 0 && saveOrigin.Y + save.Bounds.Height <= scroll.Viewport.Height + 1);
            AssertContainedHorizontally(save, card);
            Click(Find<Button>(window, "FavoriteCancel"));
            Assert.False(card.IsVisible);
        }
        finally { window.Close(); }
    }

    private static void AssertContainedHorizontally(Control child, Control parent)
    {
        var origin = child.TranslatePoint(default, parent)!.Value;
        Assert.True(origin.X >= -1 && origin.X + child.Bounds.Width <= parent.Bounds.Width + 1,
            $"{child.Name ?? child.GetType().Name} extends beyond {parent.Name ?? parent.GetType().Name}: {origin.X} + {child.Bounds.Width} > {parent.Bounds.Width}");
    }
}
