using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    private sealed class ManagedProfileFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "PresetMaestro-management-" + Guid.NewGuid().ToString("N"));
        public ProfileStore Store { get; }
        public AppSettings Settings { get; }
        public FakeMidi Midi { get; } = new();
        public MainWindow Window { get; }
        public string? NameResult { get; set; } = "New profile";
        public bool ConfirmResult { get; set; } = true;
        public string? ConfirmationMessage { get; private set; }
        public string? ConfirmationAction { get; private set; }
        public string ArchivePath => Path.Combine(DirectoryPath, "export.zip");

        public ManagedProfileFixture(string theme = "Light", bool realDialogs = false)
        {
            Store = new ProfileStore(DirectoryPath);
            Settings = Store.LoadSettings();
            Settings.Theme = theme;
            Settings.AutoSendDelayMs = 140;
            Store.SaveSettings(Settings);
            Store.SaveFavorites("Default", [Clone(Sample)]);
            Store.Create("Session", new ProfileSettings
            {
                DeviceModel = DeviceModel.FM3,
                DeviceName = "Rehearsal rig",
                MidiChannel = 6,
                DisplayOffset = 1,
                SceneCc = 58,
                PresetNameCache = new() { [4] = "Session preset", [5] = "" },
                SceneNameCaches = new()
                {
                    ["ports"] = new()
                    {
                        [4] = new() { Names = ["Clean", "", "Solo"] },
                        [5] = new() { Names = ["Rhythm", " "] },
                    },
                },
            }, [new Favorite { Id = 20, Slot = 1, Name = "Session favorite", Preset = 5, Scene = 2, Tags = ["session"] }, new Favorite { Id = 21, Slot = 2, Scene = 0 }]);
            Store.Create("Spare");
            Window = new MainWindow(Settings, [], Midi, profileStore: Store,
                confirm: realDialogs ? null : (_, message, action, _) =>
                {
                    ConfirmationMessage = message; ConfirmationAction = action;
                    return Task.FromResult(ConfirmResult);
                },
                profileFilePicker: _ => Task.FromResult<string?>(ArchivePath),
                profileNamePrompt: realDialogs ? null : (_, _) => Task.FromResult<string?>(NameResult));
        }

        public void Dispose()
        {
            foreach (Window dialog in Window.OwnedWindows.ToArray()) { dialog.Close(); }
            Window.Close();
            Directory.Delete(DirectoryPath, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(null, "Not recorded")]
    [InlineData("", "Unnamed device")]
    [InlineData("Studio FM9", "Studio FM9")]
    public void ManagementShowsSavedDeviceNameWhileDisconnected(string? name, string expected)
    {
        using var fixture = new ManagedProfileFixture();
        fixture.Settings.DeviceName = name;
        OpenProfileManagement(fixture.Window);
        Assert.Equal(expected, ManagedFact(fixture.Window, "Device name"));
        SelectManagedProfile(fixture.Window, "Session");
        Assert.Equal("Rehearsal rig", ManagedFact(fixture.Window, "Device name"));
        SelectManagedProfile(fixture.Window, "Default");
        Assert.Equal(expected, ManagedFact(fixture.Window, "Device name"));
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    private static void OpenProfileManagement(MainWindow window)
    {
        window.Show(); Dispatcher.UIThread.RunJobs();
        Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
        Click(Find<Button>(window, "ManageProfiles")); Dispatcher.UIThread.RunJobs();
    }

    private static void SelectManagedProfile(MainWindow window, string name)
    {
        var list = Find<ListBox>(window, "ManagedProfiles");
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(item => (string)item.Tag! == name);
        Dispatcher.UIThread.RunJobs();
    }

    private static string? ManagedFact(MainWindow window, string label)
    {
        var facts = Find<StackPanel>(window, "ProfileFacts");
        var row = facts.Children.OfType<Border>().Select(border => (Grid)border.Child!)
            .Single(grid => ((TextBlock)grid.Children[0]).Text == label);
        return ((TextBlock)row.Children[1]).Text;
    }

    [AvaloniaFact]
    public void ManagementSelectionAndKeyboardInspectWithoutActivatingWritingOrSending()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        window.Show();
        Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
        var favoriteList = Field<ListBox>(window, "_favListBox");
        favoriteList.SelectedIndex = 0;
        object? favoriteSelection = favoriteList.SelectedItem;
        var activeFavorites = Field<List<Favorite>>(window, "_favorites");
        OpenProfileManagement(window);
        Assert.Equal("Default", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.False(Find<Button>(window, "ProfileUse").IsEnabled);
        Field<AppSettings>(window, "_settings").MidiChannel = 10;
        fixture.Settings.PresetNameCache[12] = "Unsaved live preset";
        SetField(window, "_enteredDigits", "123");
        var originalFiles = Directory.GetFiles(fixture.DirectoryPath, "*.json").ToDictionary(path => path, File.ReadAllBytes);
        var list = Find<ListBox>(window, "ManagedProfiles");
        ((ListBoxItem)list.SelectedItem!).Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Profile: Default", Find<TextBlock>(window, "HeaderActiveProfile").Text);
        Assert.Same(activeFavorites, Field<List<Favorite>>(window, "_favorites"));
        Assert.Same(favoriteSelection, favoriteList.SelectedItem);
        Assert.Equal("Unsaved live preset", fixture.Settings.PresetNameCache[12]);
        Assert.Equal(10, Field<AppSettings>(window, "_settings").MidiChannel);
        Assert.Equal("FM3", ManagedFact(window, "Device"));
        Assert.Equal("Rehearsal rig", ManagedFact(window, "Device name"));
        Assert.Equal("1", ManagedFact(window, "Favorites"));
        Assert.Equal("1", ManagedFact(window, "Cached preset names"));
        Assert.Equal("3", ManagedFact(window, "Cached scene names"));
        Assert.Contains("MIDI 6", ManagedFact(window, "Preset mapping"));
        foreach (var (path, bytes) in originalFiles) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, fixture.Midi.TotalSendCount);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Click(Find<Button>(window, "ProfileUse")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session", fixture.Settings.ActiveProfile);
        Assert.Equal("Session favorite", activeFavorites.Single(favorite => !favorite.IsEmpty).Name);
        Assert.Equal(10, fixture.Store.LoadProfile("Default").MidiChannel);
        Assert.Equal("Unsaved live preset", fixture.Store.LoadProfile("Default").PresetNameCache[12]);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public void InactiveRenameExportAndDeletePreserveLiveActiveStateAndPersistTargets()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        OpenProfileManagement(window);
        Field<AppSettings>(window, "_settings").MidiChannel = 10;
        var liveFavorite = Field<List<Favorite>>(window, "_favorites").Single();
        liveFavorite.Name = "Unsaved active favorite";
        fixture.Settings.PresetNameCache[12] = "Live cache";
        byte[] activeBytes = File.ReadAllBytes(Path.Combine(fixture.DirectoryPath, "Default-settings.json"));
        SelectManagedProfile(window, "Session");
        fixture.NameResult = "SESSION";
        Click(Find<Button>(window, "ProfileRename")); Dispatcher.UIThread.RunJobs();
        Assert.Contains("SESSION", fixture.Store.ListProfiles());
        Assert.Equal("SESSION", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Default", new ProfileStore(fixture.DirectoryPath).LoadSettings().ActiveProfile);
        Click(Find<Button>(window, "ProfileExport"));
        string exported = fixture.Store.Import(fixture.ArchivePath, "Round trip");
        Assert.Equal(6, fixture.Store.LoadProfile(exported).MidiChannel);
        Assert.Equal("Rehearsal rig", fixture.Store.LoadProfile(exported).DeviceName);
        Assert.Equal("Session favorite", fixture.Store.LoadFavorites(exported).First().Name);
        Assert.Equal("Solo", fixture.Store.LoadProfile(exported).SceneNameCaches["ports"][4].Names[2]);
        fixture.ConfirmResult = false;
        Click(Find<Button>(window, "ProfileDelete"));
        Assert.Contains("SESSION", fixture.ConfirmationMessage);
        Assert.Equal("Delete profile", fixture.ConfirmationAction);
        Assert.Contains("SESSION", fixture.Store.ListProfiles());
        fixture.ConfirmResult = true;
        Click(Find<Button>(window, "ProfileDelete")); Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("SESSION", fixture.Store.ListProfiles());
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Same(liveFavorite, Field<List<Favorite>>(window, "_favorites").Single());
        Assert.Equal("Unsaved active favorite", liveFavorite.Name);
        Assert.Equal(10, Field<AppSettings>(window, "_settings").MidiChannel);
        Assert.Equal("Live cache", fixture.Settings.PresetNameCache[12]);
        Assert.Equal(activeBytes, File.ReadAllBytes(Path.Combine(fixture.DirectoryPath, "Default-settings.json")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(fixture.DirectoryPath, "DeletedProfiles"), "*.json", SearchOption.AllDirectories).Length);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public void ActiveExportUsesCurrentFavoritesMappingAndCachesAndThemeRetainsInactiveSelection()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        OpenProfileManagement(window);
        Field<AppSettings>(window, "_settings").MidiChannel = 12;
        Field<AppSettings>(window, "_settings").DisplayOffset = 1;
        Click(Find<Button>(window, "ProfilesBackToConfig")); Dispatcher.UIThread.RunJobs();
        SetLegacySceneCc(window, 61);
        Click(Find<Button>(window, "ManageProfiles")); Dispatcher.UIThread.RunJobs();
        Field<List<Favorite>>(window, "_favorites").Single().Name = "Current favorite";
        fixture.Settings.PresetNameCache[4] = "Current preset";
        fixture.Settings.SceneNameCaches["live"] = new() { [4] = new() { Names = ["Current scene"] } };
        Field<RadioButton>(window, "_darkThemeRadio").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("MIDI 12", ManagedFact(window, "Preset mapping"));
        Click(Find<Button>(window, "ProfileExport"));
        string name = fixture.Store.Import(fixture.ArchivePath, "Export check");
        Assert.Equal("Current favorite", fixture.Store.LoadFavorites(name).Single().Name);
        Assert.Equal(12, fixture.Store.LoadProfile(name).MidiChannel);
        Assert.Equal(1, fixture.Store.LoadProfile(name).DisplayOffset);
        Assert.Equal(61, fixture.Store.LoadProfile(name).SceneCc);
        Assert.Equal("Current preset", fixture.Store.LoadProfile(name).PresetNameCache[4]);
        Assert.Equal("Current scene", fixture.Store.LoadProfile(name).SceneNameCaches["live"][4].Names[0]);
        SelectManagedProfile(window, "Session");
        Field<RadioButton>(window, "_lightThemeRadio").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Contains("MIDI 6", ManagedFact(window, "Preset mapping"));
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public void CancellingUseProfilePreservesUnsavedFavoriteEditor()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        ShowEditor(window, Field<List<Favorite>>(window, "_favorites").Single());
        Find<TextBox>(window, "FavoriteName").Text = "Unsaved editor draft";
        OpenProfileManagement(window);
        SelectManagedProfile(window, "Session");
        fixture.ConfirmResult = false;
        Click(Find<Button>(window, "ProfileUse")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Contains("Discard unsaved favorite edits", fixture.ConfirmationMessage);
        Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Border>(window, "FavoriteEditorCard").IsVisible);
        Assert.Equal("Unsaved editor draft", Find<TextBox>(window, "FavoriteName").Text);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public void ActiveRenameRollsBackFilesAndIndicatorsWhenSavingNewIdentityFails()
    {
        using var fixture = new ManagedProfileFixture();
        var settings = fixture.Settings;
        var window = new MainWindow(settings, [], fixture.Midi, profileStore: fixture.Store,
            saveSettings: value =>
            {
                if (value.ActiveProfile == "Rejected rename") { throw new IOException("Simulated settings save failure."); }
                fixture.Store.SaveSettings(value);
            },
            profileNamePrompt: (_, _) => Task.FromResult<string?>("Rejected rename"));
        try
        {
            OpenProfileManagement(window);
            Field<List<Favorite>>(window, "_favorites").Single().Name = "Current active favorite";
            Click(Find<Button>(window, "ProfileRename")); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Default", settings.ActiveProfile);
            Assert.Equal("Default", Find<TextBlock>(window, "ManagedProfile").Text);
            Assert.Equal("Profile: Default", Find<TextBlock>(window, "HeaderActiveProfile").Text);
            Assert.Equal("Default", new ProfileStore(fixture.DirectoryPath).LoadSettings().ActiveProfile);
            Assert.Contains("Default", fixture.Store.ListProfiles());
            Assert.DoesNotContain("Rejected rename", fixture.Store.ListProfiles());
            Assert.Equal("Current active favorite", fixture.Store.LoadFavorites("Default").Single().Name);
            Assert.Contains("Simulated settings save failure", Find<TextBlock>(window, "ProfileManagementStatus").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CopyInactiveUsesItsDataActivatesIndependentCopyAndSavesPreviousLiveData()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        OpenProfileManagement(window);
        Field<List<Favorite>>(window, "_favorites").Single().Name = "Live active favorite";
        Field<AppSettings>(window, "_settings").MidiChannel = 9;
        SelectManagedProfile(window, "Session");
        fixture.NameResult = "Session copy";
        Click(Find<Button>(window, "ProfileCopy")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session copy", fixture.Settings.ActiveProfile);
        Assert.Equal("Session copy", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Equal(6, fixture.Settings.MidiChannel);
        Assert.Equal(DeviceModel.FM3, fixture.Settings.DeviceModel);
        Assert.Equal("Rehearsal rig", fixture.Settings.DeviceName);
        Assert.Equal("Rehearsal rig", ManagedFact(window, "Device name"));
        Assert.Equal(140, fixture.Settings.AutoSendDelayMs);
        Assert.Equal("Live active favorite", fixture.Store.LoadFavorites("Default").Single().Name);
        Assert.Equal(9, fixture.Store.LoadProfile("Default").MidiChannel);
        Field<List<Favorite>>(window, "_favorites").First().Name = "Copy edit";
        fixture.Settings.SceneNameCaches["ports"][4].Names[0] = "Copy scene";
        Assert.Equal("Session favorite", fixture.Store.LoadFavorites("Session").First().Name);
        Assert.Equal("Clean", fixture.Store.LoadProfile("Session").SceneNameCaches["ports"][4].Names[0]);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public void ImportSelectsWithoutActivationAndRescanRetainsOrRecoversSelection()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        fixture.Store.Export("Session", fixture.ArchivePath);
        OpenProfileManagement(window);
        fixture.NameResult = "Session";
        Click(Find<Button>(window, "ProfileImport")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session (2)", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Default", fixture.Store.LoadSettings().ActiveProfile);
        Click(Find<Button>(window, "ProfileManagementRescan")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session (2)", Find<TextBlock>(window, "ManagedProfile").Text);
        File.Move(Path.Combine(fixture.DirectoryPath, "Session (2)-settings.json"), Path.Combine(fixture.DirectoryPath, "removed-settings.bak"));
        Click(Find<Button>(window, "ProfileManagementRescan")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Default", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Contains("1 incomplete or unreadable", Find<TextBlock>(window, "ProfileManagementStatus").Text);
        fixture.NameResult = null;
        Click(Find<Button>(window, "ProfileCreate")); Click(Find<Button>(window, "ProfileImport"));
        Assert.Equal(3, fixture.Store.ListProfiles().Count);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
    }

    [AvaloniaFact]
    public void UnreadableInactiveProfileHasVisibleErrorAndNoWritesOrActivation()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        string path = Path.Combine(fixture.DirectoryPath, "Session-favorites.json");
        File.WriteAllText(path, "{unreadable");
        OpenProfileManagement(window);
        SelectManagedProfile(window, "Session");
        Assert.True(Find<TextBlock>(window, "ProfileDetailsError").IsVisible);
        Assert.Contains("Cannot read 'Session'", Find<TextBlock>(window, "ProfileDetailsError").Text);
        foreach (string name in new[] { "ProfileUse", "ProfileCopy", "ProfileRename", "ProfileExport", "ProfileDelete" })
        {
            Assert.False(Find<Button>(window, name).IsEnabled);
        }
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("{unreadable", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".pre-tags.bak"));
        // Exercise the safe switch guard as well as the disabled management command.
        Click(Find<Button>(window, "ProfilesBackToConfig")); Dispatcher.UIThread.RunJobs();
        Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Session";
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Default", Find<ComboBox>(window, "ProfileSelector").SelectedItem);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public void ActiveDeleteSkipsUnreadableAlternativesAndKeepsTheLastProfile()
    {
        using var fixture = new ManagedProfileFixture();
        var window = fixture.Window;
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "Session-settings.json"), "{unreadable");
        OpenProfileManagement(window);
        Click(Find<Button>(window, "ProfileDelete")); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Spare", fixture.Settings.ActiveProfile);
        Assert.DoesNotContain("Default", fixture.Store.ListProfiles());
        SelectManagedProfile(window, "Spare");
        Click(Find<Button>(window, "ProfileDelete"));
        Assert.Equal("Spare", fixture.Settings.ActiveProfile);
        Assert.Contains("No other readable profile", Find<TextBlock>(window, "ProfileManagementStatus").Text);
        Assert.Contains("Spare", fixture.Store.ListProfiles());
        fixture.Store.Delete("Session");
        Click(Find<Button>(window, "ProfileManagementRescan")); Dispatcher.UIThread.RunJobs();
        Assert.False(Find<Button>(window, "ProfileDelete").IsEnabled);
        Click(Find<Button>(window, "ProfileDelete"));
        Assert.Contains("Keep at least one profile", Find<TextBlock>(window, "ProfileManagementStatus").Text);
        Assert.Equal(["Spare"], fixture.Store.ListProfiles());
    }

    [AvaloniaFact]
    public async Task NameDialogValidatesInlineCancelsAndCreatesWithCompactFocusedControls()
    {
        using var fixture = new ManagedProfileFixture(realDialogs: true);
        var window = fixture.Window;
        OpenProfileManagement(window);
        Click(Find<Button>(window, "ProfileCreate")); Dispatcher.UIThread.RunJobs();
        var dialog = window.OwnedWindows.Single();
        var input = Find<TextBox>(dialog, "ProfileName");
        Assert.Same(input, dialog.FocusManager!.GetFocusedElement());
        Assert.Equal("Profile name", AutomationProperties.GetName(input));
        Assert.InRange(input.Bounds.Height, 32, 34);
        Assert.InRange(Find<Button>(dialog, "ProfileNameSave").Bounds.Height, 32, 34);
        foreach (string invalid in new[] { "", "../invalid", "Session" })
        {
            input.Text = invalid; Click(Find<Button>(dialog, "ProfileNameSave")); Dispatcher.UIThread.RunJobs();
            Assert.True(Find<TextBlock>(dialog, "ProfileNameError").IsVisible);
            Assert.True(dialog.IsVisible);
            Assert.Equal(3, fixture.Store.ListProfiles().Count);
        }
        Capture(dialog, "profile-name-dialog-validation");
        dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Click(Find<Button>(window, "ProfileCreate")); Dispatcher.UIThread.RunJobs();
        dialog = window.OwnedWindows.Single();
        Find<TextBox>(dialog, "ProfileName").Text = "Created";
        Click(Find<Button>(dialog, "ProfileNameSave"));
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        dialog = window.OwnedWindows.Single();
        Assert.Equal("Device not connected", dialog.Title);
        Click(Find<Button>(dialog, "ProfileConfirm"));
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Created", fixture.Settings.ActiveProfile);
        Assert.Empty(fixture.Store.LoadFavorites("Created"));
        Assert.Empty(fixture.Settings.PresetNameCache);
        Assert.Empty(fixture.Settings.SceneNameCaches);
        Assert.Equal(34, fixture.Settings.SceneCc);
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task DeleteConfirmationNamesSelectedTargetUsesCompactActionsAndCancels(string theme)
    {
        using var fixture = new ManagedProfileFixture(theme, realDialogs: true);
        var window = fixture.Window;
        OpenProfileManagement(window);
        SelectManagedProfile(window, "Session");
        Click(Find<Button>(window, "ProfileDelete")); Dispatcher.UIThread.RunJobs();
        var dialog = window.OwnedWindows.Single();
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("'Session'", StringComparison.Ordinal) == true);
        var cancel = Find<Button>(dialog, "ProfileConfirmCancel");
        Assert.Equal(32, Find<Button>(dialog, "ProfileConfirm").Bounds.Height);
        Assert.Equal("Delete profile", Find<Button>(dialog, "ProfileConfirm").Content);
        Assert.Equal(32, cancel.Bounds.Height);
        Assert.Same(cancel, dialog.FocusManager!.GetFocusedElement());
        Capture(dialog, $"profile-delete-dialog-{theme}");
        Click(cancel); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Contains("Session", fixture.Store.ListProfiles());
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
    }

    [AvaloniaTheory]
    [InlineData(1000, 640, "Light")]
    [InlineData(1200, 800, "Light")]
    [InlineData(1000, 640, "Dark")]
    [InlineData(1200, 800, "Dark")]
    public void ManagementMeasuresReferenceSizesAndScrollsLongNames(int width, int height, string theme)
    {
        using var fixture = new ManagedProfileFixture(theme);
        var window = fixture.Window;
        window.Width = width; window.Height = height;
        OpenProfileManagement(window);
        SelectManagedProfile(window, "Session");
        var card = Find<Border>(window, "ProfileManagementCard");
        var split = Find<Grid>(window, "ProfileManagementSplit");
        Assert.Equal(300, split.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(12, split.ColumnDefinitions[1].ActualWidth);
        foreach (string name in new[] { "ProfilesBackToConfig", "ProfileCreate", "ProfileImport", "ProfileManagementRescan", "ProfileUse", "ProfileCopy", "ProfileRename", "ProfileExport", "ProfileDelete" })
        {
            var button = Find<Button>(window, name);
            Assert.Equal(28, button.Bounds.Height);
            Assert.Equal(12, button.FontSize);
            Assert.Equal(5, button.CornerRadius.TopLeft);
            AssertContainedHorizontally(button, window);
            if (name != "ProfilesBackToConfig") { AssertContainedHorizontally(button, card); }
        }
        var list = Find<ListBox>(window, "ManagedProfiles");
        Assert.All(list.Items.OfType<ListBoxItem>(), row => Assert.Equal(26, row.Bounds.Height));
        Assert.True(Find<Button>(window, "ProfileUse").IsEnabled);
        Capture(window, $"profile-management-{width}x{height}-{theme}");
        string longName = "A long profile name for rehearsal with extra notes and a backup configuration";
        fixture.Store.Create(longName);
        for (int i = 0; i < 18; i++) { fixture.Store.Create($"Workshop {i:00}"); }
        Click(Find<Button>(window, "ProfileManagementRescan")); Dispatcher.UIThread.RunJobs();
        SelectManagedProfile(window, longName);
        Assert.Equal(longName, ToolTip.GetTip(Find<TextBlock>(window, "ManagedProfile")));
        AssertContainedHorizontally(Find<Border>(window, "ProfileDetails"), window);
        Capture(window, $"profile-management-long-name-{width}x{height}-{theme}");
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        ((ListBoxItem)list.SelectedItem!).Focus();
        window.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Workshop 17", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.True(scroll.Offset.Y > 0);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.All(list.Items.OfType<ListBoxItem>(), row => Assert.Equal(26, row.Height));
        Capture(window, $"profile-management-scrolling-{width}x{height}-{theme}");
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }
}
