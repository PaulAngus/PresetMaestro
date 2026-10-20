using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    public void AutoSendOptionCanBeToggledWithKeyboardWithoutSending(int width, string theme)
    {
        var settings = new AppSettings { Theme = theme };
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi, settings: settings);
        try
        {
            window.Width = width; window.Height = 850; window.Show();
            Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
            var option = Find<CheckBox>(window, "AutoSend");
            option.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(settings.AutoSend);
            Assert.Same(option, window.FocusManager!.GetFocusedElement());
            Assert.Equal(0, midi.TotalSendCount);
            CaptureSendConfirmation(window, $"auto-send-option-{width}-{theme}");
        }
        finally { window.Close(); }
    }

    private static void EnterPresetNumber(MainWindow window, string number)
    {
        foreach (char digit in number)
        {
            var key = Enum.Parse<PhysicalKey>("Digit" + digit);
            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyReleaseQwerty(key, RawInputModifiers.None);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("different favorite")]
    [InlineData("reopened favorite")]
    [InlineData("new draft")]
    [InlineData("edited selection")]
    [InlineData("disconnect")]
    public async Task UseCurrentLateReplyCannotReplaceNewerUserWork(string action)
    {
        var midi = new DeviceMidi();
        var favorite = Clone(Sample);
        var other = new Favorite { Id = 20, Slot = 2, Name = "Other", Preset = 202, Scene = 7 };
        var window = new MainWindow(new AppSettings(), [favorite, other], midi,
            saveSettings: _ => { }, saveFavorites: _ => { });
        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int presetReads = 0;
        midi.Send = frame =>
        {
            if (frame[5] == 0x0d)
            {
                if (++presetReads == 1) { requested.TrySetResult(true); }
                else { midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Device preset")); }
            }
            else { midi.Reply(SysexProtocol.Frame(0x0c, [3])); }
        };
        try
        {
            ShowEditor(window, favorite);
            var pending = (Task)typeof(MainWindow).GetMethod("UseCurrentFavoriteStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            switch (action)
            {
                case "different favorite": ShowEditor(window, other); break;
                case "reopened favorite":
                    Click(Find<Button>(window, "FavoriteCancel")); ShowEditor(window, favorite); break;
                case "new draft":
                    Click(Find<Button>(window, "FavoriteCancel")); Click(Find<Button>(window, "FavoriteAdd")); break;
                case "edited selection":
                    Field<NumericUpDown>(window, "_favPresetSpinner").Value = 222;
                    Field<NumericUpDown>(window, "_favSceneSpinner").Value = 6; break;
                case "disconnect": Invoke(window, "Disconnect"); break;
            }
            var preset = Field<NumericUpDown>(window, "_favPresetSpinner").Value;
            var scene = Field<NumericUpDown>(window, "_favSceneSpinner").Value;
            midi.Reply(SceneProtocolTests.Response(0x0d, [5, 0], "Device preset"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(preset, Field<NumericUpDown>(window, "_favPresetSpinner").Value);
            Assert.Equal(scene, Field<NumericUpDown>(window, "_favSceneSpinner").Value);
            Assert.Equal(action != "disconnect", Find<Button>(window, "FavoriteUseCurrent").IsEnabled);
            Assert.Equal(101, favorite.Preset);
            Assert.Equal(202, other.Preset);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, "1023")]
    [InlineData(1, "1024")]
    public void PresetSenderCanEnterAndSendTheHighestSupportedPreset(int offset, string number)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var window = new MainWindow(new AppSettings { DeviceModel = DeviceModel.AxeFxIII, DisplayOffset = offset }, [], midi,
            saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Show(); Click(Find<Button>(window, "NavPresetSender"));
            EnterPresetNumber(window, number);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(("Preset", 7, 127, 0, 0, 1), Assert.Single(midi.Writes));
            Assert.Equal($"Preset {number} sent.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PresetSenderAutoSendWaitsForTheWholeFourDigitNumberAndClearCancelsIt()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var window = new MainWindow(new AppSettings { DeviceModel = DeviceModel.AxeFxIII, DisplayOffset = 1, AutoSend = true, AutoSendDelayMs = 30 }, [], midi,
            saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Show(); Click(Find<Button>(window, "NavPresetSender"));
            EnterPresetNumber(window, "102");
            await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Writes);
            EnterPresetNumber(window, "4");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (midi.Writes.Count == 0) { await Task.Delay(5, deadline.Token); }
            Assert.Equal(("Preset", 7, 127, 0, 0, 1), Assert.Single(midi.Writes));
            EnterPresetNumber(window, "1023");
            window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
            await Task.Delay(100); Dispatcher.UIThread.RunJobs();
            Assert.Single(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UseCurrentUpdatesOnlyTheDraftAndSavePersistsTheReadSelection()
    {
        var midi = new DeviceMidi();
        var favorite = Clone(Sample);
        Favorite? saved = null;
        var window = new MainWindow(new AppSettings { DisplayOffset = 1 }, [favorite], midi,
            saveSettings: _ => { }, saveFavorites: favorites => saved = Clone(Assert.Single(favorites)));
        midi.Send = frame => midi.Reply(frame[5] == 0x0d
            ? SceneProtocolTests.Response(0x0d, [5, 0], "Device preset") : SysexProtocol.Frame(0x0c, [3]));
        try
        {
            ShowEditor(window, favorite);
            Click(Find<Button>(window, "FavoriteUseCurrent"));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!Find<Button>(window, "FavoriteUseCurrent").IsEnabled) { await Task.Delay(5, deadline.Token); }
            Assert.Equal(6, Field<NumericUpDown>(window, "_favPresetSpinner").Value);
            Assert.Equal(4, Field<NumericUpDown>(window, "_favSceneSpinner").Value);
            Assert.Equal(101, favorite.Preset); Assert.Equal(2, favorite.Scene); Assert.Null(saved);
            Click(Find<Button>(window, "FavoriteSave"));
            Assert.Equal(6, saved!.Preset); Assert.Equal(4, saved.Scene);
            Assert.Equal("Clean", saved.Name);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(DeviceModel.FM9, 0, 511)]
    [InlineData(DeviceModel.FM9, 1, 512)]
    [InlineData(DeviceModel.AxeFxIII, 0, 1023)]
    [InlineData(DeviceModel.AxeFxIII, 1, 1024)]
    public void NextPreviousAndLastRespectDeviceBoundaries(DeviceModel model, int offset, int maximum)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var window = new MainWindow(new AppSettings { DeviceModel = model, DisplayOffset = offset }, [], midi,
            saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Show(); Click(Find<Button>(window, "NavPresetSender"));
            EnterPresetNumber(window, maximum.ToString()); Invoke(window, "HandleSend");
            Invoke(window, "HandleNext");
            Invoke(window, "HandlePrev");
            Invoke(window, "HandleLast");
            int bank = (maximum - offset) / 128;
            Assert.Equal(new[] { ("Preset", bank, 127, 0, 0, 1), ("Preset", 0, 0, 0, 0, 1),
                ("Preset", bank, 127, 0, 0, 1), ("Preset", 0, 0, 0, 0, 1) }, midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPresetOrFavoriteSendDoesNotReplaceTheLastSuccessfulSelection(bool favoriteSend)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var favorite = Clone(Sample);
        var window = new MainWindow(new AppSettings(), [favorite], midi, saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Show(); Invoke(window, "SendPreset", 10);
            midi.FailWrites = true;
            if (favoriteSend) { Invoke(window, "SendFavorite", favorite); }
            else { Invoke(window, "SendPreset", 20); }
            Assert.False(Find<Border>(window, "SendFeedback").IsVisible && Find<TextBlock>(window, "SendFeedbackTitle").Text == "SENT");
            midi.FailWrites = false;
            Invoke(window, "HandleNext"); Invoke(window, "HandleLast");
            Assert.Equal(("Preset", 0, 11, 0, 0, 1), midi.Writes[2]);
            Assert.Equal(("Preset", 0, 10, 0, 0, 1), midi.Writes[3]);
            Assert.Equal("Preset 10 sent.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, true, 3, 0)]
    [InlineData(false, true, 2, 1)]
    [InlineData(true, true, 3, 1)]
    [InlineData(true, false, 2, 0)]
    public void MidiEntryHonorsPortFilterAndExplicitDebugBypass(bool debug, bool enabled, int inputChannel, int expectedWrites)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var settings = new AppSettings
        {
            MidiChannel = 7,
            DebugMode = debug,
            MidiEntryEnabled = enabled,
            NoteInputChannels = new() { ["Controller"] = 2 }
        };
        var window = new MainWindow(settings, [], midi, saveSettings: _ => { }, saveFavorites: _ => { });
        try
        {
            window.Show();
            midi.EmitNote(new NoteOnEventArgs(36, 100, inputChannel, "Controller"));
            midi.EmitNote(new NoteOnEventArgs(46, 100, inputChannel, "Controller"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expectedWrites, midi.Writes.Count);
            if (expectedWrites > 0) { Assert.Equal(("Preset", 0, 1, 0, 0, 7), Assert.Single(midi.Writes)); }
        }
        finally { window.Close(); }
    }
}
