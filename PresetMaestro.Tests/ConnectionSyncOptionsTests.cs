using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class ConnectionSyncOptionsTests
{
    private static DeviceMidi Midi()
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-fm9")); }
        };
        return midi;
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) { await Task.Delay(10, deadline.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window dialog, string name)
    {
        string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
        if (output is null) { return; }
        Directory.CreateDirectory(output);
        using var screenshot = dialog.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, name + ".png"));
    }

    [AvaloniaFact]
    public async Task PresetNameRequestsContinueWhileDialogUiIsBusy()
    {
        using var repliesComplete = new ManualResetEventSlim();
        int requests = 0;
        var settings = new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9" };
        var window = new MainWindow(settings, [], Midi(), saveSettings: _ => { }, saveFavorites: _ => { },
            queryPresetNameAsync: (slot, _, _) => Task.Run(() =>
            {
                Interlocked.Increment(ref requests);
                if (slot == 511) { repliesComplete.Set(); }
                return new PresetNameResult(slot, "Preset " + slot);
            }))
        { ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            var sync = window.SyncPresetNamesAsync();
            // Deliberately withhold the UI dispatcher: receiving and requesting
            // names must proceed without waiting for a window to render.
            bool readIndependently = repliesComplete.Wait(TimeSpan.FromSeconds(2));
            await sync;
            Assert.True(readIndependently, "The device request loop waited for the UI dispatcher between replies.");
            Assert.Equal(512, requests);
            Assert.Equal(512, settings.PresetNameCache.Count);
            Assert.Equal("Preset names synced", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PartialNameSyncIsIncompleteInBothViewsEvenWithAFullCache()
    {
        var settings = new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9", PresetNameCache = Enumerable.Range(0, 512).ToDictionary(s => s, s => "Cached " + s) };
        var window = new MainWindow(settings, [], Midi(), saveSettings: _ => { }, saveFavorites: _ => { },
            queryPresetNameAsync: (slot, _, _) => slot is 10 or 220 or 236
                ? throw new TimeoutException("No reply")
                : Task.FromResult(new PresetNameResult(slot, "Refreshed " + slot)))
        { ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            await window.SyncPresetNamesAsync();
            Assert.Equal("Preset-name sync needs attention", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.Contains("509 of 512", Find<TextBlock>(dialog, "ConnectionSyncStatus").Text);
            Click(Find<Button>(dialog, "ConnectionSyncDone"));
            Click(Find<Button>(window, "NavConfig"));
            Assert.StartsWith("Incomplete: 509 of 512", Find<TextBlock>(window, "PresetSyncStatus").Text);
            Assert.Equal(512, settings.PresetNameCache.Count);
            Assert.Equal("Cached 220", settings.PresetNameCache[220]);
            Assert.Equal("Refreshed 221", settings.PresetNameCache[221]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("complete", "Light")]
    [InlineData("cancel", "Light")]
    [InlineData("fail", "Light")]
    [InlineData("complete", "Dark")]
    public async Task PresetNameSyncShowsProgressThenKeepsOutcomeAndNextStep(string outcome, string theme)
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9", Theme = theme };
        var midi = Midi();
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { },
            queryPresetNameAsync: async (slot, _, token) =>
            {
                if (slot == 2)
                {
                    await released.Task.WaitAsync(token);
                    if (outcome == "fail") { throw new IOException("Device stopped responding."); }
                }
                return new PresetNameResult(slot, "Preset " + slot);
            })
        { Width = 1200, Height = 900, ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            var sync = window.SyncPresetNamesAsync();
            // Progress is rendered on the UI timer, independently of device replies.
            var progressUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = Find<ProgressBar>(dialog, "ConnectionSyncProgress");
            progress.PropertyChanged += (_, e) => { if (e.Property == ProgressBar.ValueProperty && progress.Value == 2) { progressUpdated.TrySetResult(); } };
            await progressUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Syncing preset names…", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.Contains("2 of 512", Find<TextBlock>(dialog, "ConnectionSyncStatus").Text);
            Assert.True(progress.IsEffectivelyVisible);
            Assert.False(progress.IsIndeterminate);
            Assert.Equal(2, progress.Value);
            Assert.False(Find<Button>(dialog, "ConnectionSyncNames").IsEnabled);
            Capture(dialog, "connection-sync-progress-" + theme.ToLowerInvariant());
            if (outcome == "cancel") { Click(Find<Button>(dialog, "ConnectionSyncCancel")); }
            else { released.SetResult(); }
            await sync; Dispatcher.UIThread.RunJobs();
            Assert.Equal(outcome switch { "complete" => "Preset names synced", "cancel" => "Preset-name sync cancelled", _ => "Preset-name sync stopped" }, Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.Contains(outcome == "complete" ? "512 of 512" : "2 of 512", Find<TextBlock>(dialog, "ConnectionSyncStatus").Text);
            Assert.Contains(outcome == "complete" ? "Done to continue" : "Sync names to retry", Find<TextBlock>(dialog, "ConnectionSyncNext").Text);
            Assert.False(progress.IsVisible);
            Assert.True(Find<Button>(dialog, "ConnectionSyncNames").IsEnabled);
            Assert.Equal(outcome == "complete" ? 512 : 2, settings.PresetNameCache.Count);
            Capture(dialog, "connection-sync-" + outcome + "-" + theme.ToLowerInvariant());
            if (outcome == "complete")
            {
                Click(Find<Button>(dialog, "ConnectionSyncDone"));
                Click(Find<Button>(window, "NavConfig"));
                Click(Find<Button>(window, "ConfigSyncOptions"));
                Assert.Equal("Preset names synced", Find<TextBlock>(Assert.Single(window.OwnedWindows), "ConnectionSyncStatusTitle").Text);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigAndDialogReadAllNamesWithoutRefreshingUiPerReply(bool fromDialog)
    {
        var settings = new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9" };
        var requests = new List<int>();
        var window = new MainWindow(settings, [], Midi(), saveSettings: _ => { }, saveFavorites: _ => { },
            queryPresetNameAsync: (slot, _, _) =>
            {
                requests.Add(slot);
                return Task.FromResult(new PresetNameResult(slot, "Preset " + slot));
            })
        { ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            int statusUpdates = 0;
            Find<TextBlock>(dialog, "ConnectionSyncStatus").PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBlock.TextProperty) { statusUpdates++; }
            };
            if (fromDialog) { Click(Find<Button>(dialog, "ConnectionSyncNames")); }
            else
            {
                Click(Find<Button>(dialog, "ConnectionSyncDone"));
                Click(Find<Button>(window, "NavConfig"));
                Click(Find<Button>(window, "ConfigPresetSync"));
            }
            await WaitFor(() => settings.PresetNameCache.Count == 512);
            Assert.Equal(Enumerable.Range(0, 512), requests);
            Assert.Equal(512, settings.PresetNameCache.Count);
            // Immediate replies need only a few UI updates, never one per name.
            Assert.InRange(statusUpdates, 0, 10);
            if (fromDialog) { Assert.Equal("Preset names synced", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SuccessfulConnectionOpensOptionsAndCanReopenFromConfig()
    {
        var midi = Midi();
        var settings = new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9", Theme = "Light" };
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { }) { Width = 1200, Height = 900, ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("ConnectionSyncDialog", dialog.Name);
            Assert.True(Find<Button>(dialog, "ConnectionSyncNames").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncScenes").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncLibrary").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncCheck").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncFullCheck").IsEnabled);
            Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<Border>(), b => b.IsEffectivelyVisible && b.GetVisualDescendants().OfType<Button>().Any(c => c.Name == "ConnectionSyncResume"));
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output); using var screenshot = dialog.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, "connection-sync-options.png"));
            }
            Click(Find<Button>(dialog, "ConnectionSyncDone"));
            Assert.Empty(window.OwnedWindows);
            Click(Find<Button>(window, "NavConfig"));
            Click(Find<Button>(window, "ConfigSyncOptions"));
            Assert.Single(window.OwnedWindows);
            Assert.DoesNotContain(midi.Sent, frame => frame[0] is >= 0xc0 and <= 0xcf);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FavoriteSceneSyncReadsDistinctStoredPresetsAndSavesProfileCache()
    {
        var settings = new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9", DisplayOffset = 1 };
        List<int> reads = [];
        var midi = Midi();
        var favorites = new List<Favorite> { new() { Id = 1, Name = "Clean", Preset = 5 }, new() { Id = 2, Name = "Solo", Preset = 5 }, new() { Id = 3, Name = "Other", Preset = 9 } };
        int saves = 0;
        var window = new MainWindow(settings, favorites, midi, saveSettings: _ => saves++, saveFavorites: _ => { },
            queryStoredScenesAsync: (slot, _) => { reads.Add(slot); return Task.FromResult(new PresetScenes(slot, "Preset " + slot, ["Clean", "Solo"])); })
        { ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.True(Find<Button>(dialog, "ConnectionSyncScenes").IsEnabled);
            await window.SyncFavoriteSceneNamesAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 4, 8 }, reads);
            var cache = Assert.Single(settings.SceneNameCaches.Values);
            Assert.Equal(new[] { "Clean", "Solo" }, cache[4].Names);
            Assert.Equal("stored", cache[8].Source);
            Assert.Contains("2 presets", Find<TextBlock>(dialog, "ConnectionSyncStatus").Text);
            Assert.True(saves >= 2);
            Assert.DoesNotContain(midi.Sent, frame => frame[0] is >= 0xc0 and <= 0xcf);
        }
        finally { window.Close(); }
    }
}
