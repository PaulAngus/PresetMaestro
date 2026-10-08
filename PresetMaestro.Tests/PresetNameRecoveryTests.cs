using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class PresetNameRecoveryTests
{
    [Fact]
    public async Task RetriesOnlyMissingNamesAndUsesFreshReplies()
    {
        var attempts = new int[512];
        var read = new PresetNameReadSession(Enumerable.Range(0, 512).ToDictionary(i => i, _ => "Cached"));
        await read.ReadAsync((slot, _, _) => slot is 170 or 220 && ++attempts[slot] == 1
            ? Task.FromException<PresetNameResult>(new TimeoutException())
            : Reply(slot), _ => { }, default);
        Assert.Equal(512, read.RefreshedSlots.Count);
        Assert.Equal(2, attempts[170]); Assert.Equal(2, attempts[220]);
        Assert.Equal("Fresh 170", read.Names[170]);
        Assert.Equal(514, attempts.Sum());

        Task<PresetNameResult> Reply(int slot)
        {
            if (slot is not (170 or 220)) { attempts[slot]++; }
            return Task.FromResult(new PresetNameResult(slot, "Fresh " + slot));
        }
    }

    [Fact]
    public async Task PersistentMissingReplyKeepsCachedNameAndRemainsUnconfirmed()
    {
        var attempts = new int[512];
        var read = new PresetNameReadSession(new() { [170] = "Cached" });
        await read.ReadAsync((slot, _, _) =>
        {
            attempts[slot]++;
            return slot == 170 ? Task.FromException<PresetNameResult>(new TimeoutException())
                : Task.FromResult(new PresetNameResult(slot, "Fresh"));
        }, _ => { }, default);
        Assert.Equal(511, read.RefreshedSlots.Count);
        Assert.DoesNotContain(170, read.RefreshedSlots);
        Assert.Equal("Cached", read.Names[170]);
        Assert.Equal(513, attempts.Sum());
    }

    [Fact]
    public async Task LostConnectionStopsAfterThreeRequests()
    {
        int requests = 0;
        var read = new PresetNameReadSession([]);
        await Assert.ThrowsAsync<IOException>(() => read.ReadAsync((_, _, _) =>
        { requests++; return Task.FromException<PresetNameResult>(new TimeoutException()); }, _ => { }, default));
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task CancellationDuringRecoveryStopsFurtherRequests()
    {
        using var cancellation = new CancellationTokenSource();
        int requests = 0;
        var read = new PresetNameReadSession([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.ReadAsync((slot, _, _) =>
        {
            if (++requests == 513) { cancellation.Cancel(); }
            return slot == 170 ? Task.FromException<PresetNameResult>(new TimeoutException())
                : Task.FromResult(new PresetNameResult(slot, "Fresh"));
        }, _ => { }, cancellation.Token));
        Assert.Equal(513, requests);
        Assert.DoesNotContain(170, read.RefreshedSlots);
    }

    [AvaloniaTheory]
    [InlineData("Light", 1000, false)]
    [InlineData("Dark", 1440, false)]
    [InlineData("Light", 1440, true)]
    [InlineData("Dark", 1000, true)]
    public async Task ReconnectAfterFullSyncRecoversTransientNameFailureOrExplainsPersistentFailure(string theme, int width, bool persistent)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-name-recovery-" + Guid.NewGuid().ToString("N"));
        var source = new FractalIndexWorkflowTests.NameSource(slot => slot < 16 ? "Populated" : "<EMPTY>");
        var reader = new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter());
        var library = new IndexLibrary(Path.Combine(directory, "FractalIndex"));
        var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Saved device", FractalDeviceVariant.FM9, "12.00") };
        await new IndexScanner(reader, library).ScanAsync(cache, false, null, default);
        Guid baselineId = cache.Committed!.Id;
        source.FullReads.Clear();
        var store = new ProfileStore(directory); var settings = store.LoadSettings();
        settings.Theme = theme; settings.MidiInputPort = "FM9"; settings.MidiOutputPort = "FM9";
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id });
        settings.PresetNameCache = cache.Committed.Presets.ToDictionary(p => p.Key, p => p.Value.Name);
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-fm9")); }
        };
        int nameReads = 0, missingAttempts = 0;
        var window = new MainWindow(settings, [], midi, profileStore: store, queryPresetNameAsync: (slot, _, _) =>
        {
            Interlocked.Increment(ref nameReads);
            if (slot == 170 && (++missingAttempts == 1 || persistent)) { throw new TimeoutException(); }
            return Task.FromResult(new PresetNameResult(slot, cache.Committed.Presets[slot].Name));
        })
        { Width = width, Height = 850, ThruInputRetryDelay = TimeSpan.Zero };
        typeof(MainWindow).GetField("_fractalIndexReader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(window, reader);
        try
        {
            window.Show();
            await window.ConnectAsync();
            await window.CheckAssignedLibraryAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(513, nameReads);
            Assert.Equal(2, missingAttempts);
            Assert.Equal(baselineId, library.Load(cache.Device.Id)!.Committed!.Id);
            if (persistent)
            {
                Assert.Empty(source.FullReads);
                var dialog = Assert.Single(window.OwnedWindows);
                Assert.Equal("Preset-name check incomplete", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
                Assert.Contains("Missing replies were retried", Find<TextBlock>(dialog, "ConnectionSyncStatus").Text);
                Assert.Contains("A full sync is optional", Find<TextBlock>(dialog, "ConnectionSyncNext").Text);
                Capture(dialog, $"name-retry-incomplete-{width}-{theme}");
            }
            else
            {
                Assert.Equal(16, source.FullReads.Count);
                Assert.Empty(window.OwnedWindows);
                Assert.DoesNotContain("not checked", Find<TextBlock>(window, "HeaderConnectionStatus").Text);
                Capture(window, $"name-retry-success-{width}-{theme}");
            }
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Capture(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
        if (output is null) { return; }
        Directory.CreateDirectory(output); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var screenshot = window.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, name + ".png"));
    }
}
