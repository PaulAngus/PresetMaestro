using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1440, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Dark")]
    public void HeaderProfileIsInformationalAndConfigStillSwitchesProfiles(int width, string theme)
    {
        using var fixture = new ManagedProfileFixture(theme);
        var window = fixture.Window;
        window.Width = width;
        window.Height = 850;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var config = Find<Button>(window, "NavConfig");
        config.Focus();
        window.CaptureRenderedFrame()?.Dispose();
        var label = Find<TextBlock>(window, "HeaderActiveProfile");
        var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(config, window.FocusManager!.GetFocusedElement());
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Default", fixture.Store.LoadSettings().ActiveProfile);
        CaptureSendConfirmation(window, $"header-profile-{width}-{theme}");

        // Tab leaves navigation for Config, skipping the informational profile label.
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(Find<ComboBox>(window, "MidiInput"), window.FocusManager.GetFocusedElement());

        var selector = Find<ComboBox>(window, "ProfileSelector");
        selector.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Session", fixture.Settings.ActiveProfile);
        Assert.Equal("Session", fixture.Store.LoadSettings().ActiveProfile);
        Assert.Equal("Profile: Session", label.Text);
        Assert.Equal("Active profile: Session", AutomationProperties.GetName(label));
        Assert.Equal(6, fixture.Settings.MidiChannel);
        Assert.Equal("Session preset", fixture.Settings.PresetNameCache[4]);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }
}
