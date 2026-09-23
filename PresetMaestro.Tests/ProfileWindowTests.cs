using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaFact]
    public void ProfileRescanImportExportButtonsUpdateRegistryAndKeepSelectionUntilChanged()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-profile-transfer-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory);
        var settings = store.LoadSettings();
        store.SaveFavorites("Default", [Clone(Sample)]);
        string archive = Path.Combine(directory, "transfer.zip");
        bool cancelPicker = false;
        var window = new MainWindow(settings, [], new FakeMidi(), profileStore: store,
            profileFilePicker: _ => Task.FromResult<string?>(cancelPicker ? null : archive));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Field<ComboBox>(window, "_channelCombo").SelectedIndex = 7;
            settings.PresetNameCache[4] = "Transfer preset";
            Click(Find<Button>(window, "ProfileExport"));
            Assert.True(File.Exists(archive));
            Assert.Contains("Exported", Find<TextBlock>(window, "ProfileStatus").Text);
            Find<TextBox>(window, "ProfileName").Text = "Imported";
            Click(Find<Button>(window, "ProfileImport"));
            Assert.Contains("Imported", settings.Profiles);
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Equal(7, store.LoadProfile("Imported").MidiChannel);
            Assert.Equal("Clean", store.LoadFavorites("Imported").Single().Name);
            cancelPicker = true;
            Click(Find<Button>(window, "ProfileImport"));
            Assert.Equal(2, settings.Profiles.Count);

            File.Copy(Path.Combine(directory, "Imported-settings.json"), Path.Combine(directory, "External-settings.json"));
            File.Copy(Path.Combine(directory, "Imported-favorites.json"), Path.Combine(directory, "External-favorites.json"));
            Assert.DoesNotContain("External", settings.Profiles);
            Click(Find<Button>(window, "ProfileRescan"));
            Assert.Contains("External", settings.Profiles);
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Contains("1 new profiles", Find<TextBlock>(window, "ProfileStatus").Text);
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "External";
            Assert.Equal("External", new ProfileStore(directory).LoadSettings().ActiveProfile);
            Assert.Equal("Transfer preset", settings.PresetNameCache[4]);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void CopyProfileDuplicatesCurrentDataAndEditsStayIndependent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-profile-copy-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory);
        var settings = store.LoadSettings();
        settings.AutoSendDelayMs = 140;
        store.SaveFavorites("Default", [Clone(Sample)]);
        var midi = new FakeMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Field<ComboBox>(window, "_channelCombo").SelectedIndex = 9;
            Field<ComboBox>(window, "_offsetCombo").SelectedIndex = 1;
            settings.MaxDisplayedPreset = 400; // Legacy value is readable but no longer authoritative.
            Field<NumericUpDown>(window, "_sceneCcSpinner").Value = 58;
            settings.PresetNameCache[100] = "Original preset";
            settings.SceneNameCaches["ports"] = new() { [100] = new() { Names = ["Original scene"] } };
            var originalFavorite = Field<List<Favorite>>(window, "_favorites").Single();

            Find<TextBox>(window, "ProfileName").Text = "Copy";
            Click(Find<Button>(window, "ProfileCopy"));
            Assert.Equal("Copy", settings.ActiveProfile);
            Assert.Equal("Copy", Find<ComboBox>(window, "ProfileSelector").SelectedItem);
            Assert.Equal("Copy", store.LoadSettings().ActiveProfile);
            Assert.Equal(9, settings.MidiChannel);
            Assert.Equal(1, settings.DisplayOffset);
            Assert.Equal(512, settings.MaxDisplayedPreset);
            Assert.Equal(58, settings.SceneCc);
            Assert.Equal(140, settings.AutoSendDelayMs);
            Assert.Equal("Original preset", settings.PresetNameCache[100]);
            Assert.Equal("Original scene", settings.SceneNameCaches["ports"][100].Names[0]);
            var copiedFavorite = Field<List<Favorite>>(window, "_favorites").Single();
            Assert.NotSame(originalFavorite, copiedFavorite);
            Assert.Equal(originalFavorite.Name, copiedFavorite.Name);
            Assert.Equal(originalFavorite.Preset, copiedFavorite.Preset);
            Assert.Equal(originalFavorite.Scene, copiedFavorite.Scene);
            Assert.Equal(originalFavorite.Tags, copiedFavorite.Tags);
            Assert.Equal(0, midi.TotalSendCount);

            copiedFavorite.Name = "Changed copy";
            copiedFavorite.Tags.Add("copy only");
            settings.PresetNameCache[100] = "Changed preset";
            settings.SceneNameCaches["ports"][100].Names[0] = "Changed scene";
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Default";
            Assert.Equal("Clean", Field<List<Favorite>>(window, "_favorites").Single().Name);
            Assert.DoesNotContain("copy only", originalFavorite.Tags);
            Assert.Equal("Original preset", settings.PresetNameCache[100]);
            Assert.Equal("Original scene", settings.SceneNameCaches["ports"][100].Names[0]);
            Assert.Equal("Changed copy", store.LoadFavorites("Copy").Single().Name);
            Assert.Equal("Changed preset", store.LoadProfile("Copy").PresetNameCache[100]);
            Assert.Equal("Changed scene", store.LoadProfile("Copy").SceneNameCaches["ports"][100].Names[0]);

            // Copy must never replace an existing profile or accept an unsafe filename.
            Find<TextBox>(window, "ProfileName").Text = "Copy";
            Click(Find<Button>(window, "ProfileCopy"));
            Assert.Contains("already exists", Find<TextBlock>(window, "ProfileStatus").Text);
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Equal("Changed copy", store.LoadFavorites("Copy").Single().Name);
            Find<TextBox>(window, "ProfileName").Text = "../invalid";
            Click(Find<Button>(window, "ProfileCopy"));
            Assert.Equal(2, store.ListProfiles().Count);
            Assert.Equal("Default", settings.ActiveProfile);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void ConfigProfileControlsSwitchMappingFavoritesAndCachesWhileKeepingMachineOptions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-profile-ui-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory);
        var settings = store.LoadSettings();
        settings.Theme = "Light";
        settings.AutoSendDelayMs = 140;
        settings.KeyboardEntryEnabled = false;
        settings.MidiEntryEnabled = false;
        settings.MidiChannel = 6;
        settings.PresetNameCache[1] = "Original";
        store.SaveSettings(settings);
        store.SaveFavorites("Default", [Clone(Sample)]);
        var midi = new FakeMidi();
        var window = new MainWindow(settings, [], midi, confirm: (_, _, _, _) => Task.FromResult(true), profileStore: store);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            var input = Field<ComboBox>(window, "_inputPortCombo");
            var output = Field<ComboBox>(window, "_outputPortCombo");
            input.Items.Add("Computer input");
            output.Items.Add("Computer output");
            input.SelectedItem = "Computer input";
            output.SelectedItem = "Computer output";
            string? screenshotDirectory = Environment.GetEnvironmentVariable("PRESETMAESTRO_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                window.Width = 1200;
                window.Height = 980;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame();
                bitmap?.Save(Path.Combine(screenshotDirectory, "profiles-config.png"));
            }
            Find<TextBox>(window, "ProfileName").Text = "Live";
            Click(Find<Button>(window, "ProfileCreate"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Live", settings.ActiveProfile);
            Assert.Empty(Field<List<Favorite>>(window, "_favorites"));
            Assert.Empty(settings.PresetNameCache);
            Assert.Equal(1, settings.MidiChannel);
            Assert.Equal(140, settings.AutoSendDelayMs);
            Assert.False(settings.KeyboardEntryEnabled);
            Assert.False(settings.MidiEntryEnabled);
            Assert.Same(input, Field<ComboBox>(window, "_inputPortCombo"));
            Assert.Same(output, Field<ComboBox>(window, "_outputPortCombo"));
            Assert.Equal("Computer input", input.SelectedItem);
            Assert.Equal("Computer output", output.SelectedItem);
            Assert.Equal("Computer input", store.LoadSettings().MidiInputPort);
            Assert.Equal("Computer output", store.LoadSettings().MidiOutputPort);
            Assert.Equal(0, midi.TotalSendCount);
            Field<ComboBox>(window, "_channelCombo").SelectedIndex = 9;
            Field<ComboBox>(window, "_offsetCombo").SelectedIndex = 1;
            settings.MaxDisplayedPreset = 400; // Legacy value is normalized when this profile is saved.
            Field<NumericUpDown>(window, "_sceneCcSpinner").Value = 58;
            settings.PresetNameCache[2] = "Live name";
            settings.SceneNameCaches["live"] = new() { [2] = new() { Names = ["Solo"] } };
            Field<List<Favorite>>(window, "_favorites").Add(new() { Id = 2, Slot = 1, Name = "Live favorite" });

            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Default";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Equal(6, settings.MidiChannel);
            Assert.Equal("Original", settings.PresetNameCache[1]);
            Assert.False(settings.PresetNameCache.ContainsKey(2));
            Assert.Equal("Clean", Field<List<Favorite>>(window, "_favorites").Single().Name);

            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Live";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(9, settings.MidiChannel);
            Assert.Equal(1, settings.DisplayOffset);
            Assert.Equal(512, settings.MaxDisplayedPreset);
            Assert.Equal(58, settings.SceneCc);
            Assert.Equal("Solo", settings.SceneNameCaches["live"][2].Names[0]);
            Assert.Equal("Live favorite", Field<List<Favorite>>(window, "_favorites").Single().Name);
            Find<TextBox>(window, "ProfileName").Text = "Tour";
            Click(Find<Button>(window, "ProfileRename"));
            Assert.Equal("Tour", store.LoadSettings().ActiveProfile);
            Assert.Equal(512, store.LoadProfile("Tour").MaxDisplayedPreset);
            Click(Find<Button>(window, "ProfileDelete"));
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Equal(["Default"], store.ListProfiles());
            Assert.Equal("Default", store.LoadSettings().ActiveProfile);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ProfileSwitchWaitsForPresetSyncAndDoesNotLeakItsResults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-profile-sync-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory);
        var settings = store.LoadSettings();
        store.Create("Other");
        var result = new TaskCompletionSource<PresetNameSync.Core.PresetNameResult>();
        var window = new MainWindow(settings, [], new FakeMidi { InputOpen = true, OutputOpen = true },
            (_, _, _) => result.Task, profileStore: store);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Task sync = window.SyncPresetNamesAsync();
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Other";
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Equal("Default", Find<ComboBox>(window, "ProfileSelector").SelectedItem);
            Assert.Contains("synchronization", Find<TextBlock>(window, "ProfileStatus").Text);
            result.SetCanceled();
            await sync;
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Other";
            Assert.Equal("Other", settings.ActiveProfile);
            Assert.Empty(store.LoadProfile("Other").PresetNameCache);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }
}
