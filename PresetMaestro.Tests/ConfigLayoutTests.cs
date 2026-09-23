using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaFact]
    public void ConfigUsesRequiredActionsAndMovesDebugToDiagnostics()
    {
        var settings = new AppSettings { Theme = "Light" };
        var window = CreateWindow(settings: settings);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "OpenSettingsJson");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<CheckBox>(), check => check.Name == "DebugMode");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "MAX PRESET");

            var actions = Find<StackPanel>(window, "ConnectionActions");
            Assert.Equal(new[] { "Connect", "Disconnect", "RefreshDevices", "OpenDiagnostics" },
                actions.Children.OfType<Button>().Select(button => button.Name!).ToArray());
            double inputTop = Find<ComboBox>(window, "MidiInput").TranslatePoint(default, window)!.Value.Y;
            double connectTop = Find<Button>(window, "Connect").TranslatePoint(default, window)!.Value.Y;
            Assert.Equal(inputTop, connectTop, 1);

            Assert.NotNull(Find<CheckBox>(window, "AutoSend"));
            Assert.NotNull(Find<NumericUpDown>(window, "AutoSendDelay"));
            Assert.NotNull(Find<CheckBox>(window, "KeyboardEntry"));
            Assert.NotNull(Find<CheckBox>(window, "MidiNoteEntry"));

            Click(Find<Button>(window, "OpenDiagnostics"));
            Dispatcher.UIThread.RunJobs();
            var debug = Find<CheckBox>(window, "DebugMode");
            debug.IsChecked = true;
            Assert.True(settings.DebugMode);
            Click(Find<Button>(window, "BackToConfig"));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Find<Button>(window, "OpenDiagnostics"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThruInputsRemainAnUnboundedMultiSelectList()
    {
        var midi = new DeviceMidi();
        midi.InputPorts.AddRange(["FootCtrlPlus", "Studio 68 MIDI In", "USB MIDI Interface"]);
        var settings = new AppSettings
        {
            Theme = "Light",
            MidiInputPort = "FM9",
            MidiOutputPort = "FM9",
            ThruInputPorts = ["FootCtrlPlus", "USB MIDI Interface"],
        };
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Show();
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NavConfig")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var choices = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "ThruInputs")
                .Children.OfType<CheckBox>().ToArray();
            Assert.Equal(3, choices.Length);
            Assert.Equal(2, choices.Count(choice => choice.IsChecked == true));
            choices.Single(choice => Equals(choice.Content, "Studio 68 MIDI In")).IsChecked = true;
            Assert.Equal(3, choices.Count(choice => choice.IsChecked == true));
            Assert.Empty(midi.Sent);

            string? screenshotDirectory = Environment.GetEnvironmentVariable("PRESETMAESTRO_CONFIG_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                foreach (var (name, width, height) in new[] { ("config-wide.png", 1400d, 1200d), ("config-compact.png", 900d, 1200d) })
                {
                    window.Width = width;
                    window.Height = height;
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var bitmap = window.CaptureRenderedFrame();
                    bitmap?.Save(Path.Combine(screenshotDirectory, name));
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DeviceModel.FM9, 0, 511)]
    [InlineData(DeviceModel.FM9, 1, 512)]
    [InlineData(DeviceModel.FM3, 0, 511)]
    [InlineData(DeviceModel.FM3, 1, 512)]
    [InlineData(DeviceModel.AxeFxIII, 0, 1023)]
    [InlineData(DeviceModel.AxeFxIII, 1, 1024)]
    public void EffectiveMaximumComesFromSavedModelAndDisplayOffset(DeviceModel model, int offset, int expected)
    {
        var settings = new AppSettings
        {
            Theme = "Light",
            DeviceModel = model,
            DisplayOffset = offset,
            MaxDisplayedPreset = 400,
        };
        var window = CreateWindow(settings: settings);
        try
        {
            window.Show();
            Assert.Equal(expected, Field<NumericUpDown>(window, "_favPresetSpinner").Maximum);
            Assert.Equal(expected, settings.MaxDisplayedPreset);
            Assert.Null(window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block => block.Name == "DeviceCapacity"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AxeFxMaximumIsValidForSendingAndNavigationWrapsAtCapacity()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var settings = new AppSettings { Theme = "Light", DeviceModel = DeviceModel.AxeFxIII };
        var window = CreateWindow(midi: midi, settings: settings);
        try
        {
            window.Show();
            Invoke(window, "SendPreset", 1023);
            Assert.Equal(1, midi.PresetSendCount);

            SetField(window, "_currentPreset", 1023);
            Invoke(window, "HandleNext");
            Assert.Equal(2, midi.PresetSendCount);
            Assert.Equal(0, (int?)typeof(MainWindow).GetField("_currentPreset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window));

            Invoke(window, "SendPreset", 1024);
            Assert.Equal(2, midi.PresetSendCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ConfigRetainsTwoColumnAndCompactSingleColumnArrangements()
    {
        var window = CreateWindow();
        try
        {
            window.Width = 1200;
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            var right = Find<StackPanel>(window, "ConfigRightColumn");
            Assert.Equal(2, Grid.GetColumn(right));
            Assert.Equal(0, Grid.GetRow(right));

            window.Width = 800;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Grid.GetColumn(right));
            Assert.Equal(2, Grid.GetRow(right));
            var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(viewer => viewer.Content is Grid { Name: "ConfigLayout" });
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
        }
        finally { window.Close(); }
    }
}
