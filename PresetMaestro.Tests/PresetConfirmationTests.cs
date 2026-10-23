using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

// Component evidence only: these replies never open MIDI or qualify hardware.
public sealed partial class PresetConfirmationTests
{
    private static T Find<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static object? Invoke(MainWindow window, string name, params object?[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static T? Field<T>(MainWindow window, string name) =>
        (T?)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static byte ModelByte(DeviceModel model) => model switch
    { DeviceModel.FM3 => 0x11, DeviceModel.AxeFxIII => 0x10, _ => 0x12 };
    private static byte[] Reply(int slot, byte model = 0x12) =>
        SysexProtocol.Frame(model, 0x0d, [(byte)(slot & 127), (byte)(slot >> 7), .. new byte[32]]);
    private static MainWindow Window(DeviceMidi midi, int offset = 0, DeviceModel model = DeviceModel.FM9, string theme = "Light", int width = 1360)
    {
        var window = new MainWindow(new AppSettings { DisplayOffset = offset, DeviceModel = model, Theme = theme }, [], midi,
            saveSettings: _ => { }, saveFavorites: _ => { })
        { Width = width, Height = 850 };
        window.ConnectionState.Device = new(model, "Component fixture");
        window.Show();
        return window;
    }
    private static void Send(MainWindow window, string route, int slot, int offset = 0)
    {
        switch (route)
        {
            case "preset": Invoke(window, "SendPreset", slot + offset); break;
            case "favorite":
                Invoke(window, "SendFavorite", new Favorite
                { Slot = 1, Name = "Favorite", Preset = slot + offset, Scene = 3 }); break;
            case "go-to": Assert.True((bool)Invoke(window, "GoToDevice", slot, null, DeviceModel.FM9)!); break;
        }
    }

    [AvaloniaTheory]
    [InlineData("preset", 0)]
    [InlineData("preset", 1)]
    [InlineData("favorite", 0)]
    [InlineData("favorite", 1)]
    [InlineData("go-to", 0)]
    [InlineData("go-to", 1)]
    public async Task EverySendRouteConfirmsTheActualActivePresetAcrossBankBoundaries(string route, int offset)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        int queries = 0;
        midi.Send = request =>
        {
            if (request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [(byte)(midi.Writes.Last().Scene - 1)])); return; }
            if (request[5] != 0x0d) { return; }
            Assert.Equal(new byte[] { 0x7f, 0x7f }, request[6..8]);
            queries++;
            var write = midi.Writes.Last();
            midi.Reply(Reply(write.Bank * 128 + write.Program));
        };
        var window = Window(midi, offset);
        try
        {
            foreach (int slot in new[] { 127, 128, 255, 256, 511 })
            {
                Send(window, route, slot, offset);
                Assert.Equal(route == "favorite" ? "Confirming preset and scene" : "Confirming preset", Find<TextBlock>(window, "SendFeedbackTitle").Text);
                await window.PresetConfirmationTask;
                Assert.Equal(route == "favorite" ? "Preset and scene confirmed" : "Preset confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
                Assert.Equal(slot + offset, Field<int?>(window, "_currentPreset"));
                Assert.Equal(slot, Field<int?>(window, "_detectedPresetSlot"));
            }
            Assert.Equal(route == "favorite" ? 10 : 5, queries);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("preset", true)]
    [InlineData("favorite", true)]
    [InlineData("go-to", true)]
    [InlineData("preset", false)]
    [InlineData("favorite", false)]
    [InlineData("go-to", false)]
    public async Task WrongPresetAndNoResponseShowErrorsWithoutClaimingSuccess(string route, bool wrongPreset)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request => { if (wrongPreset && request[5] == 0x0d) { midi.Reply(Reply(127)); } };
        var window = Window(midi, offset: 1);
        try
        {
            Send(window, route, 128, offset: 1);
            await window.PresetConfirmationTask.WaitAsync(TimeSpan.FromSeconds(6));
            var feedback = Find<Border>(window, "SendFeedback");
            Assert.True(feedback.IsVisible);
            Assert.Equal(wrongPreset ? "Preset did not change" : route == "favorite" ? "No scene confirmation" : "No preset confirmation", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Contains("129", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            if (wrongPreset) { Assert.Contains("device reports 128", AutomationProperties.GetName(feedback)); }
            Assert.Equal(wrongPreset ? 128 : (int?)null, Field<int?>(window, "_currentPreset"));
            Assert.Null(Field<string>(window, "_currentFavoriteName"));
            Assert.Single(midi.Writes); // Never resend the navigation automatically.
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnOldPresetDuringLoadingCanBecomeTheRequestedPreset()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        int reads = 0;
        midi.Send = request => { if (request[5] == 0x0d) { midi.Reply(Reply(++reads == 1 ? 4 : 128)); } };
        var window = Window(midi);
        try
        {
            Send(window, "preset", 128); await window.PresetConfirmationTask;
            Assert.Equal(2, reads);
            Assert.Equal("Preset confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task GoToSceneOnTheCurrentPresetConfirmsWithoutReloadingItOrRefreshingStoredScenes()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(128)); }
            else if (request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [2])); }
        };
        var window = Window(midi);
        try
        {
            typeof(MainWindow).GetField("_sceneSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 128);
            Assert.True((bool)Invoke(window, "GoToDevice", 128, 3, DeviceModel.FM9)!);
            await window.PresetConfirmationTask;
            await Task.Delay(250);
            Assert.Equal("Preset and scene confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Equal("Scene", Assert.Single(midi.Writes).Kind);
            Assert.Equal(new byte[] { 0x0d, 0x0c, 0x0d }, midi.Sent.Select(frame => frame[5]));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NewerSendCancelsTheOldCheckAndOnlyConfirmsTheFinalPreset()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(256)); }
            else if (request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [2])); }
        };
        var window = Window(midi);
        try
        {
            Send(window, "preset", 128); var old = window.PresetConfirmationTask;
            Send(window, "favorite", 256); await window.PresetConfirmationTask; await old;
            Assert.Equal("Preset and scene confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Contains("Favorite", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Assert.Equal(256, Field<int?>(window, "_currentPreset"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SupersedingAnUnansweredQueryDoesNotShowItsTimeoutOverTheNewSelection()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var firstQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool respond = false;
        midi.Send = request =>
        {
            if (request[5] != 0x0d) { return; }
            if (respond) { midi.Reply(Reply(256)); }
            else { firstQuery.TrySetResult(); }
        };
        var window = Window(midi);
        try
        {
            Send(window, "preset", 128); var old = window.PresetConfirmationTask;
            await firstQuery.Task.WaitAsync(TimeSpan.FromSeconds(2));
            respond = true; Send(window, "go-to", 256);
            await window.PresetConfirmationTask.WaitAsync(TimeSpan.FromSeconds(6)); await old;
            Assert.Equal("Preset confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Equal(256, Field<int?>(window, "_currentPreset"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    public async Task ConfirmationStatesKeepLayoutAndFocusAndExposeTheErrorText(int width, string theme)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request => { if (request[5] == 0x0d) { midi.Reply(Reply(128)); } };
        var window = Window(midi, theme: theme, width: width);
        try
        {
            Find<Button>(window, "NavPresetSender").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Find<Button>(window, "NavPresetSender").Focus();
            Assert.Equal(width, window.ClientSize.Width);
            var origin = Find<Border>(window, "PresetDisplayCard").TranslatePoint(default, window);
            var focus = window.FocusManager!.GetFocusedElement();
            Send(window, "preset", 128);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"confirm-pending-{width}-{theme}");
            await window.PresetConfirmationTask;
            FavoriteEditorTests.CaptureSendConfirmation(window, $"confirm-success-{width}-{theme}");
            Send(window, "favorite", 256);
            await window.PresetConfirmationTask;
            Assert.Contains("Requested preset 256; device reports 128", AutomationProperties.GetName(Find<Border>(window, "SendFeedback")));
            Assert.Same(focus, window.FocusManager.GetFocusedElement());
            Assert.Equal(origin, Find<Border>(window, "PresetDisplayCard").TranslatePoint(default, window));
            FavoriteEditorTests.CaptureSendConfirmation(window, $"confirm-error-{width}-{theme}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectAndProfileChangeCancelPendingFeedback(bool profileChange)
    {
        var window = Window(new DeviceMidi { AllowWrites = true });
        try
        {
            Send(window, "preset", 128); var pending = window.PresetConfirmationTask;
            if (profileChange) { Invoke(window, "ActivateLoadedProfile", "Other", new ProfileSettings(), new List<Favorite>(), false); }
            else { Invoke(window, "Disconnect"); }
            await pending;
            Assert.False(Find<Border>(window, "SendFeedback").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LosingTheTransportWhileConfirmingShowsAnErrorInsteadOfSilentlyCancelling()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var window = Window(midi);
        try
        {
            Send(window, "preset", 128); var pending = window.PresetConfirmationTask;
            midi.FailTransport(); Dispatcher.UIThread.RunJobs(); await pending;
            Assert.Equal("No preset confirmation", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Contains("connection was lost", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Assert.False(midi.OutputOpen);
            Assert.Null(Field<int?>(window, "_currentPreset"));
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(DeviceModel.FM9, 511)]
    [InlineData(DeviceModel.FM3, 511)]
    [InlineData(DeviceModel.AxeFxIII, 1023)]
    public async Task ConfirmationUsesTheConnectedModelAndRejectsOtherModelsAndCorruptReplies(DeviceModel model, int slot)
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi);
        client.SetDeviceModel(model);
        midi.Send = request =>
        {
            Assert.Equal(SysexProtocol.Frame(ModelByte(model), 0x0d, [0x7f, 0x7f]), request);
            midi.Reply(Reply(slot, model == DeviceModel.FM9 ? (byte)0x10 : (byte)0x12));
            var corrupt = Reply(slot, ModelByte(model)); corrupt[^2] ^= 1; midi.Reply(corrupt);
            midi.Reply(Reply(slot, ModelByte(model)));
        };
        Assert.Equal(slot, (await client.ReadPresetConfirmationAsync(CancellationToken.None)).Slot);
    }
}
