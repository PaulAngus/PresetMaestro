using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("create")]
    [InlineData("clear")]
    [InlineData("remove")]
    [InlineData("reorder")]
    public void FavoriteStorageFailurePreservesValuesOrderAndEditorForRetry(string operation)
    {
        var first = Clone(Sample);
        var favorites = new List<Favorite> { first, new() { Id = 20, Slot = 2, Name = "Lead", Preset = 202, Scene = 3 } };
        string original = JsonSerializer.Serialize(favorites);
        bool fail = false;
        int saves = 0;
        var window = CreateWindow(favorites: favorites, saveFavorites: _ =>
        {
            if (fail) { throw new IOException("Disk is full."); }
            saves++;
        });
        try
        {
            ShowEditor(window, first);
            if (operation == "create")
            {
                Click(Field<Button>(window, "_favNewBtn"));
                Field<NumericUpDown>(window, "_favPresetSpinner").Value = 101;
                Field<NumericUpDown>(window, "_favSceneSpinner").Value = 2;
            }
            Find<TextBox>(window, "FavoriteName").Text = "Unsaved edit";
            fail = true;
            Change(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(original, JsonSerializer.Serialize(favorites));
            Assert.Same(first, favorites[0]);
            Assert.Equal(0, saves);
            Assert.True(Find<Border>(window, "FavoriteEditorCard").IsVisible);
            Assert.Equal("Unsaved edit", Find<TextBox>(window, "FavoriteName").Text);
            Assert.Equal("Couldn't save favorites", Find<TextBlock>(window, "SendFeedbackTitle").Text);

            fail = false;
            Change(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, saves);
            Assert.NotEqual(original, JsonSerializer.Serialize(favorites));
        }
        finally { fail = false; window.Close(); }

        void Change()
        {
            switch (operation)
            {
                case "edit" or "create": Click(Find<Button>(window, "FavoriteSave")); break;
                case "clear" or "remove":
                    Click(Find<Button>(window, "FavoriteDelete"));
                    Click(Find<Button>(window, operation == "clear" ? "FavoriteDeleteClearSlot" : "FavoriteDeleteShiftUp"));
                    break;
                case "reorder": Invoke(window, "ReorderFavorite", first, favorites[1], true); break;
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NameSyncSaveFailureRestoresCacheReleasesBusyStateAndAllowsRetry(bool readFails)
    {
        var settings = new AppSettings { PresetNameCache = new() { [0] = "Original" } };
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        bool failSave = false;
        int saves = 0;
        var window = new MainWindow(settings, [], midi, queryPresetNameAsync: (slot, _, _) =>
        {
            if (readFails && slot == 3) { throw new IOException("Read failed."); }
            return Task.FromResult(new PresetNameResult(slot, "Updated " + slot));
        }, saveSettings: _ =>
        {
            saves++;
            if (failSave) { throw new IOException("Disk is full."); }
        }, saveFavorites: _ => { });
        window.Show();
        try
        {
            failSave = true;
            await window.SyncPresetNamesAsync();
            Assert.Equal("Original", settings.PresetNameCache[0]);
            Assert.Single(settings.PresetNameCache);
            Assert.Null(FieldOrDefault<CancellationTokenSource>(window, "_presetNamesCts"));
            Assert.Contains("previous name cache was kept", SyncOutcomeDetail(window));
            Assert.Equal(1, saves);
            failSave = false;
            readFails = false;
            await window.SyncPresetNamesAsync();
            Assert.Equal(512, settings.PresetNameCache.Count);
            Assert.Equal("Updated 0", settings.PresetNameCache[0]);
            Assert.Null(FieldOrDefault<CancellationTokenSource>(window, "_presetNamesCts"));
            Assert.Equal(2, saves);
        }
        finally { failSave = false; window.Close(); }
    }

    [AvaloniaFact]
    public void FailedShutdownSaveKeepsWindowOpenAndAllowsRetry()
    {
        bool fail = false;
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        var window = new MainWindow(new(), [], midi, saveSettings: _ =>
        {
            if (fail) { throw new UnauthorizedAccessException("Profile is read-only."); }
        }, saveFavorites: _ => { });
        window.Show();
        try
        {
            fail = true;
            window.Close(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.True(midi.InputOpen && midi.OutputOpen);
            Assert.Equal("Couldn't save before closing", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            fail = false;
            window.Close();
            Assert.False(window.IsVisible);
            Assert.False(midi.InputOpen || midi.OutputOpen);
        }
        finally { fail = false; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    public void StorageFailureKeepsEditorFocusAndPagePosition(int width, string theme)
    {
        bool fail = false;
        var favorite = Clone(Sample);
        var window = CreateWindow(settings: new() { Theme = theme }, favorites: [favorite],
            saveFavorites: _ => { if (fail) { throw new IOException("Disk full"); } });
        window.Width = width; window.Height = 850;
        try
        {
            ShowEditor(window, favorite);
            var name = Find<TextBox>(window, "FavoriteName");
            name.Text = "Retain this edit"; name.Focus();
            var card = Find<Border>(window, "FavoriteEditorCard");
            var position = card.TranslatePoint(default, window);
            fail = true;
            Click(Find<Button>(window, "FavoriteSave")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(position, card.TranslatePoint(default, window));
            Assert.Same(name, window.FocusManager!.GetFocusedElement());
            Assert.True(Find<Border>(window, "SendFeedback").IsVisible);
            CaptureSendConfirmation(window, $"storage-failure-{width}-{theme}");
        }
        finally { fail = false; window.Close(); }
    }
}
