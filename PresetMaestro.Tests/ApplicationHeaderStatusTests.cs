using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1440, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Dark")]
    public async Task HeaderConnectionStatusSurvivesNavigationConnectionAndResizing(int width, string theme)
    {
        var midi = new DeviceMidi();
        midi.Send = request => { if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); } };
        var settings = new AppSettings
        {
            Theme = theme,
            ActiveProfile = "A very long rehearsal profile name with extra information",
            MidiInputPort = "FM9",
            MidiOutputPort = "FM9",
        };
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { })
        {
            Width = width,
            Height = 850,
            DeviceInformationTimeout = TimeSpan.FromMilliseconds(20),
            ThruInputRetryDelay = TimeSpan.Zero,
        };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            AssertPages("Not connected", capture: true);
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            AssertPages("Device not checked", capture: true);
            Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "Disconnect")); Dispatcher.UIThread.RunJobs();
            AssertPages("Not connected");

            Click(Find<Button>(window, "NavAmps")); Dispatcher.UIThread.RunJobs();
            window.Width = width == 1000 ? 1440 : 1000;
            Dispatcher.UIThread.RunJobs();
            AssertHeader("Not connected");
            Assert.Equal(window.Width >= 1280, Find<TextBlock>(window, "IndexAssignedDevice").IsEffectivelyVisible);
        }
        finally { window.Close(); }

        void AssertPages(string expected, bool capture = false)
        {
            foreach (string page in new[] { "Config", "PresetSender", "Amps", "PresetIndex" })
            {
                Click(Find<Button>(window, "Nav" + page)); Dispatcher.UIThread.RunJobs();
                AssertHeader(expected);
                if (capture) { CaptureSendConfirmation(window, $"header-status-{(expected == "Not connected" ? "offline" : "unchecked")}-{page}-{width}-{theme}"); }
            }
        }

        void AssertHeader(string expected)
        {
            var status = Find<TextBlock>(window, "HeaderConnectionStatus");
            Assert.True(status.IsEffectivelyVisible);
            Assert.Equal(expected, status.Text);
            string fullStatus = expected == "Device not checked" ? "MIDI connected · Device not checked" : expected;
            foreach (var label in window.GetVisualDescendants().OfType<TextBlock>()
                .Where(label => label.Name is "MidiConnectionStatus" or "SenderConnectionStatus"))
            { Assert.Equal(fullStatus, label.Text); }
            Assert.Equal(fullStatus, Avalonia.Automation.AutomationProperties.GetName(status));
            Assert.Equal(fullStatus, Avalonia.Controls.ToolTip.GetTip(status));
            var identity = Find<StackPanel>(window, "AppIdentity");
            var first = Find<Button>(window, "NavPresetSender");
            var last = Find<Button>(window, "NavConfig");
            Assert.True(identity.TranslatePoint(default, window)!.Value.X + identity.Bounds.Width <= first.TranslatePoint(default, window)!.Value.X);
            Assert.True(last.TranslatePoint(default, window)!.Value.X + last.Bounds.Width < status.TranslatePoint(default, window)!.Value.X);
            AssertContainedHorizontally(status, window);
            AssertContainedHorizontally(Find<TextBlock>(window, "HeaderActiveProfile"), window);
        }
    }
}
