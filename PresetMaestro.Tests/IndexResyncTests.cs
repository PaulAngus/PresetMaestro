using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class IndexResyncTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-resync-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    private async Task<(MainWindow Window, DeviceMidi Midi, IndexLibrary Library, ProfileStore Store, IndexDevice Device)> Setup(string theme = "Light", int width = 1440, bool legacy = false)
    {
        var before = await SceneTagMoveTests.Read(SceneTagMoveTests.Image());
        if (legacy) { before = before with { SceneContentSha256 = null }; }
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme; settings.DisplayOffset = 1;
        settings.MidiInputPort = settings.MidiOutputPort = "FM9";
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var cache = new DeviceIndex
        {
            Device = device,
            PresetMapping = new(5, 1, 77),
            Committed = new IndexScan
            {
                Status = "Complete",
                Firmware = "12.00",
                FinishedAt = DateTimeOffset.UtcNow,
                Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot, slot => slot == 0 ? before : FractalIndexWorkflowTests.Preset(slot)),
            }
        };
        library.Save(cache);
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        var tags = profile.GetOrCreate(device.Id, before, "12.00");
        tags.Tags = ["Live"]; tags.SceneTags[0] = ["Rhythm"]; tags.SceneTags[3] = ["Lead"];
        var favorites = new List<Favorite> { new() { Id = 1, Slot = 1, Name = "First scene", Preset = 1, Scene = 1 } };
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettingsAndFavorites(settings, favorites);
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 0x64, [0, 0])); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.SyntheticName("Stage")); }
        };
        var window = new MainWindow(settings, [], midi, profileStore: store)
        { Width = width, Height = width == 1000 ? 640 : 850, ThruInputRetryDelay = TimeSpan.Zero };
        window.Show();
        await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
        Click(Find<Button>(window, "NavPresetIndex"));
        Find<ListBox>(window, "IndexPresetList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        return (window, midi, library, store, device);
    }

    private static void Reader(MainWindow window, SceneTagMoveTests.Source source) => typeof(MainWindow)
        .GetField("_fractalIndexReader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window,
            new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()));

    [AvaloniaTheory]
    [InlineData("Light", 1440, "follow", false)]
    [InlineData("Dark", 1440, "keep", false)]
    [InlineData("Light", 1000, "cancel", false)]
    [InlineData("Dark", 1000, "follow", true)]
    public async Task SceneResyncNamesRequireAnExplicitTagDecisionAndKeepThePresetSelected(string theme, int width, string decision, bool legacy)
    {
        var (window, midi, library, store, device) = await Setup(theme, width, legacy);
        try
        {
            var before = library.Load(device.Id)!.Committed!.Presets[0];
            var source = new SceneTagMoveTests.Source((slot, _, _) => Task.FromResult(SceneTagMoveTests.Image(slot, true, !legacy)));
            Reader(window, source);
            Snapshot(window, $"resync-buttons-{width}-{theme}");
            var send = Find<Button>(window, "IndexSendCommand");
            var presets = Find<Button>(window, "IndexResyncPresets");
            Assert.True(presets.TranslatePoint(default, window)!.Value.X > send.TranslatePoint(default, window)!.Value.X + send.Bounds.Width);
            var pending = window.ResyncIndexAsync(0); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Same(Find<Button>(dialog, "SceneTagMoveFollow"), dialog.FocusManager?.GetFocusedElement());
            dialog.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(Find<Button>(dialog, "SceneTagMoveKeep"), dialog.FocusManager?.GetFocusedElement());
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Mode 1: scene 1 → scene 4");
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Greasy Mode 1: scene 4 → scene 1");
            Assert.False(Find<Button>(window, "IndexResyncPresets").IsEnabled);
            Snapshot(dialog, $"resync-tag-decision-{width}-{theme}");
            if (decision == "cancel") { dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); }
            else { Click(Find<Button>(dialog, decision == "follow" ? "SceneTagMoveFollow" : "SceneTagMoveKeep")); }
            await pending;
            var saved = library.Load(device.Id)!.Committed!.Presets[0];
            var attached = IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).Annotations.Single();
            Assert.Equal(decision == "follow" ? "Lead" : "Rhythm", Assert.Single(attached.SceneTags[0]));
            Assert.Equal(decision == "follow" ? "Rhythm" : "Lead", Assert.Single(attached.SceneTags[3]));
            Assert.Equal(decision == "cancel" ? before.ContentSha256 : (await SceneTagMoveTests.Read(SceneTagMoveTests.Image(0, true, !legacy))).ContentSha256, saved.ContentSha256);
            Assert.Equal([0], source.Reads);
            Assert.Equal(0, Find<ListBox>(window, "IndexPresetList").SelectedIndex);
            Assert.Same(Find<Button>(window, "IndexResyncScenes"), window.FocusManager?.GetFocusedElement());
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactSwapsFollowTagsAutomaticallyForSceneAndFullPresetResync(bool allPresets)
    {
        var (window, midi, library, store, device) = await Setup();
        try
        {
            var source = new SceneTagMoveTests.Source((slot, _, _) => Task.FromResult(SceneTagMoveTests.Image(slot, slot == 0)));
            Reader(window, source);
            await window.ResyncIndexAsync(allPresets ? null : 0);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(allPresets ? 512 : 1, source.Reads.Count);
            Assert.Equal("Greasy Mode 1", library.Load(device.Id)!.Committed!.Presets[0].SceneNames[0]);
            var tags = IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).Annotations.Single();
            Assert.Equal(["Lead"], tags.SceneTags[0]); Assert.Equal(["Rhythm"], tags.SceneTags[3]);
            Assert.Equal(["Lead"], store.LoadFavorites("Default").Single(f => f.Name == "First scene").Tags);
            Assert.Equal(["Live"], tags.Tags);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAndCancelledReadsKeepSavedDataAndTags(bool cancel)
    {
        var (window, midi, library, store, device) = await Setup();
        try
        {
            string before = System.Text.Json.JsonSerializer.Serialize(library.Load(device.Id), IndexJson.Options);
            var source = new SceneTagMoveTests.Source(async (_, _, token) =>
            {
                if (cancel) { await Task.Delay(Timeout.Infinite, token); }
                throw new IOException("test read failed");
            });
            Reader(window, source);
            var pending = window.ResyncIndexAsync(0);
            if (cancel) { Click(Find<Button>(window, "IndexCancel")); }
            await pending;
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(library.Load(device.Id), IndexJson.Options));
            Assert.Equal(["Rhythm"], IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).Annotations.Single().SceneTags[0]);
            Assert.True(Find<Button>(window, "IndexResyncScenes").IsEnabled);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SaveFailureRollsBackProfileTagsFavoritesAndDeviceData()
    {
        var (window, _, library, store, device) = await Setup();
        try
        {
            var before = library.Load(device.Id)!;
            library.Save(before); // Ensure a readable backup exists, then deny writes to it.
            string settingsPath = Path.Combine(_directory, "Default-settings.json");
            string favoritePath = Path.Combine(_directory, "Default-favorites.json");
            string settingsBefore = File.ReadAllText(settingsPath), favoritesBefore = File.ReadAllText(favoritePath);
            string backupPath = Path.Combine(library.DirectoryPath, device.Id.ToString("N") + ".json.bak");
            using var locked = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Reader(window, new SceneTagMoveTests.Source((slot, _, _) => Task.FromResult(SceneTagMoveTests.Image(slot, true))));
            await window.ResyncIndexAsync(0);
            Assert.Equal(settingsBefore, File.ReadAllText(settingsPath));
            Assert.Equal(favoritesBefore, File.ReadAllText(favoritePath));
            Assert.Equal(before.Committed!.Presets[0].ContentSha256, library.Load(device.Id)!.Committed!.Presets[0].ContentSha256);
            Assert.Contains("Could not resync", Find<TextBlock>(window, "IndexStatus").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ResyncRejectsBusyDifferentModelAndDisconnectedDevicesWithoutReading()
    {
        var (window, _, _, _, _) = await Setup();
        try
        {
            var source = new SceneTagMoveTests.Source((slot, _, _) => Task.FromResult(SceneTagMoveTests.Image(slot)));
            Reader(window, source);
            using var busy = new CancellationTokenSource();
            var field = typeof(MainWindow).GetField("_presetNamesCts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(window, busy);
            await window.ResyncIndexAsync(); await window.ResyncIndexAsync(0);
            field.SetValue(window, null);
            window.ConnectionState.Device = new PresetMaestro.Midi.FractalDeviceInformation(DeviceModel.FM3, null);
            await window.ResyncIndexAsync(); await window.ResyncIndexAsync(0);
            typeof(MainWindow).GetMethod("Disconnect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await window.ResyncIndexAsync(); await window.ResyncIndexAsync(0);
            Assert.Empty(source.Reads);
            Assert.False(Find<Button>(window, "IndexResyncPresets").IsEnabled);
            Assert.False(Find<Button>(window, "IndexResyncScenes").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DisconnectDuringAReadCannotCommitItsLateReply()
    {
        var (window, _, library, _, device) = await Setup();
        try
        {
            var before = System.Text.Json.JsonSerializer.Serialize(library.Load(device.Id), IndexJson.Options);
            var reply = new TaskCompletionSource<PresetNameSync.Core.StoredPresetImage>();
            Reader(window, new SceneTagMoveTests.Source((_, _, _) => reply.Task));
            var pending = window.ResyncIndexAsync(0);
            typeof(MainWindow).GetMethod("Disconnect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            reply.SetResult(SceneTagMoveTests.Image(swapped: true));
            await pending;
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(library.Load(device.Id), IndexJson.Options));
            Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PartialFullReadKeepsTheCommittedLibraryAndTags()
    {
        var (window, _, library, store, device) = await Setup();
        try
        {
            var before = System.Text.Json.JsonSerializer.Serialize(library.Load(device.Id), IndexJson.Options);
            Reader(window, new SceneTagMoveTests.Source((slot, _, _) => slot == 7
                ? Task.FromException<PresetNameSync.Core.StoredPresetImage>(new IOException("unreadable slot"))
                : Task.FromResult(SceneTagMoveTests.Image(slot, slot == 0))));
            await window.ResyncIndexAsync();
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(library.Load(device.Id), IndexJson.Options));
            Assert.Equal(["Rhythm"], IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).Annotations.Single().SceneTags[0]);
            Assert.Empty(window.OwnedWindows);
            Assert.Contains("Some presets could not be read", Find<TextBlock>(window, "IndexStatus").Text);
        }
        finally { window.Close(); }
    }

    private static void Snapshot(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
        if (output is null) { return; }
        Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
        using var image = window.CaptureRenderedFrame(); image!.Save(Path.Combine(output, name + ".png"));
    }
}
