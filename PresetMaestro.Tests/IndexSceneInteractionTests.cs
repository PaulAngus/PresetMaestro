using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed partial class DeviceNavigationTests
{
    [AvaloniaFact]
    public void PresetSearchExcludesNamesThatOnlyMatchScenes()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var cache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            cache.Committed!.Presets[0] = cache.Committed.Presets[0] with { Name = "Bassman Tweed", SceneNames = ["Jum scene"], Amps = [] };
            cache.Committed.Presets[1] = cache.Committed.Presets[1] with { Name = "Jumped Plexis", SceneNames = ["Clean"], Amps = [] };
            var profile = (IndexProfile)typeof(MainWindow).GetField("_indexProfile", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            profile.GetOrCreate(cache.Device.Id, cache.Committed.Presets[0], "12.00").Tags = ["jum"];
            var search = Find<TextBox>(window, "IndexSearchInput");
            search.Focus(); search.Text = "jum";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Press(window, PhysicalKey.Enter);
            Assert.Equal(1, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Contains("Text: jum", Find<Border>(window, "IndexSearchEntry").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Click(Find<Button>(window, "IndexClearSearch"));
            Find<TextBox>(window, "IndexSearchInput").Text = "jum scene";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Find<TextBox>(window, "IndexSearchInput").Text = "Jumped Plexis";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Find<ListBox>(window, "IndexPresetList").ItemCount);
            IndexView(window, 1);
            Find<TextBox>(window, "IndexSearchInput").Text = "jum scene";
            Dispatcher.UIThread.RunJobs();
            var scene = Assert.IsType<IndexedScene>(Assert.Single(Find<ListBox>(window, "IndexSceneList").Items));
            Assert.Equal(0, scene.Preset.Slot);
            Assert.Equal(0, scene.Index);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    private static void IndexView(MainWindow window, int view)
    {
        Find<TabStrip>(window, "IndexViewTabs").SelectedIndex = view; Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void SceneNamesRemainTextAndTagSuggestionsUseOnlyCurrentSceneTags()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var cache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var profile = (IndexProfile)typeof(MainWindow).GetField("_indexProfile", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            cache.Committed!.Presets[0] = cache.Committed.Presets[0] with { SceneNames = ["Clean", "Clean", "Warm CLEAN", "Lead", "Rhythm", "Solo", "Ambient", ""] };
            var tags = profile.GetOrCreate(cache.Device.Id, cache.Committed.Presets[0], "12.00");
            tags.Tags = ["Preset only"]; tags.SceneTags[0] = ["Stage"]; tags.SceneTags[1] = ["Clean"];
            profile.Annotations.Add(new PresetAnnotation { DeviceId = cache.Device.Id, Slot = 1, ContentSha256 = "old", Firmware = "12.00", SceneTags = { [0] = ["Obsolete"] } });
            IndexView(window, 1);
            var search = Find<TextBox>(window, "IndexSearchInput"); search.Focus(); search.Text = "clean"; Dispatcher.UIThread.RunJobs();
            var list = Find<ListBox>(window, "IndexSceneList"); Assert.Equal(3, list.ItemCount);
            Press(window, PhysicalKey.Enter); Assert.Equal(3, list.ItemCount);
            Assert.Contains("Text: clean", Find<Border>(window, "IndexSearchEntry").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Click(Find<Button>(window, "IndexClearSearch"));
            search.Text = "Preset only"; Dispatcher.UIThread.RunJobs();
            Assert.Single(Find<ListBox>(window, "IndexSearchSuggestions").Items);
            search.Text = "Obsolete"; Dispatcher.UIThread.RunJobs();
            Assert.Single(Find<ListBox>(window, "IndexSearchSuggestions").Items);
            search.Text = "Clean"; Dispatcher.UIThread.RunJobs();
            var suggestion = Find<ListBox>(window, "IndexSearchSuggestions").Items.OfType<ListBoxItem>().Select(i => (Button)i.Content!).Single(b => Equals(b.Content, "Scene tag: Clean"));
            Click(suggestion); Assert.Single(list.Items); Assert.Equal(1, ((IndexedScene)list.Items[0]!).Index);
            IndexView(window, 0); IndexView(window, 1); Assert.Single(list.Items);
            var item = (ListBoxItem)list.ContainerFromIndex(0)!; item.Focus(); Press(window, PhysicalKey.Enter);
            Assert.Equal(1, ((IndexedScene)list.SelectedItem!).Index); Assert.Empty(midi.Writes);
            Click(Find<Button>(window, "IndexScene2Tags")); var dialog = Assert.Single(window.OwnedWindows);
            Find<AutoCompleteBox>(dialog, "IndexTagEditorInput").Text = "Shared scene tag";
            Click(Find<Button>(dialog, "IndexSaveTags"));
            var saved = IndexJson.ReadProfile(new ProfileStore(_directory).LoadProfile("Default").FractalIndex);
            Assert.Contains("Shared scene tag", saved.Find(cache.Device.Id, cache.Committed.Presets[0], "12.00")!.SceneTags[1]);
            Assert.Contains("Shared scene tag", ((IndexedScene)list.SelectedItem!).Tags); Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AmpSearchOpensPresetsAndLeavingItRestoresTheSceneSearch()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); IndexView(window, 1);
            Find<TextBox>(window, "IndexSearchInput").Text = "OD"; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavAmps"));
            var amps = Find<ListBox>(window, "AmpsDirectory");
            amps.SelectedItem = amps.Items.OfType<ListBoxItem>().Single(i => ((BrowserAmpFamily)i.Tag!).Variants.Any(v => v.ModelId == 141));
            amps.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "AmpsFindPresets"));
            Assert.Equal(0, Find<TabStrip>(window, "IndexViewTabs").SelectedIndex);
            Assert.True(Find<StackPanel>(window, "IndexAmpDetailsHeading").IsVisible);
            Click(Find<Button>(window, "IndexBackToAmps")); Assert.True(Find<Button>(window, "AmpsFindPresets").IsFocused);
            Click(Find<Button>(window, "AmpsFindPresets")); IndexView(window, 1);
            Assert.False(Find<StackPanel>(window, "IndexAmpDetailsHeading").IsVisible);
            Assert.Equal("OD", Find<TextBox>(window, "IndexSearchInput").Text);
            Assert.Equal(2048, Find<ListBox>(window, "IndexSceneList").ItemCount);
            Find<TabStripItem>(window, "IndexViewScenes").Focus(); Press(window, PhysicalKey.Enter);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileOrDeviceAssignmentCannotRestoreHiddenSceneSelection(bool changeDevice)
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); IndexView(window, 1);
            Find<ListBox>(window, "IndexSceneList").SelectedIndex = 7; Dispatcher.UIThread.RunJobs();
            IndexView(window, 0);
            var store = (ProfileStore)typeof(MainWindow).GetField("_profileStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            if (changeDevice)
            {
                var live = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var device = live.Device with { Id = Guid.NewGuid(), Name = "Second rig" };
                new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex { Device = device, PresetMapping = live.PresetMapping, Committed = live.Committed });
                typeof(MainWindow).GetMethod("AssignIndexLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [device]); Dispatcher.UIThread.RunJobs();
            }
            else
            {
                var settings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                store.Create("Second", ProfileSettings.From(settings));
                typeof(MainWindow).GetMethod("RefreshProfileList", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Click(Find<Button>(window, "NavConfig"));
                Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Second"; Dispatcher.UIThread.RunJobs();
                var confirmation = Assert.Single(window.OwnedWindows);
                Click(confirmation.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "OK")));
                Assert.Equal("Second", settings.ActiveProfile);
                Click(Find<Button>(window, "NavPresetIndex"));
            }
            IndexView(window, 1);
            Assert.Equal(4096, Find<ListBox>(window, "IndexSceneList").ItemCount);
            Assert.Null(Find<ListBox>(window, "IndexSceneList").SelectedItem);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal("---", Find<TextBlock>(window, "IndexPresetNumber").Text); Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SceneResultsSendTheSceneAndRespectExplicitPresetEntry(int offset)
    {
        var (window, midi) = Window(displayOffset: offset);
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex")); IndexView(window, 1);
            var list = Find<ListBox>(window, "IndexSceneList");
            Assert.Equal(4096, list.ItemCount); list.SelectedIndex = 11; Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal((1 + offset).ToString("D3"), Find<TextBlock>(window, "IndexPresetNumber").Text);
            Assert.Contains("scene 4", AutomationProperties.GetName(Find<Button>(window, "IndexSendCommand")));
            Assert.Empty(midi.Writes);
            Click(Find<Button>(window, "IndexSendCommand"));
            Assert.Equal(("PresetScene", 0, 1, 4, 77, 5), Assert.Single(midi.Writes));
            list.SelectedIndex = 12; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexSendCommand"));
            Assert.Equal(("Scene", 0, 0, 5, 77, 5), midi.Writes[1]);
            ((ListBoxItem)list.ContainerFromIndex(12)!).Focus(); Number(window, (3 + offset).ToString()); Press(window, PhysicalKey.Enter);
            Assert.Equal(("Preset", 0, 3, 0, 0, 5), midi.Writes[2]);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SceneResultsDoubleClickAndKeyboardActionsHaveSceneTargetsAndReturnFocus()
    {
        var (window, midi) = Window();
        try
        {
            await window.ConnectAsync(); Assert.Single(window.OwnedWindows).Close();
            Click(Find<Button>(window, "NavPresetIndex")); IndexView(window, 1);
            var list = Find<ListBox>(window, "IndexSceneList");
            list.SelectedIndex = 6; Dispatcher.UIThread.RunJobs();
            var selected = (ListBoxItem)list.ContainerFromIndex(6)!;
            Assert.Contains("Scene 7", AutomationProperties.GetName(selected));
            selected.Focus(); Press(window, PhysicalKey.ArrowDown); Assert.Equal(7, list.SelectedIndex);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Empty(midi.Writes);
            selected = (ListBoxItem)list.ContainerFromIndex(7)!; selected.Focus();
            Press(window, PhysicalKey.Enter);
            Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible); Assert.Empty(midi.Writes);
            window.CaptureRenderedFrame()?.Dispose();
            var otherScene = Find<Grid>(window, "IndexScene2Row");
            var point = otherScene.TranslatePoint(new Point(8, 12), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Contains("scene 2", AutomationProperties.GetName(Find<Button>(window, "IndexSendCommand")));
            Click(Find<Button>(window, "IndexCloseInspector"));
            Assert.Contains("scene 8", AutomationProperties.GetName(Find<Button>(window, "IndexSendCommand")));
            Press(window, PhysicalKey.Escape);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Same(selected, window.FocusManager?.GetFocusedElement());
            Press(window, PhysicalKey.Enter);
            Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible); Assert.Empty(midi.Writes);
            Assert.Contains("scene 8", AutomationProperties.GetName(Find<Button>(window, "IndexSendCommand")));
            DoubleClick(window, selected); Dispatcher.UIThread.RunJobs();
            Assert.Equal(("PresetScene", 0, 0, 8, 77, 5), Assert.Single(midi.Writes));
            Find<Grid>(window, "IndexScene8Row").Focus(); Press(window, PhysicalKey.Enter);
            Assert.Equal(("Scene", 0, 0, 8, 77, 5), midi.Writes[1]);
            Assert.True(Find<Button>(window, "IndexCloseInspector").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SwitchingViewsRestoresIndependentSearchTagsSelectionDockAndScroll()
    {
        var (window, midi) = Window(capacity: 1024);
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var search = Find<TextBox>(window, "IndexSearchInput");
            search.Text = "Brit"; Dispatcher.UIThread.RunJobs();
            Click(Find<ToggleButton>(window, "IndexTagMatchAny"));
            var presets = Find<ListBox>(window, "IndexPresetList"); presets.SelectedIndex = 100; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexCloseInspector"));
            var presetSelection = ((IndexedPreset)((ListBoxItem)presets.SelectedItem!).Tag!).Slot;
            var presetViewer = presets.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var presetOffset = presetViewer.Offset;
            IndexView(window, 1);
            Assert.Equal("", search.Text); Assert.True(Find<ToggleButton>(window, "IndexTagMatchAll").IsChecked);
            search.Text = "OD"; Dispatcher.UIThread.RunJobs(); search.Focus(); Press(window, PhysicalKey.Enter);
            Assert.Equal("", search.Text); Assert.Contains("Text: OD", Find<Border>(window, "IndexSearchEntry").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            var scenes = Find<ListBox>(window, "IndexSceneList"); scenes.SelectedIndex = 800; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexSceneDetails"));
            var selection = (IndexedScene)scenes.SelectedItem!;
            var sceneViewer = scenes.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var sceneOffset = sceneViewer.Offset;
            IndexView(window, 0);
            Assert.Equal("Brit", search.Text); Assert.True(Find<ToggleButton>(window, "IndexTagMatchAny").IsChecked);
            Assert.Equal(presetSelection, ((IndexedPreset)((ListBoxItem)presets.SelectedItem!).Tag!).Slot);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible); Assert.Equal(presetOffset, presetViewer.Offset);
            search.Focus(); Press(window, PhysicalKey.Enter);
            Assert.Contains("Text: Brit", Find<Border>(window, "IndexSearchEntry").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Click(Find<Button>(window, "IndexClearSearch"));
            IndexView(window, 1);
            Assert.Equal(selection.Preset.Slot, ((IndexedScene)scenes.SelectedItem!).Preset.Slot);
            Assert.Equal(selection.Index, ((IndexedScene)scenes.SelectedItem!).Index);
            Assert.Equal(sceneOffset, sceneViewer.Offset); Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal(4096, scenes.ItemCount);
            typeof(MainWindow).GetMethod("SetTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["Dark"]); Dispatcher.UIThread.RunJobs();
            scenes = Find<ListBox>(window, "IndexSceneList");
            Assert.Equal(1, Find<TabStrip>(window, "IndexViewTabs").SelectedIndex);
            Assert.Equal(selection.Preset.Slot, ((IndexedScene)scenes.SelectedItem!).Preset.Slot);
            Assert.Equal(sceneOffset, scenes.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset);
            Click(Find<Button>(window, "NavFavorites")); Click(Find<Button>(window, "NavPresetIndex"));
            Assert.Equal(1, Find<TabStrip>(window, "IndexViewTabs").SelectedIndex); Assert.Equal(4096, scenes.ItemCount);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChooserKeyboardCancelsPendingEntryAndAutoSendAndStaleLibrarySelection()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var settings = (AppSettings)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            settings.AutoSend = true; settings.AutoSendDelayMs = 60000;
            Find<Button>(window, "IndexPresetReadout").Focus(); Number(window, "123");
            var timer = (DispatcherTimer)typeof(MainWindow).GetField("_autoSendTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.True(timer.IsEnabled);
            Find<TabStripItem>(window, "IndexViewPresets").Focus(); Press(window, PhysicalKey.ArrowRight);
            Assert.False(timer.IsEnabled); Assert.Equal("---", Find<TextBlock>(window, "IndexPresetNumber").Text);
            var scenes = Find<ListBox>(window, "IndexSceneList"); scenes.SelectedIndex = 7; Dispatcher.UIThread.RunJobs();
            IndexView(window, 0);
            var cache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            cache.Committed!.Presets[0] = cache.Committed.Presets[0] with { ContentSha256 = "replaced", SceneNames = Enumerable.Repeat("Replaced", 8).ToArray() };
            IndexView(window, 1);
            Assert.Null(scenes.SelectedItem); Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal("---", Find<TextBlock>(window, "IndexPresetNumber").Text); Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FullSceneLibraryVirtualizesRowsAndKeepsNameSearchResponsive()
    {
        var (window, midi) = Window(capacity: 1024);
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var watch = Stopwatch.StartNew(); IndexView(window, 1);
            var list = Find<ListBox>(window, "IndexSceneList"); Assert.Equal(8192, list.ItemCount);
            Assert.InRange(list.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 100);
            list.SelectedIndex = 8191; list.ScrollIntoView(8191); Dispatcher.UIThread.RunJobs();
            Find<TextBox>(window, "IndexSearchInput").Text = "S4 + OD"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(1024, list.ItemCount);
            Assert.Equal(7, ((IndexedScene)list.SelectedItem!).Index);
            Assert.Equal(1023, ((IndexedScene)list.SelectedItem!).Preset.Slot);
            Find<TextBox>(window, "IndexSearchInput").Text = "Brit Lead"; Dispatcher.UIThread.RunJobs(); Assert.Empty(list.Items);
            Assert.Empty(midi.Writes); watch.Stop();
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "8192-scenes-timing.txt"), $"Scene view, last row, two searches: {watch.Elapsed.TotalMilliseconds:F0} ms"); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClosingSceneDetailsAfterScrollingReturnsFocusToTheVisibleOrigin()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); IndexView(window, 1);
            var list = Find<ListBox>(window, "IndexSceneList"); list.SelectedIndex = 7; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexSceneDetails"));
            list.ScrollIntoView(4095); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()?.Dispose();
            Click(Find<Button>(window, "IndexCloseInspector"));
            var origin = Assert.IsType<ListBoxItem>(window.FocusManager?.GetFocusedElement());
            var scene = Assert.IsType<IndexedScene>(origin.DataContext); Assert.Equal(0, scene.Preset.Slot); Assert.Equal(7, scene.Index);
            var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var position = origin.TranslatePoint(default, viewer)!.Value;
            Assert.InRange(position.Y, 0, viewer.Viewport.Height - origin.Bounds.Height);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SceneDetailsStayClosedUntilRequestedAndRememberDismissalWhileBrowsing()
    {
        var (window, midi) = Window();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            Assert.False(Find<Button>(window, "IndexSceneDetails").IsVisible);
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            IndexView(window, 1);
            var details = Find<Button>(window, "IndexSceneDetails");
            Assert.True(details.IsVisible); Assert.False(details.IsEnabled);
            var list = Find<ListBox>(window, "IndexSceneList");
            var viewport = list.Bounds.Height;
            list.SelectedIndex = 6; Dispatcher.UIThread.RunJobs();
            Assert.True(details.IsEnabled); Assert.Contains("scene 7", AutomationProperties.GetName(details));
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal(viewport, list.Bounds.Height);
            details.Focus(); Press(window, PhysicalKey.Enter);
            Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.True(list.Bounds.Height < viewport);
            list.SelectedIndex = 7; Dispatcher.UIThread.RunJobs();
            Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Contains("scene 8", AutomationProperties.GetName(Find<Button>(window, "IndexSendCommand")));
            Find<Button>(window, "IndexCloseInspector").Focus(); Press(window, PhysicalKey.Escape);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            var selected = (ListBoxItem)list.ContainerFromIndex(7)!;
            Assert.Same(selected, window.FocusManager?.GetFocusedElement());
            window.CaptureRenderedFrame()?.Dispose();
            var point = selected.TranslatePoint(new Point(55, 14), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Press(window, PhysicalKey.ArrowDown); Assert.Equal(8, list.SelectedIndex);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Assert.Equal(viewport, list.Bounds.Height);
            IndexView(window, 0);
            Assert.True(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            IndexView(window, 1);
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            typeof(MainWindow).GetMethod("SetTheme", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, ["Dark"]); Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "IndexInspectorFrame").IsVisible);
            Find<TextBox>(window, "IndexSearchInput").Text = "No matching scene"; Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Button>(window, "IndexSceneDetails").IsEnabled);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }
    }
}
