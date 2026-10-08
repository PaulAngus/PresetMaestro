using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class PresetCapacityProtocolTests
{
    private static byte[] NameReply(int slot, string name = "<EMPTY>", byte model = 0x10)
    {
        var payload = new byte[34]; payload[0] = (byte)(slot & 127); payload[1] = (byte)(slot >> 7);
        Encoding.ASCII.GetBytes(name).CopyTo(payload, 2);
        return SysexProtocol.Frame(model, 0x0d, payload);
    }

    [Fact]
    public async Task CorrelatedUpperSlotReplyEstablishesCapacityEvenWhenEmpty()
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi); client.SetDeviceModel(DeviceModel.AxeFxIII);
        midi.Send = _ =>
        {
            midi.Reply(NameReply(0)); // A wrapped or unrelated reply is not evidence.
            midi.Reply(NameReply(512, model: 0x12));
            var corrupt = NameReply(512); corrupt[^2] ^= 1; midi.Reply(corrupt);
            midi.Reply(NameReply(512));
        };
        Assert.Equal(1024, await client.DetectPresetCapacityAsync(FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIII), CancellationToken.None));
        Assert.Equal(SysexProtocol.Frame(0x10, 0x0d, [0, 4]), Assert.Single(midi.Sent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SilenceAndWrappedReplyLeaveCapacityUnknown(bool wrapped)
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi); client.SetDeviceModel(DeviceModel.AxeFxIII);
        midi.Send = _ => { if (wrapped) { midi.Reply(NameReply(0)); } };
        Assert.Null(await client.DetectPresetCapacityAsync(FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIII), CancellationToken.None));
        Assert.Single(midi.Sent);
    }

    [Fact]
    public async Task ProbeCancellationPropagatesAndCannotBecomeSmallerCapacity()
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi); client.SetDeviceModel(DeviceModel.AxeFxIII);
        using var cancellation = new CancellationTokenSource(); midi.Send = _ => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DetectPresetCapacityAsync(
            FractalDeviceDefinition.For(FractalDeviceVariant.AxeFxIII), cancellation.Token));
    }

    [Theory]
    [InlineData(FractalDeviceVariant.FM9, DeviceModel.FM9)]
    [InlineData(FractalDeviceVariant.FM3, DeviceModel.FM3)]
    public async Task KnownFamiliesNeedNoBoundaryRequest(FractalDeviceVariant variant, DeviceModel model)
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi); client.SetDeviceModel(model);
        Assert.Equal(512, await client.DetectPresetCapacityAsync(FractalDeviceDefinition.For(variant), CancellationToken.None));
        Assert.Empty(midi.Sent);
    }
}

public sealed partial class ConnectionLibrarySetupTests
{
    [AvaloniaFact]
    public async Task DetectedCapacityIsSavedAndReusedBySyncAndNavigation()
    {
        var source = new WaitingSource(withAmp: false) { Capacity = 1024 }; source.Release.SetResult();
        var (window, settings, midi, _) = CreateWindow(source, model: DeviceModel.AxeFxIII);
        try
        {
            await window.ConnectAsync(); await window.SyncIndexAsync(); Dispatcher.UIThread.RunJobs();
            var saved = Library.Load(Assert.Single(Library.ListDevices()).Id)!;
            Assert.Equal(1024, saved.Device.PresetCapacity); Assert.Equal(1024, saved.Committed!.Presets.Count);
            Assert.Equal(Enumerable.Range(0, 1024), source.Inner.NameReads);
            Assert.Equal(1024, settings.PresetNameCache.Count); Assert.Equal(1, source.CapacityChecks);
            Assert.Empty(window.OwnedWindows);
            await window.SyncIndexAsync();
            Assert.Equal(1, source.CapacityChecks);
            Assert.Equal(2048, source.Inner.NameReads.Count);
            Assert.DoesNotContain(midi.Sent, frame => frame[0] is >= 0xc0 and <= 0xcf);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light", 1440, 512)]
    [InlineData("Dark", 1440, 1024)]
    [InlineData("Light", 1000, 1024)]
    [InlineData("Dark", 1000, 512)]
    public async Task UnknownCapacityRequiresExplicitChoiceAndRemembersIt(string theme, int width, int capacity)
    {
        var source = new WaitingSource(withAmp: false); source.Release.SetResult();
        var (window, settings, midi, _) = CreateWindow(source, theme: theme, model: DeviceModel.AxeFxIII, width: width);
        try
        {
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            var syncDialog = Assert.Single(window.OwnedWindows); syncDialog.Width = width == 1000 ? 480 : 650;
            Capture(syncDialog, $"automatic-capacity-ready-{width}-{theme}");
            var syncing = window.SyncIndexAsync();
            await WaitFor(() => syncDialog.OwnedWindows.Any());
            var dialog = Assert.Single(syncDialog.OwnedWindows);
            var choices = Find<ComboBox>(dialog, "PresetCapacityChoice");
            Assert.Same(choices, dialog.FocusManager!.GetFocusedElement());
            Assert.False(Find<Button>(dialog, "PresetCapacityContinue").IsEnabled);
            Assert.Empty(source.Inner.NameReads);
            Assert.Null(Assert.Single(Library.ListDevices()).PresetCapacity);
            Capture(dialog, $"capacity-unknown-{width}-{theme}");
            choices.Focus(); dialog.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            choices.SelectedIndex = capacity == 512 ? 0 : 1; Dispatcher.UIThread.RunJobs();
            Find<Button>(dialog, "PresetCapacityContinue").Focus(); dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await syncing; Dispatcher.UIThread.RunJobs();
            var saved = Library.Load(Assert.Single(Library.ListDevices()).Id)!;
            Assert.Equal(capacity, saved.Device.PresetCapacity); Assert.Equal(capacity, saved.Committed!.Presets.Count);
            Assert.Equal(Enumerable.Range(0, capacity), source.Inner.NameReads);
            Assert.Equal(capacity - 1, settings.MaxDisplayedPreset);
            midi.AllowWrites = true;
            var send = typeof(MainWindow).GetMethod("SendPreset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            send.Invoke(window, [capacity]); Assert.Empty(midi.Writes);
            send.Invoke(window, [capacity - 1]); Assert.Single(midi.Writes);
            // The remembered limit constrains the picker as well as numeric sending.
            Assert.Equal(capacity, PresetSelection.CreateCatalog(new Dictionary<int, string>(), deviceModel: DeviceModel.AxeFxIII,
                capacity: FractalDeviceDefinition.For(saved.Device).PresetSlots).Count);
            await window.SyncIndexAsync(); Assert.Equal(1, source.CapacityChecks);
            Assert.Empty(window.OwnedWindows);
            Assert.DoesNotContain(midi.Sent, frame => frame[0] is >= 0xc0 and <= 0xcf);
            Click(Find<Button>(window, "NavConfig")); Click(Find<Button>(window, "ManageLibraries"));
            Assert.Equal(width, window.Bounds.Width);
            Capture(window, $"saved-capacity-device-{width}-{theme}");
            Click(Find<Button>(window, "IndexCreateDevice"));
            Assert.False(Find<ComboBox>(window, "IndexDeviceVariant").IsEffectivelyVisible);
            Capture(window, $"automatic-capacity-create-{width}-{theme}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingCapacityChoiceLeavesLibraryUnchanged(bool escape)
    {
        var source = new WaitingSource(withAmp: false);
        var (window, _, _, _) = CreateWindow(source, model: DeviceModel.AxeFxIII);
        try
        {
            await window.ConnectAsync(); var syncDialog = Assert.Single(window.OwnedWindows);
            var device = Assert.Single(Library.ListDevices()); string before = Json(Library.Load(device.Id)!);
            var syncing = window.SyncIndexAsync(); await WaitFor(() => syncDialog.OwnedWindows.Any());
            var dialog = Assert.Single(syncDialog.OwnedWindows);
            if (escape) { dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); }
            else { Click(Find<Button>(dialog, "PresetCapacityCancel")); }
            await syncing; Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, Json(Library.Load(device.Id)!)); Assert.Empty(source.Inner.NameReads);
            Assert.Empty(syncDialog.OwnedWindows);
            Assert.True(Find<Button>(syncDialog, "ConnectionSyncLibrary").IsEnabled);
            Assert.Same(Find<Button>(syncDialog, "ConnectionSyncLibrary"), syncDialog.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task UnresolvedLibraryCannotStartAScanOrAcceptSavedPresetData()
    {
        var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Automatic", FractalDeviceVariant.AxeFxIII) };
        var source = new WaitingSource(withAmp: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new IndexScanner(new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()), Library)
            .ScanAsync(cache, false, null, CancellationToken.None));
        Assert.Empty(source.Inner.NameReads);
        cache.LastAttempt = new IndexScan();
        Assert.Throws<InvalidDataException>(() => IndexJson.Validate(cache));
    }

    [AvaloniaFact]
    public async Task FailedScanKeepsDetectedCapacityForResume()
    {
        var source = new WaitingSource(withAmp: false) { Capacity = 1024, Fail = true }; source.Release.SetResult();
        var (window, _, _, _) = CreateWindow(source, model: DeviceModel.AxeFxIII);
        try
        {
            await window.ConnectAsync(); await window.SyncIndexAsync();
            var saved = Library.Load(Assert.Single(Library.ListDevices()).Id)!;
            Assert.Equal(1024, saved.Device.PresetCapacity); Assert.Null(saved.Committed);
            Assert.Equal("Failed", saved.LastAttempt!.Status);
            source.Fail = false; await window.SyncIndexAsync(resume: true);
            Assert.Equal(1, source.CapacityChecks);
            Assert.Equal(1024, Library.Load(saved.Device.Id)!.Committed!.Presets.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DisconnectWhileChoosingCapacityCancelsWithoutSavingAChoice()
    {
        var source = new WaitingSource(withAmp: false);
        var (window, _, _, _) = CreateWindow(source, model: DeviceModel.AxeFxIII);
        try
        {
            await window.ConnectAsync(); var syncDialog = Assert.Single(window.OwnedWindows);
            var device = Assert.Single(Library.ListDevices()); string before = Json(Library.Load(device.Id)!);
            var syncing = window.SyncIndexAsync(); await WaitFor(() => syncDialog.OwnedWindows.Any());
            typeof(MainWindow).GetMethod("Disconnect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
            Dispatcher.UIThread.RunJobs(); await syncing;
            Assert.Empty(window.OwnedWindows); Assert.Equal(before, Json(Library.Load(device.Id)!)); Assert.Empty(source.Inner.NameReads);
        }
        finally { window.Close(); }
    }
}
