using System.Buffers.Binary;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class LibraryCheckDetailsTests
{
    [AvaloniaTheory]
    [InlineData("Light", 1440, false)]
    [InlineData("Dark", 1440, false)]
    [InlineData("Light", 1000, true)]
    [InlineData("Dark", 1000, true)]
    public async Task FailedCheckShowsActualDifferencesAndKeepsBaselineAndNames(string theme, int width, bool legacy)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-review-" + Guid.NewGuid().ToString("N"));
        var source = new ChangedSource(!legacy);
        var reader = new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter());
        var device = new IndexDevice(Guid.NewGuid(), "Saved rig", FractalDeviceVariant.FM9, "12.00");
        var scan = new IndexScan { Status = "Complete", Firmware = "12.00", ConnectedDeviceName = "Stage", StartedAt = DateTimeOffset.UtcNow.AddDays(-2), FinishedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        for (int slot = 0; slot < 512; slot++)
        {
            var preset = await reader.ReadAsync(FractalDeviceDefinition.For(device.Variant), new Version(12, 0), slot, default);
            scan.Presets[slot] = legacy ? preset with { BypassIgnoredSha256 = null } : preset;
        }
        var cache = new DeviceIndex { Device = device, Committed = scan };
        var library = new IndexLibrary(Path.Combine(directory, "FractalIndex")); library.Save(cache);
        var store = new ProfileStore(directory); var settings = store.LoadSettings();
        settings.Theme = theme; settings.MidiInputPort = "FM9"; settings.MidiOutputPort = "FM9";
        settings.PresetNameCache[1] = "Previous cached name";
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.SyntheticName("Stage")); }
        };
        var window = new MainWindow(settings, [], midi, profileStore: store,
            queryPresetNameAsync: (slot, _, _) => Task.FromResult(new PresetNameResult(slot, scan.Presets[slot].Name)))
        { Width = width, Height = 850, ThruInputRetryDelay = TimeSpan.Zero };
        typeof(MainWindow).GetField("_fractalIndexReader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, reader);
        window.Show();
        try
        {
            await window.ConnectAsync();
            source.Changed = true;
            await window.CheckAssignedLibraryAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Library check needs review", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.Contains("15 of 16", Find<TextBlock>(dialog, "ConnectionSyncStatus").Text);
            var details = Find<StackPanel>(dialog, "ConnectionSyncDifferences");
            Assert.True(details.IsEffectivelyVisible);
            Assert.Equal("001  Fixture 1", Find<TextBlock>(dialog, "ConnectionSyncDifferencePreset").Text);
            var labels = details.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(labels, text => text?.Contains("Saved library:", StringComparison.Ordinal) == true);
            Assert.Contains(labels, text => text?.Contains("previous sample", StringComparison.Ordinal) == true);
            Assert.Contains(labels, text => text?.Contains(legacy ? "older library includes bypass" : "saved preset image differs", StringComparison.OrdinalIgnoreCase) == true);
            if (!legacy)
            {
                Assert.Contains("Scene 1 name", labels);
                Assert.Contains("Connected: Lead", labels);
                Assert.Contains("Scene 1, Amp 1 channel", labels);
                Assert.Contains("Connected: Channel C", labels);
            }
            Assert.Equal(scan.Id, library.Load(device.Id)!.Committed!.Id);
            Assert.Equal(scan.Presets[1].ContentSha256, library.Load(device.Id)!.Committed!.Presets[1].ContentSha256);
            Assert.Equal("Previous cached name", store.LoadSettings().PresetNameCache[1]);
            string report = File.ReadAllText(Path.Combine(library.DirectoryPath, "Checks", device.Id.ToString("N") + ".json"));
            Assert.Contains("RequestedSlots", report);
            Assert.Contains("ConnectedFingerprint", report);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = dialog.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, $"library-differences-{width}-{theme}.png"));
            }
            await window.SyncPresetNamesAsync(); Dispatcher.UIThread.RunJobs();
            Assert.False(details.IsEffectivelyVisible); // A names-only result must not inherit old check differences.
            Find<Button>(dialog, "ConnectionSyncDone").Focus();
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private sealed class ChangedSource(bool fields) : IStoredPresetImageSource, IStoredPresetNameSource
    {
        private readonly FractalIndexWorkflowTests.NameSource _inner = new(slot => slot < 16 ? "Populated" : "<EMPTY>", withAmp: true);
        public bool Changed { get; set; }
        public Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => _inner.ReadStoredPresetNameAsync(slot, device, token);
        public async Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
        {
            var image = await _inner.ReadStoredImageAsync(slot, device, token);
            if (!Changed || slot != 1) { return image; }
            image.RawImage[8] = 1;
            if (!fields) { return image; }
            BinaryPrimitives.WriteUInt16LittleEndian(image.Body.AsSpan(0x2be, 2), 2);
            return image with { Scenes = image.Scenes with { Names = ["Lead", .. image.Scenes.Names.Skip(1)] } };
        }
    }
}
