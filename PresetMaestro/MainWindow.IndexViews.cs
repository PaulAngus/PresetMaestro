#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private enum IndexView { Presets, Scenes }
    private sealed class IndexViewState
    {
        public string Search = "";
        public List<SearchChip> Chips = [];
        public bool AllTags = true, Dismissed;
        public int? Slot, Scene;
        public Vector Offset;
    }
    private readonly IndexViewState[] _indexViewStates = [new(), new() { Dismissed = true }];
    private IndexView _indexView;
    private TabStrip? _indexViewTabs;
    private Button? _indexSceneDetailsButton;
    private ListBox? _indexSceneList;
    private SceneTablesPanel? _indexSceneTablesPanel;
    private Control? _indexSceneFrame;
    private string? _indexViewContext;
    private bool _switchingIndexView;
    private ListBox? ActiveIndexList => _indexView == IndexView.Scenes ? _indexSceneList : _indexList;

    private Control BuildIndexViewTabs()
    {
        _indexViewTabs = new TabStrip
        {
            Name = "IndexViewTabs",
            Height = 28,
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Items =
            {
                new TabStripItem { Name = "IndexViewPresets", Content = "Presets", FontSize = 13, Height = 28, MinHeight = 28, Padding = new(8, 2) },
                new TabStripItem { Name = "IndexViewScenes", Content = "Scenes", FontSize = 13, Height = 28, MinHeight = 28, Padding = new(8, 2) },
            },
        };
        _indexViewTabs.SelectedIndex = (int)_indexView;
        AutomationProperties.SetName(_indexViewTabs, "Browse presets or scenes");
        _indexViewTabs.SelectionChanged += (_, _) =>
        {
            if (_indexViewTabs.SelectedIndex >= 0) { SwitchIndexView((IndexView)_indexViewTabs.SelectedIndex); }
        };
        _indexViewTabs.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && _enteredDigits.Length == 0) { e.Handled = true; return; }
            if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) { return; }
            _indexViewTabs.SelectedIndex = e.Key is Key.Left or Key.Home ? 0 : 1;
            (_indexViewTabs.SelectedItem as TabStripItem)?.Focus();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        return _indexViewTabs;
    }

    private Button BuildIndexSceneDetailsButton()
    {
        _indexSceneDetailsButton = IndexButton("Details", "IndexSceneDetails");
        _indexSceneDetailsButton.VerticalAlignment = VerticalAlignment.Center;
        _indexSceneDetailsButton.Click += (_, _) => OpenIndexSceneDetails();
        UpdateIndexSceneDetailsButton();
        return _indexSceneDetailsButton;
    }

    private void UpdateIndexSceneDetailsButton()
    {
        if (_indexSceneDetailsButton is null) { return; }
        _indexSceneDetailsButton.IsVisible = _indexView == IndexView.Scenes;
        var scene = _indexSceneList?.SelectedItem as IndexedScene;
        _indexSceneDetailsButton.IsEnabled = scene is not null;
        AutomationProperties.SetName(_indexSceneDetailsButton, scene is null ? "Show scene details"
            : $"Show details for scene {scene.Index + 1}: {scene.Name}, preset {scene.Preset.Slot + _settings.DisplayOffset:D3}: {scene.Preset.Name}");
        ToolTip.SetTip(_indexSceneDetailsButton, scene is null ? "Select a scene to view its details."
            : "Show this scene's parent preset, scenes, tags and amp models (Enter on the selected scene).");
    }

    private void OpenIndexSceneDetails()
    {
        if (_indexView != IndexView.Scenes || _indexSceneList?.SelectedItem is not IndexedScene scene) { return; }
        _indexSelectedSlot = scene.Preset.Slot; _indexSelectedScene = scene.Index;
        _indexInspectorDismissed = false;
        RenderIndexInspector(); RevealIndexSelection();
    }

    private void SaveIndexViewState()
    {
        var state = _indexViewStates[(int)_indexView];
        state.Search = _indexSearchBox?.Text ?? "";
        state.Chips = [.. _indexSearchChips]; state.AllTags = _indexAllTags;
        state.Slot = _indexSelectedSlot; state.Scene = _indexSelectedScene; state.Dismissed = _indexInspectorDismissed;
        state.Offset = ActiveIndexList?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;
    }

    private void SwitchIndexView(IndexView view)
    {
        if (_switchingIndexView || _indexView == view || _indexSceneList is null) { return; }
        SaveIndexViewState();
        _autoSendTimer.Stop(); _enteredDigits = "";
        _switchingIndexView = true;
        try
        {
            _indexView = view;
            if (view == IndexView.Scenes) { _ampContains = null; }
            var state = _indexViewStates[(int)view];
            _indexSearchChips.Clear(); _indexSearchChips.AddRange(state.Chips); _indexAllTags = state.AllTags;
            _indexSelectedSlot = state.Slot; _indexSelectedScene = state.Scene; _indexInspectorDismissed = state.Dismissed;
            _indexSearchBox!.Text = state.Search;
            _indexViewTabs!.SelectedIndex = (int)view;
            RefreshIndexSearchScope(); RenderIndexSearchChips();
        }
        finally { _switchingIndexView = false; }
        RenderIndexResults();
        RestoreIndexScroll(_indexViewStates[(int)view].Offset);
        UpdateDisplay();
    }

    private void RestoreIndexScroll(Vector offset)
    {
        var list = ActiveIndexList;
        Dispatcher.UIThread.Post(() =>
        {
            if (list != ActiveIndexList) { return; }
            var viewer = list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (viewer is not null) { viewer.Offset = offset; }
        }, DispatcherPriority.Loaded);
    }

    private void ValidateIndexViewContext(IndexScan? scan)
    {
        // A changed library invalidates selection in both views, including a hidden view.
        string context = $"{_settings.ActiveProfile}|{_indexCache?.Device.Id}|{_indexCache?.Device.Variant}|{_indexCache?.Device.PresetCapacity}|{_indexCache?.Device.Firmware}|{scan?.EffectiveFirmware}|{_settings.DisplayOffset}|{_settings.MidiChannel}|{_settings.SceneCc}|" +
            string.Join('|', scan?.Presets.Values.OrderBy(p => p.Slot).Select(p => $"{p.Slot}:{p.ContentSha256}:{p.Name}:{string.Join('\n', p.SceneNames)}") ?? []);
        if (_indexViewContext is not null && _indexViewContext != context)
        {
            foreach (var state in _indexViewStates) { state.Slot = state.Scene = null; state.Offset = default; state.Dismissed = true; }
            _indexSelectedSlot = _indexSelectedScene = null; _indexInspectorDismissed = true;
            _autoSendTimer.Stop(); _enteredDigits = "";
        }
        _indexViewContext = context;
    }

    private void RefreshIndexSearchScope()
    {
        if (_indexSearchBox is null) { return; }
        bool scenes = _indexView == IndexView.Scenes;
        _indexSearchBox.Watermark = scenes ? "Add scene tag or search scene name…" : "Add tag or search term…";
        AutomationProperties.SetName(_indexSearchBox, scenes ? "Add scene tag or search scene name" : "Add tag or search term");
        if (_indexTagMatchAny is not null)
        {
            _indexTagMatchAny.IsChecked = !_indexAllTags; _indexTagMatchAll!.IsChecked = _indexAllTags;
            _indexTagMatchAny.Foreground = _indexAllTags ? SecondaryBrush : AccentBrush;
            _indexTagMatchAll.Foreground = _indexAllTags ? AccentBrush : SecondaryBrush;
            Grid.SetColumn(_indexTagMatchThumb!, _indexAllTags ? 1 : 0);
        }
        if (_indexSearchPopup is not null) { _indexSearchPopup.IsOpen = false; }
        _indexSuggestions?.Items.Clear();
    }

#if MAESTRO_MACOS
    // Include the heading's text and gutters in the fixed numeric column.
    private static ColumnDefinitions SceneResultColumns() => new("20*,48,17*,56");
#else
    private static ColumnDefinitions SceneResultColumns() => new("20*,44,17*,56");
#endif

    private Control BuildIndexSceneResults()
    {
        var frame = new Grid { Name = "IndexSceneResults", IsVisible = false };
        _indexSceneList = new ListBox
        {
            Name = "IndexSceneList",
            Background = SurfaceBrush,
            BorderThickness = new(0),
            ItemContainerTheme = CompactListItemTheme(),
            ItemTemplate = new FuncDataTemplate<IndexedScene>((scene, _) => scene is null ? null : BuildIndexSceneResultRow(scene)),
        };
        AutomationProperties.SetName(_indexSceneList, "Scenes in the saved library");
        ConfigureIndexSceneTables();
        _indexSceneList.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters = { new Setter(ListBoxItem.BackgroundProperty, ThemeBrush("SelectedBrush")) },
        });
        _indexSceneList.SelectionChanged += (_, _) =>
        {
            if (_indexRendering) { return; }
            var scene = _indexSceneList.SelectedItem as IndexedScene;
            _indexSelectedSlot = scene?.Preset.Slot; _indexSelectedScene = scene?.Index;
            RenderIndexInspector(); RevealIndexSelection();
        };
        _indexSceneList.DoubleTapped += (_, e) =>
        {
            if (IndexItemFromSource(e.Source)?.DataContext is IndexedScene scene)
            { GoToDevice(scene.Preset.Slot, scene.Index + 1, scene.Preset.Variant.ToDeviceModel()); e.Handled = true; }
        };
        _indexSceneList.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown && _indexSceneTablesPanel is { } panel)
            {
                var direction = e.Key switch
                {
                    Key.Up => NavigationDirection.Up,
                    Key.Down => NavigationDirection.Down,
                    Key.Left => NavigationDirection.Left,
                    Key.Right => NavigationDirection.Right,
                    Key.Home => NavigationDirection.First,
                    _ => NavigationDirection.Last,
                };
                int target = e.Key is Key.PageUp or Key.PageDown
                    ? panel.PageTarget(_indexSceneList.SelectedIndex, e.Key == Key.PageUp, _indexSceneList.Bounds.Height)
                    : panel.NavigationTarget(_indexSceneList.SelectedIndex, direction);
                _indexSceneList.SelectedIndex = target;
                _indexSceneList.ScrollIntoView(target);
                _indexSceneList.ContainerFromIndex(target)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (IndexItemFromSource(e.Source)?.DataContext as IndexedScene ?? _indexSceneList.SelectedItem as IndexedScene) is { } scene)
            { _indexSceneList.SelectedItem = scene; OpenIndexSceneDetails(); e.Handled = true; }
            else if (e.Key == Key.Escape && _indexInspectorFrame?.IsVisible == true)
            { CloseIndexInspector(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        frame.Children.Add(_indexSceneList);
        _indexSceneFrame = frame; return frame;
    }

    private Control BuildIndexSceneResultRow(IndexedScene scene)
    {
        var row = new Grid { Height = 26, ColumnDefinitions = SceneResultColumns(), Margin = new(3, 0) };
        var divider = new Border { Height = 1, Background = ThemeBrush("RowSeparatorBrush"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        Grid.SetColumnSpan(divider, 4); row.Children.Add(divider);
        string[] text = [scene.Name.Length > 0 ? scene.Name : "(unnamed scene)", (scene.Index + 1).ToString(),
            scene.Preset.Name.Length > 0 ? scene.Preset.Name : "(unnamed preset)", (scene.Preset.Slot + _settings.DisplayOffset).ToString("D3")];
        var title = new TextBlock { Text = text[0], FontSize = 13, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var tags = new SceneTagSummaryPanel(scene.Tags.Select(tag => BuildFavoriteTagChip(tag, false)), SecondaryBrush)
        { VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(tags, string.Join(", ", scene.Tags));
        row.Children.Add(new SceneNameTagsPanel(title, tags)
        { Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { title, tags } });
        for (int i = 1; i < text.Length; i++)
        {
            var label = new TextBlock { Text = text[i], FontSize = 13, Foreground = i is 1 or 3 ? SecondaryBrush : TextBrush, Margin = new(6, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, i); row.Children.Add(label);
        }
        string identity = $"Scene {scene.Index + 1}: {text[0]}. Preset {text[3]}: {text[2]}. Tags: {string.Join(", ", scene.Tags)}";
        AutomationProperties.SetName(row, identity); ToolTip.SetTip(row, identity);
        var activeAccent = BuildIndexActiveAccent(row, scene.Preset.Slot, scene.Index + 1, -3);
        Grid.SetColumnSpan(activeAccent, 4); row.Children.Add(activeAccent);
        row.AttachedToVisualTree += (_, _) =>
        {
            if (row.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item)
            {
                AutomationProperties.SetName(item, identity); ToolTip.SetTip(item, identity);
                AutomationProperties.SetHelpText(item, AutomationProperties.GetHelpText(row));
            }
        };
        var go = new MenuItem { Header = "Go to scene" };
        go.Click += (_, _) => GoToDevice(scene.Preset.Slot, scene.Index + 1, scene.Preset.Variant.ToDeviceModel());
        row.ContextMenu = new ContextMenu { Items = { go } };
        row.ContextMenu.Opening += (_, _) => go.IsEnabled = DeviceNavigationUnavailable(scene.Preset.Variant.ToDeviceModel()) is null;
        return row;
    }

    private void RenderIndexSceneResults(IndexedPreset[] presets, IndexScan? scan, List<SearchChip> chips)
    {
        var scenes = SceneSearch.Find(presets, p => _indexProfile.Find(_indexCache!.Device.Id, p, scan!.Firmware),
            chips.Where(c => !c.IsTag).Select(c => c.Value), chips.Where(c => c.IsTag).Select(c => c.Value), _indexAllTags);
        _indexRendering = true;
        try
        {
            _indexSceneActiveRows.Clear();
            _indexSceneList!.ItemsSource = scenes;
            _indexSceneList.SelectedItem = scenes.FirstOrDefault(s => s.Preset.Slot == _indexSelectedSlot && s.Index == _indexSelectedScene);
            if (_indexSceneList.SelectedItem is null) { _indexSelectedSlot = _indexSelectedScene = null; }
        }
        finally { _indexRendering = false; }
        _indexEmpty!.IsVisible = scenes.Length == 0;
        _indexEmpty.Text = _indexCache is null ? "Assign a device in Config → Device for this profile. Then sync its saved presets and scenes."
            : presets.Length == 0 ? "No saved scenes are available. Connect this device and sync its saved presets."
            : "No scenes match this search.";
        if (!_indexScanning)
        {
            int count = scenes.Select(s => s.Preset.Slot).Distinct().Count();
            SetIndexStatus($"{scenes.Length} {(scenes.Length == 1 ? "scene" : "scenes")} across {count} {(count == 1 ? "preset" : "presets")}." +
                (_indexCache?.Committed is not null && _indexCache.LastAttempt is { Status: not "Complete" } ? " Showing the last complete index; an unfinished scan is available to resume." : " Details or Enter opens the pane. SEND, double-click or Go to loads the scene."));
        }
        RenderIndexInspector();
    }
}
#endif
