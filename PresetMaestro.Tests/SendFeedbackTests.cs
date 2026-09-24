using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaFact]
    public void PresetSendShowsContextualConfirmationOnBothScreens()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi);
        try
        {
            window.Show();
            Invoke(window, "HandleDigit", 8);
            Invoke(window, "HandleSend");

            Assert.Equal(1, midi.PresetSendCount);
            Assert.True(Field<Border>(window, "_senderFeedbackPanel").IsVisible);
            Assert.True(Field<Border>(window, "_favoriteFeedbackPanel").IsVisible);
            Assert.Equal("SENT", Field<TextBlock>(window, "_senderFeedbackTitle").Text);
            Assert.Equal("Preset 8 sent.", Field<TextBlock>(window, "_favoriteFeedbackDetail").Text);
            window.UpdateLayout();
            var display = Find<Border>(window, "PresetDisplayCard");
            var feedback = Find<Border>(window, "SenderSendFeedback");
            var displayOrigin = display.TranslatePoint(new Point(0, 0), window)!.Value;
            var feedbackOrigin = feedback.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.Equal(170, feedback.Bounds.Height);
            Assert.Equal(display.Bounds.Height, feedback.Bounds.Height);
            Assert.Equal(displayOrigin.Y, feedbackOrigin.Y);
            Assert.Equal(16, feedbackOrigin.X - displayOrigin.X - display.Bounds.Width);

            Click(Find<Button>(window, "NavFavorites"));
            Assert.True(Find<Border>(window, "FavoriteSendFeedback").IsVisible);
            Assert.Equal("Profile: Default", Find<TextBlock>(window, "HeaderActiveProfile").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoriteSendShowsContextualConfirmationOnBothScreens()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Invoke(window, "HandleDigit", 1);
            Click(Find<Button>(window, "FavoriteSendCommand"));

            Assert.Equal(1, midi.FavoriteSendCount);
            Assert.Equal("SENT", Field<TextBlock>(window, "_favoriteFeedbackTitle").Text);
            Assert.Contains("Favorite 1", Field<TextBlock>(window, "_favoriteFeedbackDetail").Text);
            Assert.True(Field<Border>(window, "_favoriteFeedbackPanel").IsVisible);
            Assert.True(Field<Border>(window, "_senderFeedbackPanel").IsVisible);

            Click(Find<Button>(window, "NavPresetSender"));
            Assert.True(Find<Border>(window, "SenderSendFeedback").IsVisible);
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
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Invoke(window, "HandleDigit", 2);
            Invoke(window, "HandleDigit", 7);
            Click(Find<Button>(window, "FavoriteSendCommand"));

            Assert.Equal(0, midi.TotalSendCount);
            Assert.True(Field<Border>(window, "_favoriteFeedbackPanel").IsVisible);
            Assert.False(Field<Border>(window, "_senderFeedbackPanel").IsVisible);
            Assert.Equal("Favorite 27 doesn't exist", Field<TextBlock>(window, "_favoriteFeedbackTitle").Text);
            Assert.Equal("Choose an existing favorite to send.", Field<TextBlock>(window, "_favoriteFeedbackDetail").Text);

            Invoke(window, "HandleDigit", 1);
            Assert.False(Field<Border>(window, "_favoriteFeedbackPanel").IsVisible);
        }
        finally { window.Close(); }
    }
}
