using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed class ConfigNumericInputTests
{
    [AvaloniaTheory]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    public void ConfigValuesFitAndSpinControlsKeepWorking(int width, string theme)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-numeric-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store) { Width = width, Height = 850 };
        window.Show();
        try
        {
            Find<Button>("NavConfig").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var delay = Find<NumericUpDown>("AutoSendDelay");
            var threshold = Find<NumericUpDown>("IndexMatchThreshold");
            delay.Value = 2000; threshold.Value = 100; Dispatcher.UIThread.RunJobs();
            foreach (var spinner in new[] { delay, threshold })
            {
                var input = spinner.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_TextBox");
                var text = new FormattedText(input.Text!, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch), input.FontSize, Brushes.Black);
                double available = input.Bounds.Width - input.Padding.Left - input.Padding.Right - input.BorderThickness.Left - input.BorderThickness.Right;
                Assert.True(available >= text.Width + 2, $"{spinner.Name}: {available} pixels available for {text.Width} pixels of numeric text.");
            }
            var increase = threshold.GetVisualDescendants().OfType<RepeatButton>().Single(b => b.Name == "PART_IncreaseButton");
            Assert.False(increase.IsEnabled);
            threshold.Value = 99; Dispatcher.UIThread.RunJobs();
            increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, threshold.Value);
            var field = threshold.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_TextBox");
            field.Focus(); window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(99, threshold.Value);
            threshold.Value = 100; Dispatcher.UIThread.RunJobs(); threshold.BringIntoView(); Dispatcher.UIThread.RunJobs();
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = window.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, $"config-numeric-{width}-{theme}.png"));
            }
        }
        finally { window.Close(); Directory.Delete(directory, true); }

        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    }
}
