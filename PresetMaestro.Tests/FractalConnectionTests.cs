using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public class FractalConnectionTests
{
    [AvaloniaFact]
    public async Task SlowDiscoveryLeavesDispatcherResponsiveAndOldResultCannotDisconnectNewSession()
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var window = Create(midi, []); window.ThruInputRetryDelay = TimeSpan.Zero;
        var pending = new TaskCompletionSource<PresetMaestro.Midi.MidiPorts>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await window.ConnectAsync();
            midi.Discover = () => pending.Task;
            var check = window.CheckConnectedDevicesAsync("FM9", "FM9");
            bool dispatched = false; Dispatcher.UIThread.Post(() => dispatched = true); Dispatcher.UIThread.RunJobs();
            Assert.True(dispatched); Assert.False(check.IsCompleted);
            Invoke(window, "Disconnect");
            await window.ConnectAsync();
            pending.SetResult(new([], []));
            await check;
            Assert.True(midi.InputOpen && midi.OutputOpen); Assert.Equal("Device not checked", Header(window).Text);
        }
        finally { pending.TrySetResult(new([], [])); window.Close(); }
    }

    private static MainWindow Create(DeviceMidi midi, List<string> errors)
    {
        var window = new MainWindow(new AppSettings { Theme = "Light", MidiInputPort = "FM9", MidiOutputPort = "FM9" }, [], midi,
            saveSettings: _ => { }, saveFavorites: _ => { });
        window.ConnectionErrorOverride = message => { errors.Add(message); return Task.CompletedTask; };
        window.DeviceInformationTimeout = TimeSpan.FromMilliseconds(20);
        window.Show();
        return window;
    }

    private static TextBlock Header(MainWindow window) => window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "HeaderConnectionStatus");
    private static void Invoke(MainWindow window, string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    [AvaloniaFact]
    public async Task ConnectButtonReflectsConnectionAcrossThemeChangesAndDisconnect()
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var window = Create(midi, []);
        window.ThruInputRetryDelay = TimeSpan.Zero;
        try
        {
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NavConfig")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Button ConnectButton() => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Connect");
            Assert.True(ConnectButton().IsEnabled);
            Assert.Equal("Connect", ConnectButton().Content);
            var disconnectedBackground = ConnectButton().Background;

            await window.ConnectAsync();
            Assert.False(ConnectButton().IsEnabled);
            Assert.Equal("Connected", ConnectButton().Content);
            Assert.NotEqual(disconnectedBackground, ConnectButton().Background);
            window.GetVisualDescendants().OfType<RadioButton>().Single(radio => Equals(radio.Content, "Dark")).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(ConnectButton().IsEnabled);
            Assert.Equal("Connected", ConnectButton().Content);
            Assert.True(midi.InputOpen && midi.OutputOpen);

            Invoke(window, "Disconnect");
            Assert.True(ConnectButton().IsEnabled);
            Assert.Equal("Connect", ConnectButton().Content);
            var darkBackground = ConnectButton().Background;
            window.GetVisualDescendants().OfType<RadioButton>().Single(radio => Equals(radio.Content, "Light")).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ConnectButton().IsEnabled);
            Assert.Equal("Connect", ConnectButton().Content);
            Assert.NotEqual(darkBackground, ConnectButton().Background);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("PM-TEST")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ConnectionSavesDeviceNameAndDisconnectRetainsAssociation(string? deviceName)
    {
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            else if (request[5] == 1 && deviceName is not null) { midi.Reply(FractalDeviceInformationTests.SyntheticName(deviceName)); }
        };
        var settings = new AppSettings { Theme = "Light", MidiInputPort = "FM9", MidiOutputPort = "FM9", DeviceName = "Previous device" };
        ProfileSettings? saved = null;
        var window = new MainWindow(settings, [], midi, saveSettings: value => saved = ProfileSettings.From(value), saveFavorites: _ => { })
        {
            DeviceInformationTimeout = TimeSpan.FromMilliseconds(20),
            ThruInputRetryDelay = TimeSpan.Zero,
        };
        try
        {
            window.Show();
            await window.ConnectAsync();
            Assert.NotNull(saved);
            Assert.Equal(deviceName, saved.DeviceName);
            Assert.Equal(deviceName, settings.DeviceName);
            settings.DeviceName = "Different profile device";
            Invoke(window, "ApplyProfileSettingsToUI");
            Assert.Equal(deviceName, settings.DeviceName);
            Invoke(window, "Disconnect");
            Assert.Equal(deviceName, settings.DeviceName);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0x10, DeviceModel.AxeFxIII, 1023)]
    [InlineData(0x11, DeviceModel.FM3, 511)]
    [InlineData(0x12, DeviceModel.FM9, 511)]
    public async Task PickerUsesDetectedModelAcrossProfileChangesAndOffline(int model, DeviceModel expected, int maximum)
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Frame((byte)model, 0x64, [0, 0])); } };
        var window = Create(midi, []);
        var settings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var spinner = (NumericUpDown)typeof(MainWindow).GetField("_favPresetSpinner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        try
        {
            settings.DeviceModel = expected == DeviceModel.AxeFxIII ? DeviceModel.FM9 : DeviceModel.AxeFxIII;
            await window.ConnectAsync();
            Assert.Equal(expected, settings.DeviceModel);
            Assert.Equal(maximum + settings.DisplayOffset, spinner.Maximum);
            Assert.Equal(maximum + settings.DisplayOffset, settings.MaxDisplayedPreset);
            Assert.Equal(expected, window.ConnectionState.Device!.Model);
            Assert.Equal("Device not checked", Header(window).Text);
            // Loading a profile must not override the connected device's identity.
            settings.DeviceModel = expected == DeviceModel.AxeFxIII ? DeviceModel.FM9 : DeviceModel.AxeFxIII;
            Invoke(window, "ApplyProfileSettingsToUI");
            Assert.Equal(expected, settings.DeviceModel);
            Assert.Equal(maximum + settings.DisplayOffset, spinner.Maximum);
            Invoke(window, "Disconnect");
            Assert.Equal(expected, settings.DeviceModel);
            Assert.Equal(maximum + settings.DisplayOffset, spinner.Maximum);
            Assert.Equal(maximum + settings.DisplayOffset, settings.MaxDisplayedPreset);
            Assert.Equal("Not connected", Header(window).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    public async Task UnverifiedModelsKeepPresetSendingButDisableNameReads(int model)
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Frame((byte)model, 0x64, [0, 0])); } };
        var window = Create(midi, []);
        window.ThruInputRetryDelay = TimeSpan.Zero;
        try
        {
            await window.ConnectAsync();
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NavConfig")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var sync = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ConfigSyncOptions");
            Assert.True(sync.IsEnabled);
            var dialog = Assert.Single(window.OwnedWindows);
            dialog.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "ConnectionSyncOtherOptions").IsExpanded = true;
            dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var names = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ConnectionSyncNames");
            Assert.False(names.IsEnabled);
            Assert.Contains("FM9 only", ToolTip.GetTip(names)?.ToString());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Sync all stored scene names"));
            await (Task)typeof(MainWindow).GetMethod("ReadFavoriteScenesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            await window.SyncPresetNamesAsync();
            Assert.Contains("FM9 only", dialog.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "ConnectionSyncStatus").Text);
            window.GetVisualDescendants().OfType<RadioButton>().Single(radio => Equals(radio.Content, "Dark")).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("FM9 only", dialog.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "ConnectionSyncStatus").Text);
            Assert.Equal(new byte[] { 0, 8 }, midi.Sent.Select(frame => frame[5]));
            Assert.True(midi.InputOpen && midi.OutputOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("timeout")]
    [InlineData("malformed")]
    [InlineData("non-fractal")]
    [InlineData("unsupported")]
    [InlineData("missing-input")]
    public async Task InvalidConnectionClosesPortsAndShowsError(string failure)
    {
        var midi = new DeviceMidi { FailInput = failure == "missing-input" };
        midi.Send = request =>
        {
            byte[] reply = FractalDeviceInformationTests.Capture("identity-fm9");
            if (failure == "timeout") { return; }
            if (failure == "malformed") { reply[^2] ^= 1; }
            if (failure == "non-fractal") { reply[3] = 0x75; }
            if (failure == "unsupported") { reply = FractalDeviceInformationTests.Frame(3, 0x64, [0, 0]); }
            midi.Reply(reply);
        };
        var errors = new List<string>();
        var window = Create(midi, errors);
        try
        {
            await window.ConnectAsync();
            Assert.False(midi.InputOpen);
            Assert.False(midi.OutputOpen);
            Assert.Equal("Not connected", Header(window).Text);
            Assert.Equal(MainWindow.ConnectionError, Assert.Single(errors));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PortsOpeningDoesNotConnectAndValidatedStatusIsSharedAcrossTabs()
    {
        var midi = new DeviceMidi();
        var errors = new List<string>();
        var window = Create(midi, errors);
        window.DeviceInformationTimeout = TimeSpan.FromSeconds(2);
        try
        {
            var pending = window.ConnectAsync();
            Assert.True(midi.InputOpen && midi.OutputOpen);
            Assert.Equal("Not connected", Header(window).Text);
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NavConfig").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var sync = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfigSyncOptions");
            Assert.False(sync.IsEnabled);
            Assert.Equal("Sync options…", sync.Content);
            midi.Send = request => { if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-pm-test")); } };
            midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9"));
            await pending;
            Assert.True(sync.IsEnabled);
            var syncDialog = Assert.Single(window.OwnedWindows);
            syncDialog.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "ConnectionSyncOtherOptions").IsExpanded = true;
            syncDialog.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(syncDialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConnectionSyncNames").IsEnabled);
            var header = Header(window);
            foreach (string page in new[] { "PresetSender", "Favorites", "Config" })
            {
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Nav" + page).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(header, Header(window));
                Assert.Equal("Device not checked", header.Text);
                Assert.Equal("FM9 · PM-TEST", window.ConnectionState.Device!.Label);
            }
            Assert.Empty(errors);
            Invoke(window, "Disconnect");
            Assert.Equal("Not connected", header.Text);
            Assert.False(midi.InputOpen || midi.OutputOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SavedThruPortIsOpenedThenReopenedAfterConnection()
    {
        var midi = new DeviceMidi();
        midi.InputPorts.Add("FootCtrlPlus");
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var settings = new AppSettings
        {
            Theme = "Light",
            MidiInputPort = "FM9",
            MidiOutputPort = "FM9",
            ThruInputPorts = ["FootCtrlPlus"]
        };
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { })
        {
            DeviceInformationTimeout = TimeSpan.FromMilliseconds(20),
            ThruInputRetryDelay = TimeSpan.Zero
        };
        window.Show();
        try
        {
            await window.ConnectAsync();

            Assert.Equal(["FootCtrlPlus", "FootCtrlPlus"], midi.OpenedThruPorts);
            Assert.Contains("FootCtrlPlus", midi.ClosedThruPorts);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThrowingThruDriverDoesNotCrashValidatedConnection()
    {
        var midi = new DeviceMidi { ThrowThru = true };
        midi.InputPorts.Add("FootCtrlPlus");
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var settings = new AppSettings
        {
            Theme = "Light",
            MidiInputPort = "FM9",
            MidiOutputPort = "FM9",
            ThruInputPorts = ["FootCtrlPlus"]
        };
        var errors = new List<string>();
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { })
        {
            ConnectionErrorOverride = message => { errors.Add(message); return Task.CompletedTask; },
            DeviceInformationTimeout = TimeSpan.FromMilliseconds(20),
            ThruInputRetryDelay = TimeSpan.Zero
        };
        window.Show();
        try
        {
            await window.ConnectAsync();

            Assert.True(midi.InputOpen && midi.OutputOpen);
            Assert.Equal("Device not checked", Header(window).Text);
            Assert.Empty(errors);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DisconnectDuringQueryPreventsLateConnectedLabel()
    {
        var midi = new DeviceMidi();
        var errors = new List<string>();
        var window = Create(midi, errors);
        try
        {
            var pending = window.ConnectAsync();
            var late = midi.SnapshotListener();
            Invoke(window, "Disconnect");
            late?.Invoke(midi, FractalDeviceInformationTests.Capture("identity-fm9"));
            await pending;
            Assert.Equal("Not connected", Header(window).Text);
            Assert.False(midi.InputOpen || midi.OutputOpen);
            Assert.Empty(errors);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailureOrRemovalClearsIdentity(bool removed)
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var window = Create(midi, []);
        try
        {
            await window.ConnectAsync();
            Assert.Equal("Device not checked", Header(window).Text);
            if (removed) { midi.Removed = true; await Task.Delay(1200); }
            else { midi.FailTransport(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal("Not connected", Header(window).Text);
            Assert.False(midi.InputOpen || midi.OutputOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DeviceEnumerationFailureKeepsConnectionAndRecovers()
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var window = Create(midi, []);
        window.ThruInputRetryDelay = TimeSpan.Zero;
        try
        {
            await window.ConnectAsync();
            midi.OutputEnumerationError = new IOException("MIDI output driver unavailable");
            await window.CheckConnectedDevicesAsync("FM9", "FM9");
            await window.CheckConnectedDevicesAsync("FM9", "FM9");
            Assert.True(midi.InputOpen && midi.OutputOpen);
            Assert.Equal("Device not checked", Header(window).Text);

            midi.OutputEnumerationError = null;
            await window.CheckConnectedDevicesAsync("FM9", "FM9");
            Assert.Equal("Device not checked", Header(window).Text);

            midi.OutputRemoved = true;
            await window.CheckConnectedDevicesAsync("FM9", "FM9");
            Assert.False(midi.InputOpen || midi.OutputOpen);
            Assert.Equal("Not connected", Header(window).Text);
        }
        finally { window.Close(); }
    }
}
