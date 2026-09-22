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
            Assert.Equal("○ Not connected", Header(window).Text);
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
            Assert.Equal("○ Not connected", Header(window).Text);
            midi.Send = request => { if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.Capture("name-pm-test")); } };
            midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9"));
            await pending;
            var header = Header(window);
            foreach (string page in new[] { "PresetSender", "Favorites", "Config" })
            {
                window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Nav" + page).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(header, Header(window));
                Assert.Equal("FM9 · PM-TEST", header.Text);
            }
            Assert.Empty(errors);
            Invoke(window, "Disconnect");
            Assert.Equal("○ Not connected", header.Text);
            Assert.False(midi.InputOpen || midi.OutputOpen);
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
            Assert.Equal("○ Not connected", Header(window).Text);
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
            Assert.Equal("FM9", Header(window).Text);
            if (removed) { midi.Removed = true; await Task.Delay(1200); }
            else { midi.FailTransport(); Dispatcher.UIThread.RunJobs(); }
            Assert.Equal("○ Not connected", Header(window).Text);
            Assert.False(midi.InputOpen || midi.OutputOpen);
        }
        finally { window.Close(); }
    }
}
