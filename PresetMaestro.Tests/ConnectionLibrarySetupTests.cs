using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class ConnectionLibrarySetupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-setup-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private IndexLibrary Library => new(Path.Combine(_directory, "FractalIndex"));
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) { await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Capture(Window dialog, string name)
    {
        string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
        if (output is null) { return; }
        Directory.CreateDirectory(output); dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = dialog.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(output, name + ".png"));
    }
    private static DeviceIndex OccupiedDefault(FractalDeviceVariant variant = FractalDeviceVariant.FM9) => new()
    {
        Device = new(Guid.NewGuid(), "Default", variant, "12.00"),
        PresetMapping = new(3, 1, 35),
        Committed = new IndexScan
        {
            Status = "Complete",
            FinishedAt = DateTimeOffset.UtcNow,
            Firmware = "12.00",
            ConnectedDeviceName = "Previous device",
            Presets = Enumerable.Range(0, 512).ToDictionary(s => s, s => FractalIndexWorkflowTests.Preset(s, variant)),
        },
    };
    private static string Json(DeviceIndex cache) => System.Text.Json.JsonSerializer.Serialize(cache, IndexJson.Options);

    private (MainWindow Window, AppSettings Settings, DeviceMidi Midi, ProfileStore Store) CreateWindow(WaitingSource source, DeviceIndex? existing = null, string theme = "Light",
        Func<string, string, string, string, Task<bool>>? confirm = null, DeviceModel model = DeviceModel.FM9, bool assigned = false)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        settings.MidiInputPort = "FM9"; settings.MidiOutputPort = "FM9";
        settings.PresetNameCache = Enumerable.Range(0, 512).ToDictionary(s => s, s => "Cached " + s);
        settings.FractalIndex = IndexJson.ToElement(assigned && existing is not null
            ? new IndexProfile { Devices = [existing.Device], SelectedDeviceId = existing.Device.Id } : new IndexProfile());
        store.SaveSettings(settings);
        if (existing is not null) { Library.Save(existing); }
        var midi = new DeviceMidi();
        byte modelId = model == DeviceModel.AxeFxIII ? (byte)0x10 : model == DeviceModel.FM3 ? (byte)0x11 : (byte)0x12;
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Frame(modelId, 0x64, [0, 0])); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(modelId, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.SyntheticName("Stage")); }
        };
        var window = new MainWindow(settings, [new Favorite { Id = 1, Name = "Favorite", Preset = 1, Scene = 2 }], midi, profileStore: store,
            confirm: confirm ?? ((_, _, _, _) => Task.FromResult(true)))
        { Width = 1200, Height = 900, ThruInputRetryDelay = TimeSpan.Zero };
        typeof(MainWindow).GetField("_fractalIndexReader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()));
        window.Show();
        return (window, settings, midi, store);
    }

    [AvaloniaFact]
    public async Task AxeFxRequiresTheHardwareVariantBeforeAssigningDefault()
    {
        var source = new WaitingSource();
        var (window, _, _, _) = CreateWindow(source, model: DeviceModel.AxeFxIII);
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            var variants = Find<ComboBox>(dialog, "ConnectionLibraryVariant");
            Assert.True(variants.IsEffectivelyVisible);
            Assert.Empty(Library.ListDevices());
            Assert.False(Find<Button>(dialog, "ConnectionSyncLibrary").IsEnabled);
            variants.SelectedItem = variants.Items.Cast<object>().Single(v => v.ToString() == "Axe-Fx III Mark II · 1024 slots");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FractalDeviceVariant.AxeFxIIIMarkII, Assert.Single(Library.ListDevices()).Variant);
            Assert.True(Find<Button>(dialog, "ConnectionSyncLibrary").IsEnabled);
            Assert.Empty(source.Inner.NameReads);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "Light")]
    [InlineData(true, "Dark")]
    public async Task FreshConnectionUsesUnusedDefaultAndRequiresEverySlotBeforeOpeningIndex(bool placeholder, string theme)
    {
        var source = new WaitingSource();
        var empty = placeholder ? new DeviceIndex { Device = new(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9), PresetMapping = new(2, 0, 34) } : null;
        var (window, settings, midi, store) = CreateWindow(source, empty, theme);
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            var assigned = Assert.Single(Library.ListDevices());
            Assert.Equal("Default", assigned.Name);
            if (empty is not null) { Assert.Equal(empty.Device.Id, assigned.Id); }
            Assert.Equal(assigned.Id, IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).SelectedDeviceId);
            Assert.True(Find<Button>(dialog, "ConnectionSyncLibrary").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncNames").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncNames").IsEffectivelyVisible);
            Assert.False(Find<Button>(dialog, "ConnectionSyncScenes").IsEnabled);
            Assert.Equal("Disconnect", Find<Button>(dialog, "ConnectionSyncDone").Content);
            Assert.Equal("Full library sync required", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.Empty(source.Inner.NameReads);
            Capture(dialog, "first-connection-default-" + theme.ToLowerInvariant());

            var syncing = window.SyncIndexAsync();
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(Library.Load(assigned.Id)!.Committed);
            Assert.Single(window.OwnedWindows);
            source.Release.SetResult(); await syncing; Dispatcher.UIThread.RunJobs();
            var saved = Library.Load(assigned.Id)!;
            Assert.Equal("Complete", saved.Committed!.Status);
            Assert.Equal(512, saved.Committed.Presets.Count);
            Assert.Equal(Enumerable.Range(0, 512), source.Inner.NameReads);
            Assert.Single(source.Inner.FullReads);
            Assert.Equal("Stage", saved.Committed.ConnectedDeviceName);
            Assert.Equal(512, settings.PresetNameCache.Count);
            Assert.Equal("Fixture 0", settings.PresetNameCache[0]);
            Assert.Contains(saved.Committed.Presets[0].Amps, a => a.Channels.Length == 4);
            Assert.Empty(window.OwnedWindows);
            Assert.True(Find<Control>(window, "PresetIndexPage").IsEffectivelyVisible);
            Assert.DoesNotContain(midi.Sent, frame => frame[0] is >= 0xc0 and <= 0xcf);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task OccupiedDefaultOffersNewLibraryAndDoesNotOverwriteIt()
    {
        var original = OccupiedDefault(); var source = new WaitingSource();
        var (window, _, _, _) = CreateWindow(source, original);
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Choose a library for this device", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.True(Find<Button>(dialog, "ConnectionLibraryCreate").IsEffectivelyVisible);
            Assert.True(Find<Button>(dialog, "ConnectionLibraryOverwrite").IsEnabled);
            Assert.False(Find<Button>(dialog, "ConnectionSyncLibrary").IsEnabled);
            Capture(dialog, "first-connection-default-occupied");
            Find<TextBox>(dialog, "ConnectionLibraryName").Text = "default";
            Click(Find<Button>(dialog, "ConnectionLibraryCreate"));
            Assert.Contains("already exists", Find<TextBlock>(dialog, "ConnectionLibrarySetupMessage").Text);
            Assert.Empty(source.Inner.NameReads);
            Find<TextBox>(dialog, "ConnectionLibraryName").Text = "New FM9";
            Click(Find<Button>(dialog, "ConnectionLibraryCreate"));
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
            source.Release.SetResult(); await WaitFor(() => !window.OwnedWindows.Any());
            Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
            var added = Assert.Single(Library.ListDevices(), d => d.Name == "New FM9");
            Assert.Equal(512, Library.Load(added.Id)!.Committed!.Presets.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(FractalDeviceVariant.FM9)]
    [InlineData(FractalDeviceVariant.FM3)]
    public async Task ExplicitOverwritePublishesOnlyAfterFullSyncAndUpdatesSharedReferences(FractalDeviceVariant previousModel)
    {
        var original = OccupiedDefault(previousModel); var source = new WaitingSource();
        int confirmations = 0;
        var (window, settings, _, store) = CreateWindow(source, original, confirm: (title, message, _, _) =>
        {
            Assert.Equal("Overwrite Default library", title); Assert.Contains("Other", message);
            confirmations++; return Task.FromResult(true);
        });
        var shared = new IndexProfile { Devices = [original.Device], SelectedDeviceId = original.Device.Id, PortableSnapshots = [IndexJson.Clone(original)] };
        shared.GetOrCreate(original.Device.Id, original.Committed!.Presets[0], "12.00").Tags = ["Keep for review"];
        store.Create("Other", new ProfileSettings { FractalIndex = IndexJson.ToElement(shared) });
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Click(Find<Button>(dialog, "ConnectionLibraryOverwrite"));
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, confirmations);
            Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
            Assert.Single(Library.ListDevices());
            source.Release.SetResult(); await WaitFor(() => !window.OwnedWindows.Any());
            var saved = Library.Load(original.Device.Id)!;
            Assert.NotEqual(original.Committed.Id, saved.Committed!.Id);
            Assert.Equal(FractalDeviceVariant.FM9, saved.Device.Variant);
            Assert.Equal(original.PresetMapping, saved.PresetMapping);
            Assert.Equal(512, saved.Committed.Presets.Count);
            Assert.Equal(saved.Device.Id, IndexJson.ReadProfile(settings.FractalIndex).SelectedDeviceId);
            var other = store.LoadProfile("Other");
            var references = IndexJson.ReadProfile(other.FractalIndex);
            Assert.Equal(saved.Device, references.AssignedDevice);
            Assert.Empty(references.PortableSnapshots);
            Assert.Equal("Keep for review", Assert.Single(Assert.Single(references.Pending(saved)).Tags));
            Assert.Equal(DeviceModel.FM9, other.DeviceModel);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledOrFailedOverwriteKeepsDefaultAndRequiresSetup(bool fail)
    {
        var original = OccupiedDefault(); var source = new WaitingSource { Fail = fail };
        var (window, _, midi, _) = CreateWindow(source, original);
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Click(Find<Button>(dialog, "ConnectionLibraryOverwrite"));
            await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (fail) { source.Release.SetResult(); }
            else { Click(Find<Button>(dialog, "ConnectionSyncCancel")); }
            await WaitFor(() => Find<Button>(dialog, "ConnectionSyncLibrary").IsEnabled);
            Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
            Assert.Equal("Library sync needs attention", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.False(Find<Button>(dialog, "ConnectionSyncNames").IsEnabled);
            Assert.Equal("Disconnect", Find<Button>(dialog, "ConnectionSyncDone").Content);
            Assert.True(Find<Button>(dialog, "ConnectionSyncResume").IsEnabled);
            Capture(dialog, fail ? "first-connection-sync-failed" : "first-connection-sync-cancelled");
            dialog.Close(); Dispatcher.UIThread.RunJobs();
            Assert.False(midi.InputOpen); Assert.False(midi.OutputOpen);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DecliningOverwriteKeepsTheChoiceAndClosingSetupDisconnects()
    {
        var original = OccupiedDefault(); var source = new WaitingSource();
        var (window, _, midi, _) = CreateWindow(source, original, confirm: (_, _, _, _) => Task.FromResult(false));
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Click(Find<Button>(dialog, "ConnectionLibraryOverwrite"));
            Assert.Empty(source.Inner.NameReads);
            Assert.True(Find<Button>(dialog, "ConnectionLibraryCreate").IsEnabled);
            Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
            Click(Find<Button>(dialog, "ConnectionSyncDone"));
            Assert.False(midi.InputOpen); Assert.False(midi.OutputOpen);
            Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyDefaultAssignedToAnotherProfileStillRequiresAnExplicitChoice(bool activeAlsoAssigned)
    {
        var empty = new DeviceIndex { Device = new(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9), PresetMapping = new() };
        var (window, _, _, store) = CreateWindow(new WaitingSource(), empty, assigned: activeAlsoAssigned);
        store.Create("Other", new ProfileSettings { FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [empty.Device], SelectedDeviceId = empty.Device.Id }) });
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("Choose a library for this device", Find<TextBlock>(dialog, "ConnectionSyncStatusTitle").Text);
            Assert.True(Find<Button>(dialog, "ConnectionLibraryOverwrite").IsEnabled);
            Assert.Equal(Json(empty), Json(Library.Load(empty.Device.Id)!));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void FailedProfileUpdateRollsBackReferencesAndPreservesTheOccupiedLibrary()
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        var original = OccupiedDefault(FractalDeviceVariant.FM3); Library.Save(original);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile()); store.SaveSettings(settings);
        store.Create("Other", new ProfileSettings { FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [original.Device], SelectedDeviceId = original.Device.Id }) });
        string activePath = Path.Combine(_directory, "Default-settings.json"), otherPath = Path.Combine(_directory, "Other-settings.json");
        string activeBefore = File.ReadAllText(activePath), otherBefore = File.ReadAllText(otherPath);
        var replacement = new DeviceIndex
        {
            Device = original.Device with { Variant = FractalDeviceVariant.FM9 },
            PresetMapping = original.PresetMapping,
            Committed = new IndexScan { Status = "Complete", FinishedAt = DateTimeOffset.UtcNow, Firmware = "12.00", Presets = Enumerable.Range(0, 512).ToDictionary(s => s, s => FractalIndexWorkflowTests.Preset(s)) },
        };
        using (var locked = File.Open(otherPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => store.PublishConnectionLibrary(replacement, "Default", settings.FractalIndex, original));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(activeBefore, File.ReadAllText(activePath));
        Assert.Equal(otherBefore, File.ReadAllText(otherPath));
        Assert.Equal(Json(original), Json(Library.Load(original.Device.Id)!));
    }

    private sealed class WaitingSource : IStoredPresetNameSource, IStoredPresetImageSource
    {
        public FractalIndexWorkflowTests.NameSource Inner { get; } = new(s => s == 0 ? "Fixture 0" : "<EMPTY>", withAmp: true);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail { get; init; }
        public async Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(token);
            if (Fail) { throw new TimeoutException("No reply"); }
            return await Inner.ReadStoredPresetNameAsync(slot, device, token);
        }
        public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => Fail
            ? throw new IOException("Preset data unavailable") : Inner.ReadStoredImageAsync(slot, device, token);
    }
}
