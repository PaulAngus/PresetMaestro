using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class SyncDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-timing-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    [Fact]
    public async Task NormalLoggingKeepsSummaryAndMissingSlotWithoutVerboseSuccesses()
    {
        var log = new SyncDiagnosticLog(_directory);
        using (var session = log.Begin("quick-check", false, "test", "FM9", "12.00"))
        {
            var names = new PresetNameReadSession([]);
            await names.ReadAsync((slot, _, _) => slot == 170 ? Task.FromException<PresetNameResult>(new TimeoutException("private text"))
                : Task.FromResult(new PresetNameResult(slot, "Private preset name")), _ => { }, default);
            session.Result("names-incomplete", names.CheckedSlots, errors: 512 - names.RefreshedSlots.Count);
        }
        await log.DisposeAsync();
        string text = File.ReadAllText(Path.Combine(_directory, "sync.jsonl"));
        Assert.DoesNotContain("Private preset", text); Assert.DoesNotContain("private text", text);
        var records = Records();
        Assert.Equal(5, records.Length);
        var timing = records.First(r => r.GetProperty("kind").GetString() == "timing").GetProperty("timing");
        Assert.Equal(170, timing.GetProperty("Slot").GetInt32());
        Assert.Equal("timeout", timing.GetProperty("Outcome").GetString());
        var summary = records.Single(r => r.GetProperty("kind").GetString() == "summary");
        Assert.Equal(1, summary.GetProperty("errors").GetInt32());
        Assert.Equal(513, summary.GetProperty("phases").GetProperty("names.query").GetProperty("Count").GetInt32());
    }

    [Fact]
    public async Task DetailedLoggingRecordsRecoveredReadAndCheckpointTimings()
    {
        var log = new SyncDiagnosticLog(_directory);
        var source = new RecoveringSource();
        using (var session = log.Begin("sync", true, "test", "FM9", null))
        {
            var library = new IndexLibrary(Path.Combine(_directory, "index"));
            var reader = new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter());
            var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Private device", FractalDeviceVariant.FM9, "12.00") };
            await new IndexScanner(reader, library).ScanAsync(cache, false, null, default);
            Assert.Equal("Complete", cache.LastAttempt!.Status);
            Assert.Equal(2, source.Dumps);
            session.Result("complete", cache.LastAttempt.Presets.Count, errors: cache.LastAttempt.Errors.Count);
        }
        await log.DisposeAsync();
        var timings = Records().Where(r => r.GetProperty("kind").GetString() == "timing").Select(r => r.GetProperty("timing")).ToArray();
        Assert.Contains(timings, t => t.GetProperty("Phase").GetString() == "preset.retry" && t.GetProperty("Slot").GetInt32() == 58);
        Assert.Contains(timings, t => t.GetProperty("Phase").GetString() == "preset.dump" && t.GetProperty("Outcome").GetString() == "timeout");
        Assert.Equal(512, timings.Count(t => t.GetProperty("Phase").GetString() == "scan.checkpoint"));
        Assert.Contains(timings, t => t.GetProperty("Phase").GetString() == "scan.read-loop");
        Assert.DoesNotContain("Private device", File.ReadAllText(Path.Combine(_directory, "sync.jsonl")));
    }

    [Fact]
    public async Task MidiTimeoutAndQuietPeriodAreDistinguishedFromCancellation()
    {
        var timings = new List<ReadTiming>();
        using var capture = ReadDiagnostics.Capture(timings.Add);
        var midi = new DeviceMidi();
        using var client = new PresetNameClient(midi);
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync(170, TimeSpan.FromMilliseconds(300), default));
        midi.Send = _ => midi.Reply(FractalDeviceInformationTests.Frame(0x12, 0x0d, [43, 1, .. new byte[32]]));
        var result = await client.QueryAsync(171, TimeSpan.FromSeconds(1), default);
        Assert.Equal(171, result.Slot);
        Assert.Contains(timings, t => t.Phase == "midi.reply" && t.Slot == 170 && t.Outcome == "timeout");
        Assert.Contains(timings, t => t.Phase == "midi.quiet" && t.Slot == 171 && t.Outcome == "complete");
        Assert.Equal(2, midi.Sent.Count);
        using var cancellation = new CancellationTokenSource();
        midi.Send = _ => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync(172, TimeSpan.FromSeconds(1), cancellation.Token));
        Assert.Contains(timings, t => t.Phase == "midi.exchange" && t.Slot == 172 && t.Outcome == "cancelled");
    }

    [Fact]
    public async Task LoggingFailureDoesNotChangeReadResult()
    {
        Directory.CreateDirectory(_directory);
        string blocked = Path.Combine(_directory, "file"); File.WriteAllText(blocked, "occupied");
        var log = new SyncDiagnosticLog(blocked);
        using (var session = log.Begin("sync", true, "test", "FM9", null))
        { Assert.Equal(42, await ReadDiagnostics.MeasureAsync("names.query", 42, () => Task.FromResult(42))); session.Result("complete"); }
        await log.DisposeAsync();
        Assert.NotNull(log.WriteFailure);
        using var capture = ReadDiagnostics.Capture(_ => throw new IOException("Logger failed"));
        Assert.Equal(42, await ReadDiagnostics.MeasureAsync("names.query", 42, () => Task.FromResult(42)));
    }

    [Theory]
    [InlineData("none", 0, 0, 0)]
    [InlineData("wrong-slot", 1, 1, 0)]
    [InlineData("bad-checksum", 1, 1, 1)]
    public async Task TimeoutEvidenceDistinguishesAbsentAndRejectedReplies(string response, int frames, int rejected, int invalid)
    {
        var timings = new List<ReadTiming>();
        using var capture = ReadDiagnostics.Capture(timings.Add);
        var midi = new DeviceMidi();
        midi.Send = _ =>
        {
            if (response == "none") { return; }
            var frame = FractalDeviceInformationTests.Frame(0x12, 0x0d, [43, 1, .. new byte[32]]);
            if (response == "bad-checksum") { frame[^2] ^= 1; }
            midi.Reply(frame);
        };
        using var client = new PresetNameClient(midi);
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync(170, TimeSpan.FromMilliseconds(30), default));
        var evidence = Assert.Single(timings, t => t.Phase == "midi.receive");
        Assert.Equal("timeout", evidence.Outcome);
        Assert.NotNull(evidence.Receive);
        Assert.Equal(0x0d, evidence.Receive.Opcode);
        Assert.Equal(frames, evidence.Receive.FramesReceived);
        Assert.Equal(rejected, evidence.Receive.RejectedMatchingFrames);
        Assert.Equal(invalid, evidence.Receive.InvalidMatchingFrames);
    }

    [Fact]
    public async Task ReceiveQueueDropsAreCountedWithoutRejectingFreshReply()
    {
        var timings = new List<ReadTiming>();
        using var capture = ReadDiagnostics.Capture(timings.Add);
        var midi = new DeviceMidi();
        midi.Send = _ =>
        {
            for (int i = 0; i < 100; i++) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 0x0d, [43, 1, .. new byte[32]])); }
            midi.Reply(FractalDeviceInformationTests.Frame(0x12, 0x0d, [42, 1, .. new byte[32]]));
        };
        using var client = new PresetNameClient(midi);
        Assert.Equal(170, (await client.QueryAsync(170, TimeSpan.FromSeconds(1), default)).Slot);
        var evidence = Assert.Single(timings, t => t.Phase == "midi.receive").Receive!;
        Assert.Equal(37, evidence.QueueDrops);
        Assert.Equal(64, evidence.FramesReceived);
    }

    [Fact]
    public async Task RotationKeepsThreeBoundedFilesAndValidRecords()
    {
        var log = new SyncDiagnosticLog(_directory, 2048);
        using (var session = log.Begin("sync", true, "test", "FM9", null))
        { for (int i = 0; i < 100; i++) { ReadDiagnostics.Record("names.query", i, 1); } session.Result("complete", 100); }
        await log.DisposeAsync();
        var files = Directory.GetFiles(_directory);
        Assert.Equal(3, files.Length);
        foreach (string file in files)
        {
            Assert.InRange(new FileInfo(file).Length, 1, 2048);
            foreach (string line in File.ReadLines(file)) { using var document = JsonDocument.Parse(line); }
        }
        Assert.Contains(Records(), r => r.GetProperty("kind").GetString() == "summary");
    }

    [Fact]
    public async Task ParallelScopesDoNotMixAndShutdownKeepsFinalSummary()
    {
        var log = new SyncDiagnosticLog(_directory);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(async () =>
        {
            using var session = log.Begin("op" + i, true, "test", "FM9", null);
            await Task.Yield();
            await ReadDiagnostics.MeasureAsync("names.query", i, () => Task.FromResult(i));
            session.Result("complete", 1);
        })));
        var final = log.Begin("closing", false, "test", "FM9", null);
        var shutdown = log.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        final.Result("cancelled"); final.Dispose();
        await shutdown;
        var records = Records();
        foreach (int i in Enumerable.Range(0, 2))
        {
            string? id = records.Single(r => r.GetProperty("kind").GetString() == "start" && r.GetProperty("operation").GetString() == "op" + i)
                .GetProperty("operationId").GetString();
            var timing = Assert.Single(records, r => r.GetProperty("kind").GetString() == "timing" && r.GetProperty("operationId").GetString() == id);
            Assert.Equal(i, timing.GetProperty("timing").GetProperty("Slot").GetInt32());
        }
        Assert.Contains(records, r => r.GetProperty("kind").GetString() == "summary" && r.GetProperty("status").GetString() == "cancelled");
    }

    [AvaloniaTheory]
    [InlineData("Light", 1000)]
    [InlineData("Dark", 1000)]
    [InlineData("Light", 1440)]
    [InlineData("Dark", 1440)]
    public void TimingSettingPersistsWithoutChangingMidiFilter(string theme, int width)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store) { Width = width };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Find<Button>("OpenDiagnostics").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var toggle = Find<ToggleSwitch>("DetailedSyncTiming");
            toggle.Focus();
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Space, Avalonia.Input.RawInputModifiers.None);
            window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Space, Avalonia.Input.RawInputModifiers.None);
            Assert.True(settings.DetailedSyncTiming);
            Assert.True(store.LoadSettings().DetailedSyncTiming);
            Assert.False(settings.DebugMode);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"sync-diagnostics-{width}-{theme}");
        }
        finally { window.Close(); }
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    }

    private JsonElement[] Records() => File.ReadAllLines(Path.Combine(_directory, "sync.jsonl"))
        .Select(line => { using var document = JsonDocument.Parse(line); return document.RootElement.Clone(); }).ToArray();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SceneStatusFailuresArePersistedButSuccessfulPollsAreQuiet(bool failure)
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-fm9")); }
            if (request[5] == 0x0d) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 0x0d, [0, 0, .. new byte[32]])); }
            if (request[5] == 0x0c && !failure) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 0x0c, [0])); }
        };
        var window = new MainWindow(new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9" }, [], midi,
            saveSettings: _ => { }, saveFavorites: _ => { }, syncLogDirectory: _directory)
        { ThruInputRetryDelay = TimeSpan.Zero };
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            typeof(MainWindow).GetField("_sceneClosing", fields)!.SetValue(window, true);
            window.Show(); await window.ConnectAsync();
            ((DispatcherTimer)typeof(MainWindow).GetField("_scenePoll", fields)!.GetValue(window)!).Stop();
            typeof(MainWindow).GetField("_sceneClosing", fields)!.SetValue(window, false);
            typeof(MainWindow).GetField("_sceneSlot", fields)!.SetValue(window, 0);
            await (Task)typeof(MainWindow).GetMethod("PollSceneStateAsync", fields)!.Invoke(window, null)!;
            var logger = (SyncDiagnosticLog?)typeof(MainWindow).GetField("_syncDiagnosticLog", fields)!.GetValue(window);
            if (failure)
            {
                Assert.NotNull(logger); await logger.DisposeAsync();
                var record = Assert.Single(Records());
                Assert.Equal("background-failure", record.GetProperty("kind").GetString());
                Assert.Equal("scene-status", record.GetProperty("operation").GetString());
                Assert.Contains(record.GetProperty("timings").EnumerateArray(), t =>
                    t.GetProperty("Phase").GetString() == "state.scene" && t.GetProperty("Outcome").GetString() == "timeout");
            }
            else { Assert.Null(logger); Assert.False(Directory.Exists(_directory)); }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InjectedWindowLogsOnlyWhenAnIsolatedFolderIsSupplied(bool logging)
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-fm9")); }
        };
        var window = new MainWindow(new AppSettings { MidiInputPort = "FM9", MidiOutputPort = "FM9" }, [], midi,
            queryPresetNameAsync: (slot, _, _) => Task.FromResult(new PresetNameResult(slot, "Synthetic")),
            saveSettings: _ => { }, saveFavorites: _ => { }, syncLogDirectory: logging ? _directory : null)
        { ThruInputRetryDelay = TimeSpan.Zero };
        try
        {
            window.Show();
            await window.ConnectAsync();
            await window.SyncPresetNamesAsync();
            var logger = (SyncDiagnosticLog)typeof(MainWindow).GetField("_syncDiagnosticLog",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            await logger.DisposeAsync();
            Assert.Null(logger.WriteFailure);
            Assert.Equal(logging, Directory.Exists(_directory));
            if (logging) { Assert.Contains(Records(), r => r.GetProperty("kind").GetString() == "summary"); }
        }
        finally { window.Close(); }
    }

    private sealed class RecoveringSource : IStoredPresetImageSource, IStoredPresetNameSource
    {
        private readonly FractalIndexWorkflowTests.NameSource _inner = new(_ => "<EMPTY>");
        internal int Dumps { get; private set; }
        public Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token) =>
            slot == 58 ? Task.FromException<string?>(new TimeoutException()) : _inner.ReadStoredPresetNameAsync(slot, device, token);
        public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token) =>
            ++Dumps == 1 ? Task.FromException<StoredPresetImage>(new TimeoutException()) : _inner.ReadStoredImageAsync(slot, device, token);
    }
}
