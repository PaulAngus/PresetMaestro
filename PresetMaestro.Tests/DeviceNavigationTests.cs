using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class DeviceNavigationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-navigation-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void DoubleClick(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(55, 14), window)!.Value;
        DoubleClick(window, point, point);
    }
    private static void DoubleClick(Window window, Point first, Point second)
    {
        window.MouseDown(first, MouseButton.Left); window.MouseUp(first, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        window.MouseMove(second);
        window.MouseDown(second, MouseButton.Left); window.MouseUp(second, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
    private (MainWindow Window, DeviceMidi Midi) Window(int channel = 5, int width = 1440, string theme = "Light")
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        settings.Theme = theme; settings.MidiInputPort = settings.MidiOutputPort = "FM9";
        var device = new IndexDevice(Guid.NewGuid(), "Default", FractalDeviceVariant.FM9, "12.00");
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex
        {
            Device = device,
            PresetMapping = new(channel, 1, 77),
            Committed = new IndexScan { Status = "Complete", FinishedAt = DateTimeOffset.UtcNow, Firmware = "12.00", Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot, slot => FractalIndexWorkflowTests.Preset(slot)) },
        });
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi { AllowWrites = true };
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 8) { midi.Reply(FractalDeviceInformationTests.Frame(0x12, 8, [12, 0, 0, 1])); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.SyntheticName("Stage")); }
        };
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = 850, ThruInputRetryDelay = TimeSpan.Zero };
        window.Show(); return (window, midi);
    }

    [AvaloniaTheory]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    public async Task GoToUsesSlotBankChannelAndSceneMappingWithoutReloadingTheCurrentPreset(int channel, int expectedChannel)
    {
        var (window, midi) = Window(channel);
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex"));
            var list = Find<ListBox>(window, "IndexPresetList"); list.SelectedIndex = 255; Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Writes);
            Click(Find<Button>(window, "IndexGoToPreset"));
            Assert.Equal(("Preset", 1, 127, 0, 0, expectedChannel), Assert.Single(midi.Writes));
            Click(Find<Button>(window, "IndexScene3GoTo"));
            Assert.Equal(("Scene", 0, 0, 3, 77, expectedChannel), midi.Writes[1]);
            list.SelectedIndex = 260; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexScene5GoTo"));
            Assert.Equal(("PresetScene", 2, 4, 5, 77, expectedChannel), midi.Writes[2]);
            Assert.Contains("Sent preset 261, scene 5", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Assert.True(Find<Border>(window, "SendFeedback").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DoubleClickLoadsTheClickedPresetAndSceneOnce()
    {
        var (window, midi) = Window();
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close(); Click(Find<Button>(window, "NavPresetIndex"));
            Find<TextBox>(window, "IndexSearchInput").Text = "Plexis+ACs"; Dispatcher.UIThread.RunJobs();
            var item = (ListBoxItem)Assert.Single(Find<ListBox>(window, "IndexPresetList").Items)!;
            DoubleClick(window, item);
            Assert.Equal(("Preset", 0, 125, 0, 0, 5), Assert.Single(midi.Writes));
            Assert.True(Find<Border>(window, "SendFeedback").IsVisible);
            Assert.Equal("Sent preset 126.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            DoubleClick(window, Find<Grid>(window, "IndexScene2Row"));
            Assert.Equal(("Scene", 0, 0, 2, 77, 5), midi.Writes[1]); Assert.Equal(2, midi.Writes.Count);
            Assert.Equal("Sent preset 126, scene 2.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    public async Task IndexAndAmpResultsShowTheSameConfirmationWithoutMovingContent(int width, string theme)
    {
        var (window, midi) = Window(width: width, theme: theme);
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex"));
            Find<TextBox>(window, "IndexSearchInput").Text = "Plexis+ACs"; Dispatcher.UIThread.RunJobs();
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            var goTo = Find<Button>(window, "IndexGoToPreset"); goTo.Focus();
            var results = Find<Grid>(window, "IndexResultsPane");
            var search = Find<Border>(window, "IndexSearchEntry");
            var bounds = (results.TranslatePoint(default, window), results.Bounds.Size, search.TranslatePoint(default, window));
            Click(goTo);
            Assert.Equal(bounds, (results.TranslatePoint(default, window), results.Bounds.Size, search.TranslatePoint(default, window)));
            Assert.Same(goTo, window.FocusManager!.GetFocusedElement());
            var feedback = Find<Border>(window, "SendFeedback");
            Assert.True(feedback.IsVisible);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"index-sent-{width}-{theme}");
            Click(Find<Button>(window, "IndexScene2GoTo"));
            Assert.Equal("Sent preset 126, scene 2.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Assert.Equal(bounds, (results.TranslatePoint(default, window), results.Bounds.Size, search.TranslatePoint(default, window)));
            FavoriteEditorTests.CaptureSendConfirmation(window, $"scene-sent-{width}-{theme}");

            Click(Find<Button>(window, "NavAmps"));
            var amps = Find<ListBox>(window, "AmpsDirectory");
            amps.SelectedItem = amps.Items.OfType<ListBoxItem>().Single(i => ((BrowserAmpFamily)i.Tag!).Variants.Any(v => v.ModelId == 141));
            amps.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "AmpsFindPresets"));
            var ampResults = Find<ListBox>(window, "IndexPresetList");
            var item = ampResults.Items.OfType<ListBoxItem>().Single(i => ((IndexedPreset)i.Tag!).Slot == 0);
            ampResults.ScrollIntoView(item); Dispatcher.UIThread.RunJobs();
            int previous = midi.Writes.Count;
            DoubleClick(window, item);
            Assert.Equal(previous + 1, midi.Writes.Count);
            Assert.Same(feedback, Find<Border>(window, "SendFeedback"));
            Assert.True(feedback.IsVisible);
            Assert.Equal("Sent preset 001.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"amp-preset-sent-{width}-{theme}");
            Click(Find<Button>(window, "IndexGoToPreset"));
            Assert.Equal(previous + 2, midi.Writes.Count);
            Assert.Equal("Sent preset 001.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Click(Find<Button>(window, "IndexScene3GoTo"));
            Assert.Equal("Sent preset 001, scene 3.", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"amp-scene-sent-{width}-{theme}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1440)]
    [InlineData(1000)]
    public async Task SceneDoubleClickCanCrossFromItsBackgroundToItsLabel(int width)
    {
        var (window, midi) = Window();
        try
        {
            window.Width = width;
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close(); Click(Find<Button>(window, "NavPresetIndex"));
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 260; Dispatcher.UIThread.RunJobs();
            var row = Find<Grid>(window, "IndexScene2Row");
            var title = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text!.StartsWith("2   ", StringComparison.Ordinal));
            var first = title.TranslatePoint(new Point(-1, title.Bounds.Height / 2), window)!.Value;
            var second = title.TranslatePoint(new Point(1, title.Bounds.Height / 2), window)!.Value;
            window.MouseDown(first, MouseButton.Left); window.MouseUp(first, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Writes);
            window.MouseMove(second);
            window.MouseDown(second, MouseButton.Left); window.MouseUp(second, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(("PresetScene", 2, 4, 2, 77, 5), Assert.Single(midi.Writes));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SceneRowSupportsKeyboardInvocationAndKeepsButtonActionsSeparate()
    {
        var (window, midi) = Window();
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close(); Click(Find<Button>(window, "NavPresetIndex"));
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 260; Dispatcher.UIThread.RunJobs();
            var row = Find<Grid>(window, "IndexScene2Row");
            var point = row.TranslatePoint(new Point(5, 14), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Writes);
            Assert.Same(row, window.FocusManager?.GetFocusedElement());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal(("PresetScene", 2, 4, 2, 77, 5), Assert.Single(midi.Writes));

            var goTo = Find<Button>(window, "IndexScene2GoTo");
            point = goTo.TranslatePoint(new Point(goTo.Bounds.Width / 2, goTo.Bounds.Height / 2), window)!.Value;
            DoubleClick(window, point, point);
            Assert.Equal(3, midi.Writes.Count);
            Assert.All(midi.Writes.Skip(1), write => Assert.Equal(("Scene", 0, 0, 2, 77, 5), write));

            var tags = Find<Button>(window, "IndexScene2Tags");
            point = tags.TranslatePoint(new Point(tags.Bounds.Width / 2, tags.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Single(window.OwnedWindows).Close();
            Assert.Equal(3, midi.Writes.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NavigationIsUnavailableOfflineDuringReadsForAnotherModelAndAfterDisconnect()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); Find<ListBox>(window, "IndexPresetList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            var goTo = (DeviceGoToButton)Find<Button>(window, "IndexGoToPreset");
            goTo.Refresh(); Assert.False(goTo.IsEnabled); Assert.False(goTo.Go());
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            goTo.Refresh(); Assert.True(goTo.IsEnabled);
            using var cancellation = new CancellationTokenSource();
            typeof(MainWindow).GetField("_presetNamesCts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, cancellation);
            Assert.False(goTo.Go());
            typeof(MainWindow).GetField("_presetNamesCts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
            window.ConnectionState.Device = new PresetMaestro.Midi.FractalDeviceInformation(DeviceModel.FM3, null);
            Assert.False(goTo.Go());
            typeof(MainWindow).GetMethod("Disconnect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.False(goTo.Go()); Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedSendKeepsTheCurrentPresetUnchangedAndShowsAnError()
    {
        var (window, midi) = Window();
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close(); Click(Find<Button>(window, "NavPresetIndex"));
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 260; Dispatcher.UIThread.RunJobs();
            midi.FailWrites = true;
            Click(Find<Button>(window, "IndexGoToPreset"));
            Assert.Null(typeof(MainWindow).GetField("_sceneSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Assert.Contains("could not be reached", Find<TextBlock>(window, "SendFeedbackDetail").Text);
            Assert.Equal("Could not go to selection", Find<TextBlock>(window, "SendFeedbackTitle").Text);
            midi.FailWrites = false;
            Click(Find<Button>(window, "IndexScene5GoTo"));
            Assert.Equal(("PresetScene", 2, 4, 5, 77, 5), midi.Writes[1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PickersAuditionWithExplicitActionOrDoubleClickAndKeepDraftSelectionSeparate()
    {
        int? presetSent = null, sceneSent = null;
        var preset = new PresetSelectionWindow(new Dictionary<int, string> { [0] = "First", [1] = "Second" }, 1, displayOffset: 1,
            goTo: slot => { presetSent = slot; return true; }, navigationUnavailable: () => null);
        var scene = new SceneSelectionWindow(["Clean", "Lead"], 2, goTo: number => { sceneSent = number; return true; }, navigationUnavailable: () => null);
        try
        {
            preset.Show(); Dispatcher.UIThread.RunJobs(); Click(Find<Button>(preset, "GoToPresetSelection"));
            Assert.Equal(1, presetSent); Assert.True(preset.IsVisible);
            DoubleClick(preset, (ListBoxItem)Find<ListBox>(preset, "PresetList").SelectedItem!);
            Assert.Equal(1, presetSent); Assert.True(preset.IsVisible);
            scene.Show(); Dispatcher.UIThread.RunJobs(); Click(Find<Button>(scene, "GoToSceneSelection"));
            Assert.Equal(2, sceneSent); Assert.True(scene.IsVisible);
            DoubleClick(scene, (ListBoxItem)Find<ListBox>(scene, "SceneList").SelectedItem!);
            Assert.Equal(2, sceneSent); Assert.True(scene.IsVisible);
        }
        finally { preset.Close(); scene.Close(); }
    }
}
