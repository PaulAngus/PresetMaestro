using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    public void FavoriteConfirmationDoesNotMovePageContentOrKeyboardFocus(int width, string theme)
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi, settings: new() { Theme = theme });
        window.Width = width; window.Height = 850;
        try
        {
            window.Show(); Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            Control[] controls = [Find<Grid>(window, "FavoriteToolbar"), Find<Grid>(window, "FavoriteSearch"), Find<TextBlock>(window, "FavoriteResultCount"), Field<ListBox>(window, "_favListBox")];
            var positions = controls.Select(c => (Origin: c.TranslatePoint(default, window), Size: c.Bounds.Size)).ToArray();
            var send = Find<Button>(window, "FavoriteSendCommand"); send.Focus();
            Invoke(window, "HandleDigit", 1); Click(send); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, midi.FavoriteSendCount);
            var feedback = Find<Border>(window, "SendFeedback");
            Assert.True(feedback.IsVisible);
            Assert.Equal("SENT", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Contains("Favorite 1", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Assert.Same(send, window.FocusManager!.GetFocusedElement());
            Assert.Equal(positions, controls.Select(c => (Origin: c.TranslatePoint(default, window), Size: c.Bounds.Size)).ToArray());
            var origin = feedback.TranslatePoint(default, window)!.Value;
            Assert.InRange(origin.X, 0, width - feedback.Bounds.Width);
            Assert.Equal(56, origin.Y);
            Assert.Equal((width - feedback.Bounds.Width) / 2, origin.X);
            Assert.True(origin.X + feedback.Bounds.Width < send.TranslatePoint(default, window)!.Value.X);
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(feedback));
            Assert.Contains("Favorite 1", AutomationProperties.GetName(feedback));
            CaptureSendConfirmation(window, $"favorite-sent-{width}-{theme}");
            Invoke(window, "HideSendFeedback"); Dispatcher.UIThread.RunJobs();
            Assert.False(feedback.IsVisible);
            Assert.Equal(positions, controls.Select(c => (Origin: c.TranslatePoint(default, window), Size: c.Bounds.Size)).ToArray());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PresetAndFavoriteSendsShareOneConfirmationAcrossScreens()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi);
        try
        {
            window.Show(); Invoke(window, "HandleDigit", 8); Invoke(window, "HandleSend"); Dispatcher.UIThread.RunJobs();
            var feedback = Find<Border>(window, "SendFeedback");
            var originalPosition = feedback.TranslatePoint(default, window);
            Assert.Equal(1, midi.PresetSendCount);
            Assert.True(feedback.IsVisible);
            Assert.Equal("Preset 8 sent.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
            Assert.Same(feedback, Find<Border>(window, "SendFeedback"));
            Assert.Equal(originalPosition, feedback.TranslatePoint(default, window));
            Invoke(window, "HandleDigit", 1); Click(Find<Button>(window, "FavoriteSendCommand")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, midi.FavoriteSendCount);
            Assert.Contains("Favorite 1", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Click(Find<Button>(window, "NavPresetSender")); Dispatcher.UIThread.RunJobs();
            Assert.True(feedback.IsVisible);
            Assert.Equal(originalPosition, feedback.TranslatePoint(default, window));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MissingFavoriteShowsWarningAndDoesNotSendMidi()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi);
        try
        {
            window.Show(); Click(Find<Button>(window, "NavFavorites"));
            Invoke(window, "HandleDigit", 2); Invoke(window, "HandleDigit", 7);
            Click(Find<Button>(window, "FavoriteSendCommand"));
            Assert.Equal(0, midi.TotalSendCount);
            Assert.True(Find<Border>(window, "SendFeedback").IsVisible);
            Assert.Equal("Favorite 27 doesn't exist", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Equal("Choose an existing favorite to send.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Invoke(window, "HandleDigit", 1);
            Assert.False(Find<Border>(window, "SendFeedback").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ConfirmationPausesWhileHoveredAndExpiresAfterLeaving()
    {
        var window = CreateWindow(new FakeMidi { OutputOpen = true });
        try
        {
            window.Show(); Invoke(window, "HandleDigit", 8); Invoke(window, "HandleSend"); Dispatcher.UIThread.RunJobs();
            var feedback = Find<Border>(window, "SendFeedback");
            window.MouseMove(feedback.TranslatePoint(new Point(20, 20), window)!.Value); Dispatcher.UIThread.RunJobs();
            await Task.Delay(TimeSpan.FromSeconds(7.2)); Dispatcher.UIThread.RunJobs();
            Assert.True(feedback.IsVisible);
            window.MouseMove(new Point(10, 55)); Dispatcher.UIThread.RunJobs();
            await Task.Delay(TimeSpan.FromSeconds(7.2)); Dispatcher.UIThread.RunJobs();
            Assert.False(feedback.IsVisible);
        }
        finally { window.Close(); }
    }

    internal static void CaptureSendConfirmation(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR") is not { } output) { return; }
        Directory.CreateDirectory(output); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var screenshot = window.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, name + ".png"));
    }
}
