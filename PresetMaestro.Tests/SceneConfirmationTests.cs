using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

// In-memory component tests only. Native/hardware acceptance stays with the platform.
public sealed partial class PresetConfirmationTests
{
    private static void SendSceneSelection(MainWindow window, string route, int scene, int offset = 0)
    {
        if (route == "favorite")
        {
            Invoke(window, "SendFavorite", new Favorite { Slot = 1, Name = "Scene favorite", Preset = 128 + offset, Scene = scene });
        }
        else
        {
            typeof(MainWindow).GetField("_sceneSlot", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, route == "go-to-current" ? 128 : 127);
            Assert.True((bool)Invoke(window, "GoToDevice", 128, scene, DeviceModel.FM9)!);
        }
    }

    [AvaloniaTheory]
    [InlineData("favorite", 0)]
    [InlineData("favorite", 1)]
    [InlineData("go-to-current", 0)]
    [InlineData("go-to-current", 1)]
    [InlineData("go-to-other", 0)]
    [InlineData("go-to-other", 1)]
    public async Task EverySceneAndSendRouteRequiresMatchingPresetAndSceneReadback(string route, int offset)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(128)); }
            else if (request[5] == 0x0c)
            {
                Assert.Equal(0x7f, request[6]); // Query; never select a scene through SysEx.
                midi.Reply(SysexProtocol.Frame(0x0c, [(byte)(midi.Writes.Last().Scene - 1)]));
            }
        };
        var window = Window(midi, offset);
        try
        {
            for (int scene = 1; scene <= 8; scene++)
            {
                SendSceneSelection(window, route, scene, offset);
                Assert.Equal("Confirming preset and scene", Find<TextBlock>(window, "SendFeedbackTitle").Text);
                Assert.Null(Field<int?>(window, "_detectedScene"));
                await window.PresetConfirmationTask;
                Assert.Equal("Preset and scene confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
                Assert.Equal(128, Field<int?>(window, "_detectedPresetSlot"));
                Assert.Equal(scene, Field<int?>(window, "_detectedScene"));
                Assert.Equal(scene, Field<int?>(window, "_activeScene"));
            }
            Assert.Equal(8, midi.Writes.Count);
            Assert.All(midi.Writes, write => Assert.Equal(route == "go-to-current" ? "Scene" : "PresetScene", write.Kind));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("favorite", true)]
    [InlineData("favorite", false)]
    [InlineData("go-to-current", true)]
    [InlineData("go-to-current", false)]
    [InlineData("go-to-other", true)]
    [InlineData("go-to-other", false)]
    public async Task WrongSceneAndMissingSceneRepliesShowErrorsForEveryRoute(string route, bool wrongScene)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(128)); }
            else if (wrongScene && request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [1])); }
        };
        var window = Window(midi, offset: 1);
        try
        {
            SendSceneSelection(window, route, 3, offset: 1);
            await window.PresetConfirmationTask.WaitAsync(TimeSpan.FromSeconds(6));
            var feedback = Find<Border>(window, "SendFeedback");
            Assert.True(feedback.IsVisible);
            Assert.Equal(wrongScene ? "Scene did not change" : "No scene confirmation", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Contains("preset 129", AutomationProperties.GetName(feedback));
            if (wrongScene) { Assert.Contains("device reports scene 2", AutomationProperties.GetName(feedback)); }
            Assert.Equal(wrongScene ? 2 : (int?)null, Field<int?>(window, "_activeScene"));
            Assert.Equal(wrongScene ? 2 : (int?)null, Field<int?>(window, "_detectedScene"));
            Assert.Null(Field<string>(window, "_currentFavoriteName"));
            Assert.Single(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SceneCanSettleToTheRequestedValueWithoutResendingTheCommand()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        int scenes = 0;
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(128)); }
            else if (request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [(byte)(++scenes == 1 ? 1 : 2)])); }
        };
        var window = Window(midi);
        try
        {
            SendSceneSelection(window, "go-to-current", 3); await window.PresetConfirmationTask;
            Assert.Equal("Preset and scene confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Equal(2, scenes); Assert.Single(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ANewSceneSendCancelsAnUnansweredSceneQuery()
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var firstSceneQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool respond = false;
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(128)); }
            else if (request[5] == 0x0c)
            {
                if (respond) { midi.Reply(SysexProtocol.Frame(0x0c, [3])); }
                else { firstSceneQuery.TrySetResult(); }
            }
        };
        var window = Window(midi);
        try
        {
            SendSceneSelection(window, "go-to-current", 3); var old = window.PresetConfirmationTask;
            await firstSceneQuery.Task.WaitAsync(TimeSpan.FromSeconds(2));
            respond = true; SendSceneSelection(window, "go-to-current", 4);
            await window.PresetConfirmationTask.WaitAsync(TimeSpan.FromSeconds(6)); await old;
            Assert.Equal("Preset and scene confirmed", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Equal(4, Field<int?>(window, "_activeScene"));
            Assert.Equal(2, midi.Writes.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("disconnect")]
    [InlineData("profile")]
    [InlineData("close")]
    [InlineData("transport")]
    public async Task SceneConfirmationCannotOutliveItsConnectionProfileOrWindow(string action)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        var window = Window(midi);
        try
        {
            SendSceneSelection(window, "favorite", 3); var pending = window.PresetConfirmationTask;
            if (action == "profile") { Invoke(window, "ActivateLoadedProfile", "Other", new ProfileSettings(), new List<Favorite>(), false); }
            else if (action == "transport") { midi.FailTransport(); Dispatcher.UIThread.RunJobs(); }
            else if (action == "close") { window.Close(); }
            else { Invoke(window, "Disconnect"); }
            await pending;
            if (action == "transport")
            {
                Assert.Equal("No scene confirmation", Find<TextBlock>(window, "SendFeedbackTitle").Text);
                Assert.Contains("connection was lost", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            }
            else { Assert.False(Find<Border>(window, "SendFeedback").IsVisible); }
        }
        finally { if (window.IsVisible) { window.Close(); } }
    }

    [Theory]
    [InlineData(DeviceModel.FM9, 511, 7)]
    [InlineData(DeviceModel.FM3, 511, 0)]
    [InlineData(DeviceModel.AxeFxIII, 1023, 7)]
    public async Task SceneConfirmationRejectsWrongModelsInvalidIndexesAndCorruptReplies(DeviceModel model, int slot, int scene)
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi);
        client.SetDeviceModel(model);
        byte modelByte = ModelByte(model);
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(slot, modelByte)); return; }
            Assert.Equal(SysexProtocol.Frame(modelByte, 0x0c, [0x7f]), request);
            midi.Reply(SysexProtocol.Frame(modelByte == 0x12 ? (byte)0x10 : (byte)0x12, 0x0c, [(byte)scene]));
            midi.Reply(SysexProtocol.Frame(modelByte, 0x0c, [8]));
            midi.Reply(SysexProtocol.Frame(modelByte, 0x0c, [0x7f]));
            var bad = SysexProtocol.Frame(modelByte, 0x0c, [(byte)scene]); bad[^2] ^= 1; midi.Reply(bad);
            midi.Reply(SysexProtocol.Frame(modelByte, 0x0c, [(byte)scene]));
        };
        var observed = await client.ReadSceneConfirmationAsync(slot, CancellationToken.None);
        Assert.Equal(slot, observed.Preset.Slot); Assert.Equal(scene, observed.Scene!.Index);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASceneReplyCannotConfirmAChangedPresetEvenIfItReturnsToTheOriginalSlot(bool returnsToOriginal)
    {
        var midi = new DeviceMidi(); using var client = new PresetNameClient(midi);
        int presets = 0;
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(++presets == 1 || returnsToOriginal ? 128 : 256)); }
            else
            {
                if (returnsToOriginal) { midi.EmitPresetChange(); }
                midi.Reply(SysexProtocol.Frame(0x0c, [2]));
            }
        };
        var observed = await client.ReadSceneConfirmationAsync(128, CancellationToken.None);
        Assert.Equal(returnsToOriginal ? 128 : 256, observed.Preset.Slot);
        Assert.Null(observed.Scene);
    }

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    public async Task SceneConfirmationMessagesWrapAndKeepFocusAndLayout(int width, string theme)
    {
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0x0d) { midi.Reply(Reply(128)); }
            else if (request[5] == 0x0c) { midi.Reply(SysexProtocol.Frame(0x0c, [2])); }
        };
        var window = Window(midi, theme: theme, width: width);
        try
        {
            Find<Button>(window, "NavPresetSender").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Find<Button>(window, "NavPresetSender").Focus();
            var focus = window.FocusManager!.GetFocusedElement();
            var origin = Find<Border>(window, "PresetDisplayCard").TranslatePoint(default, window);
            Assert.Equal(width, window.ClientSize.Width);
            SendSceneSelection(window, "go-to-current", 3);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"scene-pending-{width}-{theme}");
            await window.PresetConfirmationTask;
            FavoriteEditorTests.CaptureSendConfirmation(window, $"scene-confirmed-{width}-{theme}");
            SendSceneSelection(window, "go-to-current", 4);
            await window.PresetConfirmationTask;
            Assert.Equal("Scene did not change", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            Assert.Contains("device reports scene 3", AutomationProperties.GetName(Find<Border>(window, "SendFeedback")));
            Assert.Equal(origin, Find<Border>(window, "PresetDisplayCard").TranslatePoint(default, window));
            Assert.Same(focus, window.FocusManager.GetFocusedElement());
            FavoriteEditorTests.CaptureSendConfirmation(window, $"scene-error-{width}-{theme}");
        }
        finally { window.Close(); }
    }
}
