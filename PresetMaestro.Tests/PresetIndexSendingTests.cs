using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed partial class DeviceNavigationTests
{
    private static void Press(MainWindow window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Number(MainWindow window, string number)
    {
        foreach (char digit in number) { Press(window, Enum.Parse<PhysicalKey>("Digit" + digit)); }
    }

    [AvaloniaTheory]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    public async Task IndexNumberEntrySendsPresetInsteadOfFavoriteOrSelection(int channel, int expectedChannel)
    {
        var (window, midi) = Window(channel);
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavFavorites"));
            Number(window, "1");
            Click(Find<Button>(window, "NavPresetIndex"));
            var list = Find<ListBox>(window, "IndexPresetList");
            list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); list.Focus();
            Assert.Equal("001", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Number(window, "256");
            Assert.Equal("256", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Assert.Empty(midi.Writes);
            Press(window, PhysicalKey.Enter);
            Assert.Equal(("Preset", 1, 127, 0, 0, expectedChannel), Assert.Single(midi.Writes));
            Assert.Contains("Sent preset 256", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Number(window, "261");
            Click(Find<Button>(window, "IndexSendCommand"));
            Assert.Equal(("Preset", 2, 4, 0, 0, expectedChannel), midi.Writes[1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(512, "512", 3, 127, 1)]
    [InlineData(1024, "1024", 7, 127, 1)]
    [InlineData(1024, "1023", 7, 127, 0)]
    [InlineData(512, "0", 0, 0, 0)]
    public async Task IndexNumberEntryUsesTheDeviceCapacity(int capacity, string number, int bank, int program, int displayOffset)
    {
        var (window, midi) = Window(capacity: capacity, displayOffset: displayOffset);
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex"));
            var readout = Find<Button>(window, "IndexPresetReadout"); readout.Focus();
            var settings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            settings.AutoSend = true; settings.AutoSendDelayMs = 60000;
            Number(window, number);
            Assert.Equal(number, Find<TextBlock>(window, "IndexPresetNumber").Text);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"index-toolbar-number-{capacity}-{displayOffset}");
            Assert.Empty(midi.Writes);
            Press(window, PhysicalKey.Enter);
            Assert.Equal(("Preset", bank, program, 0, 0, 5), Assert.Single(midi.Writes));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task IndexEntrySupportsEditingAndLeavesSearchAndBrowsingSeparate()
    {
        var (window, midi) = Window();
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex"));
            var search = Find<TextBox>(window, "IndexSearchInput"); search.Focus();
            Number(window, "126");
            window.KeyTextInput("126"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("126", search.Text);
            Assert.Equal("---", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Press(window, PhysicalKey.Enter);
            Assert.Empty(midi.Writes);
            Click(Find<Button>(window, "IndexClearSearch"));
            var list = Find<ListBox>(window, "IndexPresetList"); list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); ((ListBoxItem)list.SelectedItem!).Focus();
            Press(window, PhysicalKey.ArrowDown);
            Assert.Equal(1, list.SelectedIndex); Assert.Empty(midi.Writes);
            Find<Button>(window, "IndexPresetReadout").Focus();
            Number(window, "261"); Press(window, PhysicalKey.Backspace);
            Assert.Equal("26", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Click(Find<Button>(window, "IndexClearCommand"));
            Assert.Equal("002", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Number(window, "999");
            Assert.False(Find<Button>(window, "IndexSendCommand").IsEnabled);
            Press(window, PhysicalKey.Enter);
            Assert.Contains("out of range", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Empty(midi.Writes);
            Press(window, PhysicalKey.Delete);
            Assert.Equal("002", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Number(window, "0"); Press(window, PhysicalKey.Enter);
            Assert.Empty(midi.Writes);
            Click(Find<Button>(window, "NavFavorites"));
            Assert.Equal("---", (Find<Border>(window, "FavoritePresetReadout").Child as TextBlock)!.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    public async Task IndexToolbarSendsSelectedPresetAndSceneAndSupportsKeyboardFocus(int width, string theme)
    {
        int height = width == 1000 ? 640 : 850;
        var (window, midi) = Window(width: width, theme: theme, height: height);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavPresetIndex"));
            Assert.Equal(height, window.Bounds.Height);
            Assert.False(Find<Button>(window, "IndexSendCommand").IsEnabled);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"index-toolbar-offline-{width}-{theme}");
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 260; Dispatcher.UIThread.RunJobs();
            var readout = Find<Button>(window, "IndexPresetReadout");
            var send = Find<Button>(window, "IndexSendCommand");
            readout.Focus();
            Press(window, PhysicalKey.Tab);
            Assert.Same(Find<Button>(window, "IndexClearCommand"), window.FocusManager?.GetFocusedElement());
            Press(window, PhysicalKey.Tab); Assert.Same(send, window.FocusManager?.GetFocusedElement());
            Press(window, PhysicalKey.Space);
            Assert.Equal(("Preset", 2, 4, 0, 0, 5), Assert.Single(midi.Writes));
            FavoriteEditorTests.CaptureSendConfirmation(window, $"index-toolbar-preset-{width}-{theme}");
            var row = Find<Grid>(window, "IndexScene3Row");
            row.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose();
            var point = row.TranslatePoint(new Point(5, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Send preset 261, scene 3", AutomationProperties.GetName(send));
            Click(send);
            Assert.Equal(("Scene", 0, 0, 3, 77, 5), midi.Writes[1]);
            Assert.Same(row, window.FocusManager?.GetFocusedElement());
            FavoriteEditorTests.CaptureSendConfirmation(window, $"index-toolbar-scene-{width}-{theme}");
            readout.Focus(); Number(window, "256");
            FavoriteEditorTests.CaptureSendConfirmation(window, $"index-toolbar-entry-{width}-{theme}");
            Click(send);
            Assert.Equal(("Preset", 1, 127, 0, 0, 5), midi.Writes[2]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task IndexToolbarDoesNotSendOfflineDuringReadsOrToAnotherModel()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            Find<Button>(window, "IndexPresetReadout").Focus(); Number(window, "261");
            Press(window, PhysicalKey.Enter); Assert.Empty(midi.Writes);
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Find<Button>(window, "IndexPresetReadout").Focus(); Number(window, "261");
            using var cancellation = new CancellationTokenSource();
            var reading = typeof(MainWindow).GetField("_presetNamesCts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            reading.SetValue(window, cancellation);
            Press(window, PhysicalKey.Enter); Assert.Empty(midi.Writes);
            reading.SetValue(window, null);
            window.ConnectionState.Device = new PresetMaestro.Midi.FractalDeviceInformation(DeviceModel.FM3, null);
            Press(window, PhysicalKey.Enter); Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task IndexAutoSendWaitsForFourDigitsAndCancelsWhenLeavingThePage()
    {
        var (window, midi) = Window(capacity: 1024);
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex")); Find<Button>(window, "IndexPresetReadout").Focus();
            var settings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            settings.AutoSend = true; settings.AutoSendDelayMs = 30;
            Number(window, "102"); await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Writes);
            Number(window, "4"); await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.Equal(("Preset", 7, 127, 0, 0, 5), Assert.Single(midi.Writes));
            Number(window, "1023"); Click(Find<Button>(window, "NavPresetSender"));
            await Task.Delay(100); Dispatcher.UIThread.RunJobs(); Assert.Single(midi.Writes);
            Click(Find<Button>(window, "NavPresetIndex"));
            Assert.Equal("---", Find<TextBlock>(window, "IndexPresetNumber").Text);
            settings.KeyboardEntryEnabled = false;
            Find<Button>(window, "IndexPresetReadout").Focus(); Number(window, "1024");
            Assert.Equal("---", Find<TextBlock>(window, "IndexPresetNumber").Text);
            Assert.Single(midi.Writes);
        }
        finally { window.Close(); }
    }
}
