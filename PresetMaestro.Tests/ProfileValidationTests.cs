using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    private sealed class ProfileValidationFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "PresetMaestro-validation-" + Guid.NewGuid().ToString("N"));
        public ProfileStore Store { get; }
        public AppSettings Settings { get; }
        public FakeMidi Midi { get; } = new();
        public MainWindow Window { get; }
        public bool Confirm { get; set; }
        public Func<string, bool>? ConfirmTitle { get; set; }
        public List<(string Title, string Message, string Ok, string Cancel)> Dialogs { get; } = [];
        public List<int> PresetQueries { get; } = [];
        public List<int> SceneQueries { get; } = [];
        public Func<int, CancellationToken, Task<PresetNameResult>> PresetQuery { get; set; } =
            (slot, _) => Task.FromResult(new PresetNameResult(slot, "Preset " + slot));
        public Func<int, CancellationToken, Task<PresetScenes>> SceneQuery { get; set; } =
            (slot, _) => Task.FromResult(new PresetScenes(slot, "Preset " + slot, ["Clean", "Solo"]));

        public ProfileValidationFixture(bool realDialogs = false, string theme = "Light")
        {
            Store = new ProfileStore(DirectoryPath);
            Settings = Store.LoadSettings();
            Settings.Theme = theme;
            Store.SaveFavorites("Default", [Clone(Sample)]);
            Store.Create("Target", new ProfileSettings
            {
                DeviceName = "Studio",
                MidiChannel = 6,
                PresetNameCache = new() { [4] = "Preset 4", [5] = "Preset 5" },
                SceneNameCaches = new() { ["old ports"] = new() { [4] = new() { Names = ["Clean", "Solo"] } } },
            }, [new Favorite { Id = 20, Slot = 1, Name = "Target favorite", Scene = 1 }]);
            Window = new MainWindow(Settings, [], Midi,
                (slot, _, token) => { PresetQueries.Add(slot); return PresetQuery(slot, token); },
                profileStore: Store,
                confirm: realDialogs ? null : (title, message, ok, cancel) =>
                {
                    Dialogs.Add((title, message, ok, cancel));
                    return Task.FromResult(ConfirmTitle?.Invoke(title) ?? Confirm);
                },
                queryStoredScenesAsync: (slot, token) => { SceneQueries.Add(slot); return SceneQuery(slot, token); });
            Window.Show(); Dispatcher.UIThread.RunJobs();
        }

        public void Connect(DeviceModel model = DeviceModel.FM9, string? name = "Studio")
        {
            Midi.InputOpen = Midi.OutputOpen = true;
            Window.ConnectionState.Device = new FractalDeviceInformation(model, name);
        }

        public Task Switch(string action = "select", string name = "Target") =>
            (Task)typeof(MainWindow).GetMethod("RunProfileActionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Window, [action, name, null])!;

        public void Dispose()
        {
            foreach (var dialog in Window.OwnedWindows.ToArray()) { dialog.Close(); }
            Window.Close();
            Directory.Delete(DirectoryPath, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisconnectedWarningFromBothSwitchControlsCanCancelAndContinue(bool management)
    {
        using var fixture = new ProfileValidationFixture();
        var window = fixture.Window;
        OpenProfileManagement(window);
        SelectManagedProfile(window, "Target");
        if (!management) { Click(Find<Button>(window, "ProfilesBackToConfig")); Dispatcher.UIThread.RunJobs(); }
        var files = Directory.GetFiles(fixture.DirectoryPath, "*.json").ToDictionary(path => path, File.ReadAllBytes);
        var favorites = Field<List<Favorite>>(window, "_favorites");
        var presetCache = fixture.Settings.PresetNameCache;
        void Switch()
        {
            if (management) { Click(Find<Button>(window, "ProfileUse")); }
            else { Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Target"; }
            Dispatcher.UIThread.RunJobs();
        }
        Switch();
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Default", Field<ComboBox>(window, "_profileCombo").SelectedItem);
        Assert.Same(favorites, Field<List<Favorite>>(window, "_favorites"));
        Assert.Same(presetCache, fixture.Settings.PresetNameCache);
        foreach (var (path, bytes) in files) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        var dialog = Assert.Single(fixture.Dialogs);
        Assert.Contains("isn't connected", dialog.Message);
        Assert.Equal("OK", dialog.Ok);
        Assert.Equal("Cancel", dialog.Cancel);
        fixture.Confirm = true;
        Switch();
        Assert.Equal("Target", fixture.Settings.ActiveProfile);
        Assert.Equal("Target", new ProfileStore(fixture.DirectoryPath).LoadSettings().ActiveProfile);
        Assert.Equal("Target favorite", favorites.Single().Name);
        Assert.Empty(fixture.PresetQueries);
        Assert.Empty(fixture.SceneQueries);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public async Task MatchingProfileChecksLiveNamesWithoutPromptOrChangingDevicePreset()
    {
        using var fixture = new ProfileValidationFixture();
        fixture.Connect();
        // Matching active-profile caches must never substitute for actual device reads.
        fixture.Settings.PresetNameCache[5] = "Unrelated current cache";
        await fixture.Switch();
        Assert.Equal("Target", fixture.Settings.ActiveProfile);
        Assert.Empty(fixture.Dialogs);
        Assert.Equal([4], fixture.SceneQueries);
        Assert.Equal([5], fixture.PresetQueries);
        Assert.Equal("Preset 5", fixture.Settings.PresetNameCache[5]);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public async Task CancellingDeviceWarningKeepsFavoriteDraftAfterDiscardConfirmation()
    {
        using var fixture = new ProfileValidationFixture();
        ShowEditor(fixture.Window, Field<List<Favorite>>(fixture.Window, "_favorites").Single());
        Find<TextBox>(fixture.Window, "FavoriteName").Text = "Unsaved draft";
        fixture.ConfirmTitle = title => title == "Change Profile?";
        await fixture.Switch();
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.True(Find<Border>(fixture.Window, "FavoriteEditorCard").IsVisible);
        Assert.Equal("Unsaved draft", Find<TextBox>(fixture.Window, "FavoriteName").Text);
        Assert.Equal(2, fixture.Dialogs.Count);
        Assert.Equal("Device not connected", fixture.Dialogs[1].Title);
    }

    [AvaloniaTheory]
    [InlineData("type", "Device type")]
    [InlineData("device", "Device name")]
    [InlineData("preset", "Preset names")]
    [InlineData("scene", "Scene names")]
    public async Task EachMismatchWarnsAndCancelPreservesCurrentProfile(string mismatch, string category)
    {
        using var fixture = new ProfileValidationFixture();
        fixture.Connect(name: mismatch == "device" ? "Different rig" : "Studio");
        if (mismatch == "type")
        {
            var target = fixture.Store.LoadProfile("Target");
            target.DeviceModel = DeviceModel.FM3;
            File.WriteAllText(Path.Combine(fixture.DirectoryPath, "Target-settings.json"), System.Text.Json.JsonSerializer.Serialize(target));
        }
        if (mismatch == "preset") { fixture.PresetQuery = (slot, _) => Task.FromResult(new PresetNameResult(slot, "Changed preset")); }
        if (mismatch == "scene") { fixture.SceneQuery = (slot, _) => Task.FromResult(new PresetScenes(slot, "Preset " + slot, ["Changed scene", "Solo"])); }
        await fixture.Switch();
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Contains(category + ":", Assert.Single(fixture.Dialogs).Message);
        Assert.Equal("Clean", Field<List<Favorite>>(fixture.Window, "_favorites").Single().Name);
        fixture.Confirm = true;
        await fixture.Switch();
        Assert.Equal("Target", fixture.Settings.ActiveProfile);
    }

    [AvaloniaFact]
    public async Task FailedNameReadsAndUnsupportedDeviceOfferContinueAnyway()
    {
        using var fixture = new ProfileValidationFixture();
        fixture.Connect();
        fixture.SceneQuery = (_, _) => throw new TimeoutException("No scene reply");
        fixture.PresetQuery = (_, _) => throw new TimeoutException("No preset reply");
        await fixture.Switch();
        string message = Assert.Single(fixture.Dialogs).Message;
        Assert.Contains("Scene names: could not fully check", message);
        Assert.Contains("Preset names: could not fully check", message);
        fixture.Dialogs.Clear(); fixture.PresetQueries.Clear(); fixture.SceneQueries.Clear();
        fixture.Connect(DeviceModel.FM3, null);
        await fixture.Switch();
        message = Assert.Single(fixture.Dialogs).Message;
        Assert.Contains("Device name: cannot fully check", message);
        Assert.Contains("FM9 only", message);
        Assert.Empty(fixture.PresetQueries);
        Assert.Empty(fixture.SceneQueries);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
    }

    [AvaloniaFact]
    public async Task DeviceRemovalDuringValidationShowsDisconnectedWarningAndSyncCannotStart()
    {
        using var fixture = new ProfileValidationFixture();
        fixture.Connect();
        var pending = new TaskCompletionSource<PresetScenes>();
        fixture.SceneQuery = (_, token) => pending.Task.WaitAsync(token);
        Task switching = fixture.Switch();
        await fixture.Window.SyncPresetNamesAsync();
        Assert.Empty(fixture.PresetQueries);
        typeof(MainWindow).GetMethod("Disconnect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Window, null);
        await switching;
        Assert.True(fixture.Dialogs.Count > 0, Field<TextBlock>(fixture.Window, "_profileStatus").Text);
        Assert.Equal("Device not connected", Assert.Single(fixture.Dialogs).Title);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
    }

    [AvaloniaTheory]
    [InlineData("create", "New")]
    [InlineData("copy", "Copy")]
    [InlineData("delete", "")]
    public async Task CancellingSwitchDoesNotCreateCopyOrDeleteProfiles(string action, string name)
    {
        using var fixture = new ProfileValidationFixture();
        fixture.ConfirmTitle = title => title == "Delete Profile?";
        await fixture.Switch(action, name);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal(["Default", "Target"], fixture.Store.ListProfiles());
        Assert.True(File.Exists(Path.Combine(fixture.DirectoryPath, "Default-settings.json")));
        Assert.Contains(fixture.Dialogs, dialog => dialog.Title == "Device not connected");
    }

    [AvaloniaFact]
    public async Task ActualDisconnectedDialogHasOkCancelAndClosingKeepsProfile()
    {
        using var fixture = new ProfileValidationFixture(realDialogs: true);
        Task switching = fixture.Switch();
        Dispatcher.UIThread.RunJobs();
        var dialog = fixture.Window.OwnedWindows.Single();
        Assert.Equal("Device not connected", dialog.Title);
        Assert.Equal("OK", Find<Button>(dialog, "ProfileConfirm").Content);
        Assert.Equal("Cancel", Find<Button>(dialog, "ProfileConfirmCancel").Content);
        Assert.Same(Find<Button>(dialog, "ProfileConfirmCancel"), dialog.FocusManager!.GetFocusedElement());
        Capture(dialog, "profile-device-disconnected");
        dialog.Close(); await switching;
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
    }

    [AvaloniaFact]
    public async Task ActualMismatchDialogListsEveryMismatchAndOkSwitches()
    {
        using var fixture = new ProfileValidationFixture(realDialogs: true);
        fixture.Connect(name: "Other rig");
        var target = fixture.Store.LoadProfile("Target");
        target.DeviceModel = DeviceModel.FM3;
        // Different port caches must also be checked without making duplicate device reads.
        target.SceneNameCaches["other ports"] = new() { [4] = new() { Names = ["Old", "Old"] } };
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "Target-settings.json"), System.Text.Json.JsonSerializer.Serialize(target));
        fixture.PresetQuery = (slot, _) => Task.FromResult(new PresetNameResult(slot, "Changed preset"));
        Task switching = fixture.Switch();
        Dispatcher.UIThread.RunJobs();
        var dialog = fixture.Window.OwnedWindows.Single();
        Assert.Equal("Profile does not fully match", dialog.Title);
        string message = ((StackPanel)dialog.Content!).Children.OfType<TextBlock>().Single().Text!;
        foreach (string category in new[] { "Device type:", "Device name:", "Preset names:", "Scene names:" })
        {
            Assert.Contains(category, message);
        }
        Assert.Contains("Scene names: 2 saved names", message);
        Assert.Equal([4], fixture.SceneQueries);
        Capture(dialog, "profile-device-mismatch");
        Click(Find<Button>(dialog, "ProfileConfirm"));
        await switching;
        Assert.Equal("Target", fixture.Settings.ActiveProfile);
        Assert.Equal("Target", new ProfileStore(fixture.DirectoryPath).LoadSettings().ActiveProfile);
    }
}
