using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class SavedScenePollingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Maestro-saved-poll-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) { await Task.Delay(5, timeout.Token); }
    }

    private sealed class Session(MainWindow window, AppSettings settings, DeviceMidi midi, ProfileStore store, IndexLibrary library, IndexDevice device)
    {
        public MainWindow Window { get; } = window;
        public AppSettings Settings { get; } = settings;
        public DeviceMidi Midi { get; } = midi;
        public ProfileStore Store { get; } = store;
        public IndexLibrary Library { get; } = library;
        public IndexDevice Device { get; } = device;
        public Fm9ActivePreset Live { get; set; } = LiveFor(SceneTagMoveTests.Image());
        public StoredPresetImage Saved { get; set; } = SceneTagMoveTests.Image();
        public List<int> Reads { get; } = [];
        public string Log => string.Join("\n", (Queue<string>)typeof(MainWindow).GetField("_logLines", Fields)!.GetValue(Window)!) +
            string.Join("; ", new[] { "_sceneCts", "_presetNamesCts", "_pollBusy", "_changingProfile", "_sceneClosing", "_favEditingId", "_indexError" }
                .Select(name => name + "=" + typeof(MainWindow).GetField(name, Fields)!.GetValue(Window))) +
            $" pending={Window.ConnectionState.Pending != null}; device={Window.ConnectionState.Device}; ports={Midi.InputOpen}/{Midi.OutputOpen}; sent={Midi.Sent.Count}; dialogs={Window.OwnedWindows.Count}";
        public Func<int, CancellationToken, Task<StoredPresetImage>>? Read { get; set; }
        public IndexedPreset Preset => Library.Load(Device.Id)!.Committed!.Presets[0];
        public PresetAnnotation Tags => IndexJson.ReadProfile(Store.LoadProfile("Default").FractalIndex).Annotations.Single();
        public Task Poll() => Window.PollSceneStateAsync();
    }

    private static Fm9ActivePreset LiveFor(StoredPresetImage image, bool edited = false) =>
        new(image.Scenes.Slot, edited, image.Scenes.PresetName, image.Scenes.Names);

    private async Task<Session> Setup(bool legacy = false, string theme = "Light", int width = 1440)
    {
        var before = await SceneTagMoveTests.Read(SceneTagMoveTests.Image());
        if (legacy) { before = before with { SceneContentSha256 = null }; }
        var other = await SceneTagMoveTests.Read(SceneTagMoveTests.Image(1));
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme; settings.DisplayOffset = 1;
        settings.MidiInputPort = settings.MidiOutputPort = "FM9";
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        var cache = new DeviceIndex
        {
            Device = device,
            Committed = new IndexScan
            {
                Status = "Complete",
                Firmware = "12.00",
                FinishedAt = DateTimeOffset.UtcNow,
                Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot,
                    slot => slot == 0 ? before : slot == 1 ? other : FractalIndexWorkflowTests.Preset(slot))
            }
        };
        library.Save(cache);
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        var tags = profile.GetOrCreate(device.Id, before, "12.00");
        tags.SceneTags[0] = ["Rhythm"]; tags.SceneTags[3] = ["Lead"]; tags.Tags = ["Live"];
        var favorites = new List<Favorite> { new() { Id = 1, Slot = 1, Name = "First scene", Preset = 1, Scene = 1 } };
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        settings.FractalIndex = IndexJson.ToElement(profile);
        settings.PresetNameCache[0] = before.Name; settings.PresetNameCache[1] = other.Name;
        settings.SceneNameCaches[JsonSerializer.Serialize(new[] { "FM9", "FM9" })] = new()
        {
            [0] = new() { Source = "stored", Names = before.SceneNames },
            [1] = new() { Source = "stored", Names = other.SceneNames }
        };
        store.SaveSettingsAndFavorites(settings, favorites);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = width == 1000 ? 640 : 850 };
        var session = new Session(window, settings, midi, store, library, device);
        midi.Send = request =>
        {
            if (request.SequenceEqual(Fm9ActivePreset.BuildQuery())) { midi.Reply(Fm9ActivePresetTests.Response(session.Live)); }
            else if (request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [0])); }
            else { throw new InvalidOperationException("Unexpected device request: " + SysexProtocol.ToHex(request)); }
        };
        var source = new SceneTagMoveTests.Source((slot, _, token) =>
        {
            session.Reads.Add(slot);
            return session.Read?.Invoke(slot, token) ?? Task.FromResult(slot == 0 ? session.Saved : SceneTagMoveTests.Image(slot));
        });
        Set(window, "_fractalIndexReader", new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()));
        window.ConnectionState.Device = new(DeviceModel.FM9, null, "12.00");
        window.Show();
        window.Activate();
        ((Action?)window.PlatformImpl!.GetType().GetProperty("Activated")!.GetValue(window.PlatformImpl))?.Invoke();
        return session;
    }

    [AvaloniaFact]
    public async Task UnsavedSwapDoesNotChangeNamesOrTagsThenSavedSwapMovesBothSidesOnce()
    {
        var s = await Setup();
        try
        {
            await s.Poll();
            string before = JsonSerializer.Serialize(s.Library.Load(s.Device.Id), IndexJson.Options);
            s.Live = LiveFor(SceneTagMoveTests.Image(swapped: true), edited: true);
            await s.Poll();
            Assert.Empty(s.Reads); Assert.Equal(before, JsonSerializer.Serialize(s.Library.Load(s.Device.Id), IndexJson.Options));
            Assert.Equal(["Rhythm"], s.Tags.SceneTags[0]);
            Assert.Equal("Mode 1", s.Settings.SceneNameCaches.Values.Single()[0].Names[0]);
            s.Saved = SceneTagMoveTests.Image(swapped: true); s.Live = LiveFor(s.Saved);
            await s.Poll(); await s.Poll();
            Assert.True(s.Reads.Count > 0, s.Log);
            Assert.Equal([0], s.Reads); Assert.Equal("Greasy Mode 1", s.Preset.SceneNames[0]);
            Assert.Equal(["Lead"], s.Tags.SceneTags[0]); Assert.Equal(["Rhythm"], s.Tags.SceneTags[3]);
            Assert.Equal(["Live"], s.Tags.Tags);
            Assert.Equal(["Lead"], s.Store.LoadFavorites("Default").Single().Tags);
            Assert.Empty(s.Midi.Writes); Assert.Empty(s.Window.OwnedWindows);
        }
        finally { s.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigatingAwayReadsWhatWasSavedAndNeverCommitsDiscardedLiveNames(bool saved)
    {
        var s = await Setup();
        try
        {
            await s.Poll();
            s.Live = LiveFor(SceneTagMoveTests.Image(swapped: true), edited: true); await s.Poll();
            if (saved) { s.Saved = SceneTagMoveTests.Image(swapped: true); }
            s.Live = LiveFor(SceneTagMoveTests.Image(1)); await s.Poll();
            Assert.Equal([0], s.Reads);
            Assert.Equal(saved ? "Greasy Mode 1" : "Mode 1", s.Preset.SceneNames[0]);
            Assert.Equal([saved ? "Lead" : "Rhythm"], s.Tags.SceneTags[0]);
            Assert.Equal([saved ? "Rhythm" : "Lead"], s.Tags.SceneTags[3]);
            s.Live = LiveFor(s.Saved); await s.Poll();
            Assert.Equal([0], s.Reads); Assert.Empty(s.Midi.Writes);
        }
        finally { s.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task SavedPresetAndSceneRenamesBetweenPollsRefreshOnlyThatPresetAndRetainTags()
    {
        var s = await Setup();
        try
        {
            await s.Poll();
            var image = SceneTagMoveTests.Image();
            image.Body.AsSpan(4, 32).Clear(); Encoding.ASCII.GetBytes("Renamed scene").CopyTo(image.Body, 4);
            image.RawImage.AsSpan(8, 32).Clear(); Encoding.ASCII.GetBytes("Renamed preset").CopyTo(image.RawImage, 8);
            string[] names = image.Scenes.Names.ToArray(); names[0] = "Renamed scene";
            s.Saved = image with { Scenes = new(0, "Renamed preset", names) }; s.Live = LiveFor(s.Saved);
            await s.Poll();
            Assert.Equal([0], s.Reads); Assert.Equal("Renamed preset", s.Preset.Name); Assert.Equal("Renamed scene", s.Preset.SceneNames[0]);
            Assert.Equal("Renamed preset", s.Store.LoadSettings().PresetNameCache[0]);
            Assert.Equal(["Rhythm"], s.Tags.SceneTags[0]); Assert.Equal(["Lead"], s.Tags.SceneTags[3]);
        }
        finally { s.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReadFailureKeepsNamesAndTagsAndCanRetry() => await ReadOrPersistenceFailureKeepsNamesAndTagsAndCanRetry(false);

    [WindowsFileLockAvaloniaFact]
    public async Task PersistenceFailureKeepsNamesAndTagsAndCanRetry() => await ReadOrPersistenceFailureKeepsNamesAndTagsAndCanRetry(true);

    private async Task ReadOrPersistenceFailureKeepsNamesAndTagsAndCanRetry(bool persistence)
    {
        var s = await Setup();
        try
        {
            await s.Poll();
            var old = s.Preset;
            s.Saved = SceneTagMoveTests.Image(swapped: true); s.Live = LiveFor(s.Saved);
            FileStream? locked = null;
            if (persistence)
            {
                s.Library.Save(s.Library.Load(s.Device.Id)!);
                locked = new(Path.Combine(s.Library.DirectoryPath, s.Device.Id.ToString("N") + ".json.bak"), FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            else { s.Read = (_, _) => Task.FromException<StoredPresetImage>(new IOException("Test read failure")); }
            try { await s.Poll(); }
            finally { locked?.Dispose(); }
            Assert.Equal(old.SceneNames, s.Preset.SceneNames); Assert.Equal(["Rhythm"], s.Tags.SceneTags[0]);
            Assert.Equal("Mode 1", s.Settings.SceneNameCaches.Values.Single()[0].Names[0]);
            s.Read = null; Set(s.Window, "_savedNamesRetryAt", 0L);
            await s.Poll();
            Assert.Equal(["Lead"], s.Tags.SceneTags[0]);
        }
        finally { s.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectOrProfileChangeRejectsALateSavedReply(bool profileChange)
    {
        var s = await Setup();
        try
        {
            await s.Poll();
            var reply = new TaskCompletionSource<StoredPresetImage>();
            s.Read = (_, _) => reply.Task;
            s.Live = LiveFor(SceneTagMoveTests.Image(swapped: true));
            var poll = s.Poll();
            await WaitFor(() => s.Reads.Count > 0);
            if (profileChange) { Set(s.Window, "_profileGeneration", 1); }
            else { typeof(MainWindow).GetMethod("Disconnect", Fields)!.Invoke(s.Window, null); }
            reply.SetResult(SceneTagMoveTests.Image(swapped: true)); await poll;
            Assert.Equal("Mode 1", s.Preset.SceneNames[0]); Assert.Equal(["Rhythm"], s.Tags.SceneTags[0]);
            Assert.Equal("Mode 1", s.Settings.SceneNameCaches.Values.Single()[0].Names[0]);
        }
        finally { s.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light", 1440, false)]
    [InlineData("Dark", 1440, true)]
    [InlineData("Light", 1000, true)]
    [InlineData("Dark", 1000, false)]
    public async Task NameOnlyMovesAskAndCancelDoesNotRepeatUntilAnotherEdit(string theme, int width, bool cancel)
    {
        var s = await Setup(legacy: true, theme, width);
        try
        {
            await s.Poll();
            s.Window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "NavPresetIndex")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var origin = s.Window.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "IndexSearchInput");
            origin.Focus();
            s.Saved = SceneTagMoveTests.Image(swapped: true); s.Live = LiveFor(s.Saved);
            var polling = s.Poll();
            await WaitFor(() => s.Window.OwnedWindows.Any());
            var dialog = s.Window.OwnedWindows.Single();
            Assert.Equal("Should tags follow these scenes?", dialog.Title);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output); Dispatcher.UIThread.RunJobs();
                using var rendered = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(dialog);
                rendered!.Save(Path.Combine(output, $"saved-scene-decision-{theme}-{width}.png"));
            }
            var button = dialog.GetVisualDescendants().OfType<Button>().Single(c => c.Name == (cancel ? "SceneTagMoveCancel" : "SceneTagMoveFollow"));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await polling; await s.Poll();
            Assert.Empty(s.Window.OwnedWindows);
            Assert.Same(origin, s.Window.FocusManager?.GetFocusedElement());
            Assert.Equal(cancel ? [0] : [0, 0], s.Reads);
            Assert.Equal([cancel ? "Rhythm" : "Lead"], s.Tags.SceneTags[0]);
            Assert.Equal(cancel ? "Mode 1" : "Greasy Mode 1", s.Preset.SceneNames[0]);
        }
        finally { s.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task UncertainMoveWaitsForAppFocusAndDisconnectCancelsTheDecision()
    {
        var s = await Setup(legacy: true);
        try
        {
            await s.Poll();
            Dispatcher.UIThread.RunJobs();
            ((Action?)s.Window.PlatformImpl!.GetType().GetProperty("Deactivated")!.GetValue(s.Window.PlatformImpl))?.Invoke();
            s.Saved = SceneTagMoveTests.Image(swapped: true); s.Live = LiveFor(s.Saved);
            await s.Poll().WaitAsync(TimeSpan.FromSeconds(5)); await s.Poll().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(s.Window.OwnedWindows); Assert.Equal([0], s.Reads);
            Assert.Equal("Mode 1", s.Preset.SceneNames[0]); Assert.Equal(["Rhythm"], s.Tags.SceneTags[0]);
            ((Action?)s.Window.PlatformImpl!.GetType().GetProperty("Activated")!.GetValue(s.Window.PlatformImpl))?.Invoke();
            var polling = s.Poll();
            await WaitFor(() => s.Window.OwnedWindows.Any());
            typeof(MainWindow).GetMethod("Disconnect", Fields)!.Invoke(s.Window, null);
            Dispatcher.UIThread.RunJobs();
            await polling.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(s.Window.OwnedWindows); Assert.Equal("Mode 1", s.Preset.SceneNames[0]);
            Assert.Equal(["Rhythm"], s.Tags.SceneTags[0]);
        }
        finally { s.Window.Close(); }
    }
}
