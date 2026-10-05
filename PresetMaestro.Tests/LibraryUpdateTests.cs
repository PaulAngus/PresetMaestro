using System.Buffers.Binary;
using System.Reflection;
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

public sealed class LibraryUpdateTests
{
    [AvaloniaTheory]
    [InlineData("Light", 1440, false)]
    [InlineData("Dark", 1440, true)]
    [InlineData("Light", 1000, true)]
    [InlineData("Dark", 1000, false)]
    public async Task UpdateDecisionIsOwnedBySyncAndReviewDoesNotChangeData(string theme, int width, bool accept)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-update-" + Guid.NewGuid().ToString("N"));
        var source = new ChangedSource();
        var reader = new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter());
        var device = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9, "12.00");
        var scan = new IndexScan { Status = "Complete", Firmware = "12.00", ConnectedDeviceName = "Stage", StartedAt = DateTimeOffset.UtcNow.AddDays(-2), FinishedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        for (int slot = 0; slot < 512; slot++)
        { scan.Presets[slot] = await reader.ReadAsync(FractalDeviceDefinition.For(device.Variant), new Version(12, 0), slot, default); }
        var library = new IndexLibrary(Path.Combine(directory, "FractalIndex"));
        library.Save(new DeviceIndex { Device = device, Committed = scan });
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
        { Width = width, Height = width == 1000 ? 680 : 850, ThruInputRetryDelay = TimeSpan.Zero };
        typeof(MainWindow).GetField("_fractalIndexReader", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, reader);
        window.Show();
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var sync = Assert.Single(window.OwnedWindows);
            source.Changed = true;
            var pending = window.SyncIndexAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!sync.OwnedWindows.Any()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, deadline.Token); }
            var dialog = Assert.Single(sync.OwnedWindows);
            Assert.Single(window.OwnedWindows); // The confirmation belongs to sync, not its sibling.
            Assert.Equal("3 presets don't match.", Find<TextBlock>(dialog, "LibraryUpdateSummary").Text);
            Assert.DoesNotContain(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains('%') == true);
            Assert.False(pending.IsCompleted);
            Assert.Equal(scan.Id, library.Load(device.Id)!.Committed!.Id);
            Assert.Equal("Previous cached name", store.LoadSettings().PresetNameCache[1]);
            var view = Find<Button>(dialog, "LibraryUpdateViewDifferences");
            Assert.Same(view, dialog.FocusManager!.GetFocusedElement());
            dialog.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Same(Find<Button>(dialog, "LibraryUpdateAccept"), dialog.FocusManager.GetFocusedElement());
            int reads = source.FullReadCount;
            Capture("decision");
            Click(view); Dispatcher.UIThread.RunJobs();
            Assert.Empty(dialog.OwnedWindows); // Review uses this window, without another nested dialog.
            Assert.True(Find<ScrollViewer>(dialog, "LibraryUpdateDifferencesScroll").IsEffectivelyVisible);
            Assert.False(Find<StackPanel>(dialog, "LibraryUpdateDecision").IsVisible);
            var labels = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("001  Fixture 1", labels);
            Assert.Contains("009  Fixture 9", labels);
            Assert.Contains("053  Fixture 53", labels);
            Assert.Contains("Scene 1 name", labels);
            Assert.Contains("Connected: Lead", labels);
            Assert.Contains("Scene 1, Amp 1 channel", labels);
            Assert.Contains(labels, text => text?.Contains("Exact change unavailable", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(labels, text => text?.Contains('%') == true);
            Assert.Equal(reads, source.FullReadCount);
            Assert.Equal(scan.Id, library.Load(device.Id)!.Committed!.Id);
            var scroll = Find<ScrollViewer>(dialog, "LibraryUpdateDifferencesScroll");
            if (width == 1000) { Assert.True(scroll.Extent.Height > scroll.Viewport.Height); }
            var back = Find<Button>(dialog, "LibraryUpdateBack");
            var position = back.TranslatePoint(default, dialog);
            scroll.Offset = new(0, scroll.Extent.Height); Dispatcher.UIThread.RunJobs();
            Assert.Equal(position, back.TranslatePoint(default, dialog));
            Assert.True(back.IsEffectivelyVisible);
            scroll.Offset = default; Dispatcher.UIThread.RunJobs();
            Capture("differences");
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.True(Find<StackPanel>(dialog, "LibraryUpdateDecision").IsEffectivelyVisible);
            Assert.Same(view, dialog.FocusManager.GetFocusedElement());
            if (accept) { Click(Find<Button>(dialog, "LibraryUpdateAccept")); }
            else if (width == 1440) { Click(Find<Button>(dialog, "LibraryUpdateCancel")); }
            else { dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); }
            await pending; Dispatcher.UIThread.RunJobs();
            var saved = library.Load(device.Id)!;
            Assert.Equal(accept, saved.Committed!.Id != scan.Id);
            Assert.Equal(accept ? "Fixture 1" : "Previous cached name", store.LoadSettings().PresetNameCache[1]);
            if (!accept)
            {
                Assert.True(sync.IsVisible);
                Assert.Empty(sync.OwnedWindows);
                Assert.Same(Find<Button>(sync, "ConnectionSyncLibrary"), sync.FocusManager!.GetFocusedElement());
            }

            void Capture(string viewName)
            {
                string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
                if (output is null) { return; }
                Directory.CreateDirectory(output); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = dialog.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, $"library-update-{viewName}-{width}-{theme}.png"));
            }
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class ChangedSource : IStoredPresetImageSource, IStoredPresetNameSource
    {
        private readonly FractalIndexWorkflowTests.NameSource _inner = new(slot => slot < 254 ? "Populated" : "<EMPTY>", withAmp: true);
        public bool Changed { get; set; }
        public int FullReadCount => _inner.FullReads.Count;
        public Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => _inner.ReadStoredPresetNameAsync(slot, device, token);
        public async Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
        {
            var image = await _inner.ReadStoredImageAsync(slot, device, token);
            if (!Changed || slot is not (1 or 9 or 53)) { return image; }
            image.RawImage[8] = 1;
            if (slot != 1) { return image; }
            BinaryPrimitives.WriteUInt16LittleEndian(image.Body.AsSpan(0x2be, 2), 2);
            return image with { Scenes = image.Scenes with { Names = ["Lead", .. image.Scenes.Names.Skip(1)] } };
        }
    }
}
