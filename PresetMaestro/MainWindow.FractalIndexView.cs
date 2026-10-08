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
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private Control? _indexPageControl, _indexSearchControls, _indexLibraryActions, _indexSyncInfo;
    private TextBlock? _indexPageTitle;
    private ListBox? _indexList;
    private StackPanel? _indexInspector;
    private StackPanel? _indexInspectorHeader;
    private ScrollViewer? _indexInspectorScroll;
    private Border? _indexInspectorFrame;
    private IndexDockLayout? _indexResultsPane;
    private TextBlock? _indexStatus, _indexEmpty;
    private Button? _indexResumeButton, _indexCancelButton, _indexReviewButton;
    private ProgressBar? _indexProgress;
    private StackPanel? _indexProgressPanel;
    private TextBlock? _indexProgressText;
    private string _indexProgressMessage = "Preparing sync…";
    private TextBox? _indexSearchBox;
    private WrapPanel? _indexSearchPanel;
    private Popup? _indexSearchPopup;
    private ListBox? _indexSuggestions;
    private readonly List<SearchChip> _indexSearchChips = [];
    private bool _indexAllTags = true, _indexRendering;
    private int? _indexSelectedSlot;
    private int? _indexSelectedScene;
    private Border? _indexConnectionSyncBanner;
    private TextBlock? _indexConnectionSyncTitle, _indexConnectionSyncSummary;

    private static Button IndexButton(string text, string? name = null) => new()
    {
        Name = name,
        Content = text,
        MinHeight = 28,
        Padding = new Thickness(8, 2),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    partial void RestoreIndexPage(ContentControl host)
    {
        _indexPageControl = BuildIndexPage();
        _ampsPage = BuildAmpsPage();
        if (_currentPage == AppPage.PresetIndex) { host.Content = _indexPageControl; }
        if (_currentPage == AppPage.Amps) { host.Content = _ampsPage; }
    }

    partial void AddIndexNavigation(StackPanel nav, Func<string, Control, AppPage, EntryMode?, Button> create)
    {
        var button = create("Preset Index", _indexPageControl!, AppPage.PresetIndex, EntryMode.Preset);
        _presetIndexNavigation = button;
        button.Click += (_, _) =>
        {
            if (!_openingAmpPresets) { _ampContains = null; }
            RefreshIndexContext();
        };
        nav.Children.Add(button);
        _ampsNavigation = create("Amps", _ampsPage!, AppPage.Amps, null);
        _ampsNavigation.Click += (_, _) => RefreshIndexContext();
        nav.Children.Add(_ampsNavigation);
    }

    private Control BuildIndexPage()
    {
        var page = new Grid { Name = "PresetIndexPage", Margin = new Thickness(20, 12, 20, 20) };
        var card = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16, 11, 12, 12), BoxShadow = Elevation };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto") };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        _indexPageTitle = new TextBlock { Name = "IndexPageTitle", Text = "Preset Index", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        title.Children.Add(_indexPageTitle);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _indexLibraryActions = actions;
        _indexReviewButton = IndexButton("Needs review", "IndexReview");
        _indexReviewButton.Click += async (_, _) => await ShowIndexReviewAsync();
        _indexResumeButton = IndexButton("Resume", "IndexResume");
        _indexResumeButton.Click += async (_, _) => await SyncIndexAsync(resume: true);
        _indexCancelButton = IndexButton("Cancel", "IndexCancel");
        _indexCancelButton.Click += (_, _) => _presetNamesCts?.Cancel();
        actions.Children.Add(_indexReviewButton);
        actions.Children.Add(_indexResumeButton); actions.Children.Add(_indexCancelButton);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        commands.Children.Add(actions);
        commands.Children.Add(BuildIndexSendControls());
        Grid.SetColumn(commands, 1); title.Children.Add(commands); content.Children.Add(title);
        var syncInfo = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 0, 10) };
        _indexSyncInfo = syncInfo;
        _indexConnectionSyncTitle = new TextBlock { Name = "IndexConnectionSyncTitle", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = ThemeBrush("SendSuccessForegroundBrush"), TextWrapping = TextWrapping.Wrap };
        _indexConnectionSyncSummary = new TextBlock { Name = "IndexConnectionSyncSummary", FontSize = 14, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        var completion = new Grid { ColumnDefinitions = new("*,12,Auto") };
        completion.Children.Add(new StackPanel { Spacing = 4, Children = { _indexConnectionSyncTitle, _indexConnectionSyncSummary } });
        var dismiss = IndexButton("Dismiss", "IndexConnectionSyncDismiss");
        dismiss.Click += (_, _) => { _indexConnectionSyncCompletion = null; RefreshIndexConnectionSyncCompletion(); };
        Grid.SetColumn(dismiss, 2); completion.Children.Add(dismiss);
        _indexConnectionSyncBanner = new Border { Name = "IndexConnectionSyncBanner", Child = completion, Padding = new(12), CornerRadius = new(6), Background = ThemeBrush("SendSuccessBackgroundBrush"), BorderBrush = ThemeBrush("SendSuccessForegroundBrush"), BorderThickness = new(4, 0, 0, 0), IsVisible = false };
        syncInfo.Children.Add(_indexConnectionSyncBanner);
        _indexProgressText = new TextBlock { Name = "IndexProgressText", Text = _indexProgressMessage, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        _indexProgress = new ProgressBar { Name = "IndexProgress", Height = 14, Minimum = 0, Maximum = 512, Foreground = AccentBrush, Background = UiBorderBrush };
        AutomationProperties.SetName(_indexProgress, "Device index sync progress");
        _indexProgressPanel = new StackPanel { Spacing = 7, Children = { _indexProgressText, _indexProgress } };
        syncInfo.Children.Add(_indexProgressPanel);
        Grid.SetRow(syncInfo, 1); content.Children.Add(syncInfo);
        _indexSearchControls = BuildIndexSearch(); _indexSearchControls.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(_indexSearchControls, 2); content.Children.Add(_indexSearchControls);
        var resultsHeading = BuildAmpDetailsHeading();
        resultsHeading.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(resultsHeading, 3); content.Children.Add(resultsHeading);
        var results = new Grid { MinHeight = 140 };
        _indexList = new ListBox { Name = "IndexPresetList", Background = SurfaceBrush, BorderThickness = new Thickness(0), ItemContainerTheme = CompactListItemTheme(), Focusable = true };
        ConfigureIndexTables();
        _indexList.SelectionChanged += (_, _) =>
        {
            if (_indexRendering) { return; }
            _indexInspectorDismissed = false;
            _indexSelectedSlot = (_indexList.SelectedItem as ListBoxItem)?.Tag is IndexedPreset preset ? preset.Slot : null;
            _indexSelectedScene = null;
            PaintIndexSelection(); RenderIndexInspector();
            UpdateIndexSendControls();
            // The dock changes the viewport height. Reveal the selection after layout.
            RevealIndexSelection();
        };
        _indexList.DoubleTapped += (_, e) =>
        {
            if (IndexItemFromSource(e.Source) is { Tag: IndexedPreset preset })
            { GoToDevice(preset.Slot, null, _indexCache!.Device.Variant.ToDeviceModel()); e.Handled = true; }
        };
        results.Children.Add(_indexList);
        _indexEmpty = new TextBlock { Name = "IndexEmptyState", TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        results.Children.Add(_indexEmpty);
        _indexResultsPane = new IndexDockLayout { Name = "IndexResultsPane", RowDefinitions = new("*,0,0") };
        _indexResultsPane.Children.Add(results);
        Grid.SetRow(_indexResultsPane, 4); content.Children.Add(_indexResultsPane);
        _indexInspector = new StackPanel { Name = "IndexInspector", Spacing = 0 };
        _indexInspectorHeader = new StackPanel { Name = "IndexInspectorHeader", Spacing = 8 };
        var inspectorHeader = new Border { Child = _indexInspectorHeader, BorderBrush = UiBorderBrush, BorderThickness = new(0, 0, 0, 1), Padding = new(12, 8) };
        _indexInspectorScroll = new ScrollViewer { Name = "IndexInspectorScroll", Content = _indexInspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(12, 8) };
        var inspectorLayout = new Grid { RowDefinitions = new("Auto,*") };
        inspectorLayout.Children.Add(inspectorHeader);
        Grid.SetRow(_indexInspectorScroll, 1); inspectorLayout.Children.Add(_indexInspectorScroll);
        _indexInspectorFrame = new Border { Name = "IndexInspectorFrame", Child = inspectorLayout, Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new(6), ClipToBounds = true };
        _indexInspectorFrame.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseIndexInspector(); e.Handled = true; }
        };
        Grid.SetRow(_indexInspectorFrame, 2); _indexResultsPane.Children.Add(_indexInspectorFrame);
        _indexStatus = new TextBlock { Name = "IndexStatus", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(_indexStatus, 5); content.Children.Add(_indexStatus);
        card.Child = content; page.Children.Add(card);
        RenderIndexResults(); UpdateIndexButtons();
        return page;
    }

    private static ListBoxItem? IndexItemFromSource(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button) { return null; }
            if (visual is ListBoxItem item) { return item; }
        }
        return null;
    }

    private void RefreshIndexConnectionSyncCompletion()
    {
        if (_indexConnectionSyncBanner is null) { return; }
        bool visible = _indexConnectionSyncCompletion is { } completion && completion.Context == ConnectionSyncContext &&
            !_indexScanning && !_indexChecking && _indexError is null;
        _indexConnectionSyncBanner.IsVisible = visible;
        UpdateIndexSyncInfoVisibility();
        if (!visible) { return; }
        _indexConnectionSyncTitle!.Text = _indexConnectionSyncCompletion!.Title;
        _indexConnectionSyncSummary!.Text = _indexConnectionSyncCompletion.Detail + " " + _indexConnectionSyncCompletion.Next;
    }

    private void UpdateIndexSyncInfoVisibility()
    {
        if (_indexSyncInfo is null) { return; }
        _indexSyncInfo.IsVisible = (_ampContains is null || _indexScanning) &&
            (_indexConnectionSyncBanner?.IsVisible == true || _indexProgressPanel?.IsVisible == true);
    }

    private string[] IndexAvailableTags() => _indexProfile.Annotations.Where(a => a.HasTags)
        .SelectMany(a => a.Tags.Concat(a.SceneTags.Values.SelectMany(t => t)))
        .Concat(_favorites.SelectMany(f => f.Tags)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private SearchChip IndexPreviewChip(string text) => IndexAvailableTags().FirstOrDefault(t => t.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } tag
        ? new(tag, true) : new(text, false);

    private Control BuildIndexSearch()
    {
        string previous = _indexSearchBox?.Text ?? "";
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        _indexSearchPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _indexSearchBox = new TextBox { Name = "IndexSearchInput", Text = previous, Watermark = "Add tag or search term…", MinWidth = 218, MinHeight = 26, Padding = new Thickness(4, 2), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        _indexSearchPanel.Children.Add(_indexSearchBox);
        var entry = new Border { Name = "IndexSearchEntry", Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2), MinHeight = 32, Child = _indexSearchPanel };
        layout.Children.Add(entry);
        _indexSuggestions = new ListBox { Name = "IndexSearchSuggestions", MinWidth = 300, MaxHeight = 220, Background = SurfaceBrush, ItemContainerTheme = CompactListItemTheme(), Focusable = false };
        _indexSearchPopup = new Popup { PlacementTarget = entry, Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true, Child = new Border { BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Background = SurfaceBrush, Child = _indexSuggestions } };
        layout.Children.Add(_indexSearchPopup);
        _indexSearchBox.TextChanged += (_, _) => { RenderIndexResults(); RefreshIndexSuggestions(); };
        _indexSearchBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                string input = _indexSearchBox.Text?.Trim() ?? "";
                if (input.Length > 0) { CommitIndexChip(_indexSuggestions.SelectedItem is ListBoxItem { Tag: SearchChip chip } ? chip : IndexPreviewChip(input)); }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { _indexSearchPopup.IsOpen = false; e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up && _indexSuggestions.ItemCount > 0)
            {
                _indexSuggestions.SelectedIndex = Math.Clamp(_indexSuggestions.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _indexSuggestions.ItemCount - 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(_indexSearchBox.Text) && _indexSearchChips.Count > 0)
            {
                _indexSearchChips.RemoveAt(_indexSearchChips.Count - 1); RenderIndexSearchChips(); RenderIndexResults(); e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(new TextBlock { Text = "Tag Match", FontSize = 12, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var any = BuildTagMatchButton("IndexTagMatchAny", "Any tag");
        var all = BuildTagMatchButton("IndexTagMatchAll", "All tags");
        var modes = new Grid { Width = TagMatchSegmentWidth * 2, Height = 26, ColumnDefinitions = new ColumnDefinitions("*,*") };
        var thumb = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false };
        modes.Children.Add(thumb); modes.Children.Add(any); Grid.SetColumn(all, 1); modes.Children.Add(all);
        void Mode(bool value)
        {
            _indexAllTags = value; any.IsChecked = !value; all.IsChecked = value;
            any.Foreground = value ? SecondaryBrush : AccentBrush; all.Foreground = value ? AccentBrush : SecondaryBrush;
            Grid.SetColumn(thumb, value ? 1 : 0); RenderIndexResults();
        }
        any.Click += (_, _) => Mode(false); all.Click += (_, _) => Mode(true); Mode(_indexAllTags);
        actions.Children.Add(new Border { Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = modes });
        var clear = IndexButton("Clear search", "IndexClearSearch");
        clear.Background = Brushes.Transparent; clear.BorderThickness = new Thickness(0);
        clear.Click += (_, _) => { _indexSearchChips.Clear(); _indexSearchBox.Text = ""; RenderIndexSearchChips(); Mode(true); };
        actions.Children.Add(clear); Grid.SetColumn(actions, 2); layout.Children.Add(actions);
        RenderIndexSearchChips();
        return layout;
    }

    private void RefreshIndexSuggestions()
    {
        if (_indexSuggestions is null) { return; }
        _indexSuggestions.Items.Clear();
        string input = _indexSearchBox?.Text?.Trim() ?? "";
        if (input.Length == 0) { _indexSearchPopup!.IsOpen = false; return; }
        foreach (var chip in IndexAvailableTags().Where(t => t.Contains(input, StringComparison.OrdinalIgnoreCase)).Take(30)
            .Select(t => new SearchChip(t, true)).Append(new SearchChip(input, false)))
        {
            var button = IndexButton(chip.IsTag ? chip.Value : $"Search text “{chip.Value}”");
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Focusable = false;
            button.Click += (_, _) => CommitIndexChip(chip);
            _indexSuggestions.Items.Add(new ListBoxItem { Content = button, Tag = chip, Padding = new Thickness(0) });
        }
        _indexSuggestions.SelectedIndex = 0;
        _indexSearchPopup!.IsOpen = _indexSearchBox!.IsKeyboardFocusWithin;
    }

    private void CommitIndexChip(SearchChip chip)
    {
        if (!_indexSearchChips.Contains(chip)) { _indexSearchChips.Add(chip); }
        _indexSearchBox!.Text = ""; _indexSearchPopup!.IsOpen = false;
        RenderIndexSearchChips(); RenderIndexResults(); _indexSearchBox.Focus();
    }

    private void RenderIndexSearchChips()
    {
        if (_indexSearchPanel is null) { return; }
        foreach (var item in _indexSearchPanel.Children.Where(c => c != _indexSearchBox).ToArray()) { _indexSearchPanel.Children.Remove(item); }
        foreach (var chip in _indexSearchChips)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            row.Children.Add(new TextBlock { Text = (chip.IsTag ? "" : "Text: ") + chip.Value, FontSize = 12, Foreground = TagTextBrush, VerticalAlignment = VerticalAlignment.Center });
            var remove = new Button { Content = "×", MinHeight = 22, Padding = new Thickness(3, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TagRemoveBrush };
            AutomationProperties.SetName(remove, "Remove search " + chip.Value);
            remove.Click += (_, _) => { _indexSearchChips.Remove(chip); RenderIndexSearchChips(); RenderIndexResults(); };
            row.Children.Add(remove);
            _indexSearchPanel.Children.Insert(_indexSearchPanel.Children.Count - 1, new Border { Background = TagBrush, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 3, 0), Margin = new Thickness(0, 2, 5, 2), Child = row });
        }
    }

    private void RenderIndexResults()
    {
        if (_indexList is null || _indexInspector is null) { return; }
        var scan = _indexCache?.Browsable;
        var chips = _indexSearchChips.ToList();
        if (!string.IsNullOrWhiteSpace(_indexSearchBox?.Text)) { chips.Add(IndexPreviewChip(_indexSearchBox.Text.Trim())); }
        var saved = scan?.Presets.Values.OrderBy(p => p.Slot).Select(p => AmpBrowserCatalog.ResolveForDisplay(p, scan.EffectiveFirmware)).ToArray() ?? [];
        if (_ampContains is not null && (_indexCache is null || _ampContains.DeviceId != _indexCache.Device.Id ||
            _ampContains.Variant != _indexCache.Device.Variant || !AmpBrowserCatalog.SameFirmware(_ampContains.Firmware, _indexCache.Device.Firmware))) { _ampContains = null; }
        if (_ampFilterRow is not null)
        {
            _ampFilterRow.IsVisible = _ampContains is not null;
            _indexPageTitle!.Text = _ampContains?.Label ?? "Preset Index";
            _indexSearchControls!.IsVisible = _ampContains is null;
            _indexLibraryActions!.IsVisible = _ampContains is null || _indexScanning;
            UpdateIndexSyncInfoVisibility();
            if (_ampContains is not null)
            {
                chips.Clear();
                if (_indexSearchPopup is not null) { _indexSearchPopup.IsOpen = false; }
            }
        }
        // Use exactly the Favorites picker's empty-edge policy. Unknown names and interior gaps remain.
        var visibleSlots = PresetSelection.TrimExplicitEmptyEdges(saved.Select(p => new PresetChoice(p.Slot, p.Name)).ToArray())
            .Select(p => p.Slot).ToHashSet();
        var presets = saved.Where(p => visibleSlots.Contains(p.Slot)).ToArray();
        if (_indexSelectedSlot is int selected && !visibleSlots.Contains(selected)) { _indexSelectedSlot = null; }
        var filtered = presets.Where(p => (_ampContains is null || _ampContains.AppliesTo(_indexCache) && _ampContains.Matches(p)) && IndexSearch.Matches(p, _indexProfile.Find(_indexCache!.Device.Id, p, scan!.Firmware),
            chips.Where(c => !c.IsTag).Select(c => c.Value), chips.Where(c => c.IsTag).Select(c => c.Value), _indexAllTags, _settings.DisplayOffset)).ToArray();
        if (_indexSelectedSlot is int chosen && filtered.All(p => p.Slot != chosen)) { _indexSelectedSlot = _indexSelectedScene = null; }
        UpdateIndexSendControls();
        _indexRendering = true;
        _indexList.Items.Clear();
        foreach (var preset in filtered)
        {
            var annotation = _indexProfile.Find(_indexCache!.Device.Id, preset, scan!.Firmware);
            var row = new Grid { Height = _ampContains is null ? 26 : 48, ColumnDefinitions = new ColumnDefinitions("48,*"), Margin = new Thickness(3, 0) };
            row.Children.Add(new TextBlock { Text = (preset.Slot + _settings.DisplayOffset).ToString("D3"), FontSize = 13, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center });
            var tags = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
            foreach (string tag in annotation?.Tags ?? []) { tags.Children.Add(BuildFavoriteTagChip(tag, false)); }
            foreach (var (scene, sceneTags) in (annotation?.SceneTags ?? []).OrderBy(p => p.Key))
            {
                foreach (string tag in sceneTags) { tags.Children.Add(BuildIndexSceneChip(scene, tag)); }
            }
            var titleTags = new FavoriteTitleTagsPanel
            {
                ClipToBounds = true,
                Children =
            {
                new TextBlock { Text = preset.Name.Length > 0 ? preset.Name : "(unnamed preset)", FontSize = 13, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, tags,
            }
            };
            Grid.SetColumn(titleTags, 1); row.Children.Add(titleTags);
            if (_ampContains is not null)
            {
                row.Children.Remove(titleTags);
                var summary = new StackPanel
                {
                    Spacing = 5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                {
                    titleTags,
                    new TextBlock { Name = "IndexAmpResultSummary", Text = string.Join(" · ", _ampContains.MatchingChannelSummary(preset)), FontSize = 13, Foreground = SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis },
                }
                };
                Grid.SetColumn(summary, 1); row.Children.Add(summary);
            }
            var item = new ListBoxItem { Tag = preset, Content = row, Padding = new Thickness(3, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, $"Preset {preset.Slot + _settings.DisplayOffset}: {preset.Name}");
            ToolTip.SetTip(item, string.Join(" · ", new[] { preset.Name }.Concat(annotation?.Tags ?? [])
                .Concat(annotation?.SceneTags.SelectMany(p => p.Value.Select(t => $"S{p.Key + 1}: {t}")) ?? [])
                .Concat(_ampContains?.MatchingChannels(preset) ?? [])));
            _indexList.Items.Add(item);
            var goTo = new MenuItem { Header = "Go to preset" };
            goTo.Click += (_, _) => GoToDevice(preset.Slot, null, _indexCache!.Device.Variant.ToDeviceModel());
            item.ContextMenu = new ContextMenu { Items = { goTo } };
            item.ContextMenu.Opening += (_, _) => goTo.IsEnabled = DeviceNavigationUnavailable(_indexCache!.Device.Variant.ToDeviceModel()) is null;
        }
        _indexList.SelectedItem = _indexList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((IndexedPreset)i.Tag!).Slot == _indexSelectedSlot);
        _indexRendering = false;
        _indexEmpty!.IsVisible = filtered.Length == 0;
        _indexEmpty.Text = _indexCache is null ? "Assign a device in Config → Device for this profile. Then return here to sync its saved presets."
            : _indexScanning && presets.Length == 0 ? "Sync is in progress. Completed reads are being saved; you can cancel and resume."
            : saved.Length > 0 && presets.Length == 0 ? "No populated presets. Empty slots at the start and end are hidden, as in Select Preset."
            : presets.Length == 0 ? "No saved presets have been indexed yet. Connect this device and choose Sync device." : "No presets match this search.";
        if (_ampContains is not null && filtered.Length == 0)
        {
            _indexEmpty.Text = scan is null ? "No saved scan is available. Sync this device to find presets containing this amp."
                : !_ampContains.AppliesTo(_indexCache) ? "Check the connected device against its saved data, or sync it again, to find presets using this amp."
                : "No presets in this saved index contain this amp.";
        }
        int pending = _indexCache is null ? 0 : _indexProfile.Pending(_indexCache).Count();
        _indexReviewButton!.Content = $"Needs review ({pending})";
        _indexReviewButton.IsVisible = pending > 0 || _indexProfile.ReviewHistory.Count > 0;
        if (!_indexScanning)
        {
            _indexProgress!.Value = scan?.Presets.Count ?? 0;
            _indexProgress.Maximum = _indexCache is null ? 512 : FractalDeviceDefinition.For(_indexCache.Device).PresetSlots;
            _indexStatus!.Text = _indexCache?.Committed is not null && _indexCache.LastAttempt is { Status: not "Complete" }
                ? "Showing the last complete index. An unfinished scan is available to resume."
                : "Select a preset to see its scenes and amp models. Double-click or choose Go to to load it on the connected device.";
        }
        PaintIndexSelection(); RenderIndexInspector();
    }

    private void PaintIndexSelection()
    {
        if (_indexList is null) { return; }
        foreach (var row in _indexList.Items.OfType<ListBoxItem>())
        {
            row.Background = row.IsSelected ? ThemeBrush("SelectedBrush") : SurfaceBrush;
            row.BorderBrush = ThemeBrush("RowSeparatorBrush");
            row.BorderThickness = new Thickness(0, 0, 0, 1);
        }
    }

    private Border BuildIndexSceneChip(int scene, string tag)
    {
        var chip = BuildFavoriteTagChip($"S{scene + 1} {tag}", false);
        chip.Background = InsetBrush;
        return chip;
    }

    private void RenderIndexInspector()
    {
        if (_indexInspector is null) { return; }
        UpdateIndexSendControls();
        _indexInspector.Children.Clear();
        _indexInspectorHeader!.Children.Clear();
        _indexInspectorScroll!.Offset = default;
        _indexInspectorFrame!.IsVisible = false;
        _indexResultsPane!.DockOpen = false;
        if (_indexInspectorDismissed || _indexSelectedSlot is not int slot || _indexCache?.Browsable is not { } scan || !scan.Presets.TryGetValue(slot, out var preset)) { return; }
        preset = AmpBrowserCatalog.ResolveForDisplay(preset, scan.EffectiveFirmware);
        _indexInspectorFrame.IsVisible = true;
        _indexResultsPane.DockOpen = true;
        var annotation = _indexProfile.Find(_indexCache.Device.Id, preset, scan.Firmware);
        var header = new Grid { Name = "IndexInspectorToolbar", ColumnDefinitions = new("*,16,Auto,8,Auto,8,Auto,16,Auto") };
        header.Children.Add(BuildIndexInspectorIdentity($"{slot + _settings.DisplayOffset:D3}  {preset.Name}", annotation?.Tags ?? []));
        var close = IndexButton("Close", "IndexCloseInspector");
        close.MinHeight = 28; close.FontSize = 12; close.Padding = new(8, 2);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Center;
        close.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new PathIcon
                {
                    Data = Geometry.Parse("M1,0 L6,5 L11,0 L12,1 L7,6 L12,11 L11,12 L6,7 L1,12 L0,11 L5,6 L0,1 Z"),
                    Width = 12, Height = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock { Text = "Close", VerticalAlignment = VerticalAlignment.Center },
            },
        };
        AutomationProperties.SetName(close, "Close details");
        ToolTip.SetTip(close, "Close details (Esc)");
        close.Click += (_, _) => CloseIndexInspector();
        Grid.SetColumn(close, 8);
        var model = _indexCache.Device.Variant.ToDeviceModel();
        var goTo = new DeviceGoToButton("Go to preset", "IndexGoToPreset", () => preset.Slot,
            selected => GoToDevice(selected, null, model), () => DeviceNavigationUnavailable(model))
        { MinHeight = 28, FontSize = 12, Padding = new(8, 2) };
        Grid.SetColumn(goTo, 4); header.Children.Add(goTo);
        var editTags = IndexButton(annotation?.Tags.Count > 0 ? "Edit tags" : "+ Add tag", "IndexPresetTags");
        editTags.VerticalAlignment = VerticalAlignment.Center;
        editTags.IsEnabled = !preset.NameOnlyEmpty;
        AutomationProperties.SetName(editTags, $"Edit tags for preset {preset.Name}");
        editTags.Click += async (_, _) => await EditIndexTagsAsync(preset, null);
        Grid.SetColumn(editTags, 6); header.Children.Add(editTags);
        if (preset.NameOnlyEmpty)
        {
            header.Children.Add(close); _indexInspectorHeader.Children.Add(header);
            _indexInspector.Children.Add(new TextBlock { Text = "Reported empty by the device; scene and amp data were not downloaded.", Foreground = SecondaryBrush, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            return;
        }
        if (_ampContains is not null)
        {
            _indexInspector.Children.Add(new TextBlock { Name = "IndexAmpMatches", Text = string.Join("   ·   ", _ampContains.MatchingChannels(preset)), Foreground = AccentBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4) });
        }
        var scenes = new Grid { Name = "IndexScenes" };
        for (int scene = 0; scene < 8; scene++)
        {
            var tags = annotation?.SceneTags.GetValueOrDefault(scene) ?? [];
            var row = BuildIndexSceneRow(preset, scene, tags);
            scenes.Children.Add(row);
        }
        var headings = new Grid { Height = 28 };
        var sceneSection = new IndexSceneSectionPanel(headings, scenes)
        {
            Name = "IndexSceneSection",
            HeadingForeground = SecondaryBrush,
            HeadingBackground = InsetBrush,
            Spacing = 0,
            Children = { headings, scenes, new TextBlock { Text = "A–D = amp channel · Off = bypassed · Go to loads the preset and scene on your device.", FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap, Margin = new(6, 6, 0, 0) } },
        };
        var ampSection = new StackPanel { Name = "IndexAmpSection" };
        ampSection.Spacing = 8; ampSection.IsVisible = false;
        ampSection.Children.Add(BuildIndexAmpTable(preset));
        var tabs = new TabStrip
        {
            Name = "IndexDetailTabs",
            SelectedIndex = 0,
            Items = { new TabStripItem { Name = "IndexShowScenes", Content = "Scenes (8)", FontSize = 13, Padding = new(8, 4), MinHeight = 28 }, new TabStripItem { Name = "IndexShowAmpModels", Content = "Amp models", FontSize = 13, Padding = new(8, 4), MinHeight = 28 } }
        };
        AutomationProperties.SetName(tabs, "Preset information");
        tabs.SelectionChanged += (_, _) =>
        {
            sceneSection.IsVisible = tabs.SelectedIndex == 0;
            ampSection.IsVisible = tabs.SelectedIndex == 1;
            _indexInspectorScroll.Offset = default;
        };
        tabs.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) { return; }
            tabs.SelectedIndex = e.Key is Key.Left or Key.Home ? 0 : 1;
            (tabs.SelectedItem as TabStripItem)?.Focus();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        tabs.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tabs, 2); header.Children.Insert(1, tabs);
        header.Children.Add(close); _indexInspectorHeader.Children.Add(header);
        _indexInspector.Children.Add(sceneSection); _indexInspector.Children.Add(ampSection);
    }

    private void CloseIndexInspector()
    {
        var source = _indexList?.SelectedItem as ListBoxItem;
        _indexInspectorDismissed = true;
        _indexSelectedScene = null;
        RenderIndexInspector();
        if (source?.Focus() != true) { _indexList?.Focus(); }
    }

    private Grid BuildIndexSceneRow(IndexedPreset preset, int scene, IEnumerable<string> tags)
    {
        int number = scene + 1;
        var row = new Grid { Name = $"IndexScene{number}Row", MinHeight = 32, ColumnDefinitions = IndexSceneSectionPanel.CellColumns(), Background = _indexSelectedScene == scene ? ThemeBrush("SelectedBrush") : SurfaceBrush, Focusable = true };
        var divider = new Border { Height = 1, Background = ThemeBrush("RowSeparatorBrush"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        Grid.SetColumnSpan(divider, 4); row.Children.Add(divider);
        row.Children.Add(new TextBlock { Text = number.ToString(), FontSize = 12, Foreground = SecondaryBrush, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center });
        var tagPanel = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tag in tags) { tagPanel.Children.Add(BuildFavoriteTagChip(tag, false)); }
        var titleTags = new FavoriteTitleTagsPanel
        {
            Margin = new(6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
        {
            new TextBlock { Name = $"IndexScene{number}Name", Text = preset.SceneNames[scene].Length > 0 ? preset.SceneNames[scene] : "(unnamed scene)", FontSize = 13, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis }, tagPanel,
        }
        };
        Grid.SetColumn(titleTags, 1); row.Children.Add(titleTags);
        string ampSummary = preset.Amps.Length == 0 ? "No Amp blocks" : string.Join("\n", preset.Amps.Select(amp =>
        {
            var state = amp.Scenes[scene];
            var model = amp.Channels.Single(channel => channel.Channel == state.Channel).Model;
            return $"{model.DisplayName} · {(char)('A' + state.Channel)}{(state.Bypassed ? " (off)" : "")}";
        }));
        var summary = new TextBlock { Name = $"IndexScene{number}AmpSummary", Text = ampSummary, FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap, Margin = new(6, 2), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(summary, 2); row.Children.Add(summary);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(0, 2, 6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var edit = IndexButton("Tags…", $"IndexScene{number}Tags");
        AutomationProperties.SetName(edit, $"Edit tags for scene {number}: {preset.SceneNames[scene]}");
        edit.Padding = new(6, 2); ToolTip.SetTip(edit, tags.Any() ? string.Join(", ", tags) : "Add tags to this scene.");
        edit.Click += async (_, _) => await EditIndexTagsAsync(preset, scene);
        actions.Children.Add(edit);
        var model = _indexCache!.Device.Variant.ToDeviceModel();
        var goTo = new DeviceGoToButton("Go to", $"IndexScene{number}GoTo", () => number,
            selected => GoToDevice(preset.Slot, selected, model), () => DeviceNavigationUnavailable(model))
        { MinHeight = 28, Padding = new(6, 2), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(goTo);
        Grid.SetColumn(actions, 3); row.Children.Add(actions);
        void Select()
        {
            _indexSelectedScene = scene;
            UpdateIndexSendControls();
            foreach (var other in row.Parent is Grid scenes ? scenes.Children.OfType<Grid>() : []) { other.Background = other == row ? ThemeBrush("SelectedBrush") : SurfaceBrush; }
        }
        // DoubleTapped requires identical child controls for both presses. A scene is one
        // target even when the pointer moves between its labels and background.
        row.PointerPressed += (_, e) =>
        {
            if (e.Source is not Visual visual || visual is Button || visual.GetVisualAncestors().OfType<Button>().Any()) { return; }
            bool invoke = e.GetCurrentPoint(row).Properties.IsLeftButtonPressed && e.ClickCount % 2 == 0 && _indexSelectedScene == scene;
            Select(); row.Focus();
            if (invoke) { goTo.Go(); e.Handled = true; }
        };
        row.KeyDown += (_, e) =>
        {
            if (e.Source == row && e.Key == Key.Enter) { Select(); goTo.Go(); e.Handled = true; }
        };
        var menu = new MenuItem { Header = "Go to scene" }; menu.Click += (_, _) => goTo.Go();
        row.ContextMenu = new ContextMenu { Items = { menu } };
        row.ContextMenu.Opening += (_, _) => { goTo.Refresh(); menu.IsEnabled = goTo.IsEnabled; };
        string accessibleAmps = preset.Amps.Length == 0 ? "No Amp blocks" : string.Join("; ", preset.Amps.Select(amp =>
        {
            var state = amp.Scenes[scene];
            var selectedModel = amp.Channels.Single(channel => channel.Channel == state.Channel).Model;
            return $"Amp {amp.BlockNumber}: {selectedModel.DisplayName}, channel {(char)('A' + state.Channel)}{(state.Bypassed ? ", bypassed" : "")}";
        }));
        AutomationProperties.SetName(row, $"Scene {number}: {preset.SceneNames[scene]}. {accessibleAmps}");
        ToolTip.SetTip(row, string.Join(" · ", new[] { $"{number} {preset.SceneNames[scene]}", accessibleAmps }.Concat(tags)));
        return row;
    }

    private Control BuildIndexAmpTable(IndexedPreset preset)
    {
        if (preset.Amps.Length == 0)
        {
            return new TextBlock { Name = "IndexAmpChannels", Text = "No Amp blocks in this preset.", FontSize = 12, Foreground = SecondaryBrush, Margin = new Thickness(8) };
        }
        // Wrapping model names remain readable independently of the preset list's height.
        var table = new Grid { Name = "IndexAmpChannels", ColumnDefinitions = new ColumnDefinitions("70," + string.Join(',', preset.Amps.Select(_ => "*"))), RowDefinitions = new RowDefinitions("30,Auto,Auto,Auto,Auto") };
        AutomationProperties.SetName(table, "Saved amp models by channel");
        for (int row = 0; row < 5; row++)
        {
            var stripe = new Border { Background = row % 2 == 0 ? InsetBrush : SurfaceBrush };
            Grid.SetRow(stripe, row); Grid.SetColumnSpan(stripe, preset.Amps.Length + 1); table.Children.Add(stripe);
            var label = new TextBlock { Text = row == 0 ? "Channel" : ((char)('A' + row - 1)).ToString(), FontSize = 12, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0) };
            Grid.SetRow(label, row); table.Children.Add(label);
        }
        for (int column = 0; column < preset.Amps.Length; column++)
        {
            var amp = preset.Amps[column];
            var heading = new TextBlock { Text = $"Amp {amp.BlockNumber}", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
            Grid.SetColumn(heading, column + 1); table.Children.Add(heading);
            foreach (var channel in amp.Channels)
            {
                var family = _ampsCatalog?.Families.FirstOrDefault(f => f.Variants.Any(v => v.ModelId == channel.Model.Id));
                var variant = family?.Variants.First(v => v.ModelId == channel.Model.Id);
                string realAmp = family is null || family.Family.Confidence == "unmapped" ? "Real amp not mapped"
                    : variant!.SpecificModel == variant.Name ? $"{family.Family.Manufacturer} {family.Family.Name}" : variant.SpecificModel;
                var text = new StackPanel
                {
                    Spacing = 4,
                    Margin = new(12, 4),
                    Children =
                {
                    new TextBlock { Text = realAmp, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Fractal: " + channel.Model.DisplayName, FontSize = 13, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap },
                }
                };
                ToolTip.SetTip(text, channel.Model.DisplayName);
                Grid.SetColumn(text, column + 1); Grid.SetRow(text, channel.Channel + 1); table.Children.Add(text);
            }
        }
        return table;
    }

    private FavoriteTitleTagsPanel BuildIndexInspectorIdentity(string title, IEnumerable<string> tags)
    {
        var tagNames = tags.ToArray();
        var tagPanel = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tag in tagNames) { tagPanel.Children.Add(BuildFavoriteTagChip(tag, false)); }
        var titleTags = new FavoriteTitleTagsPanel
        {
            Name = "IndexInspectorIdentity",
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Children =
        {
            new TextBlock { Name = "IndexInspectorTitle", Text = title, Foreground = TextBrush, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, tagPanel,
        }
        };
        string description = tagNames.Length == 0 ? title : $"{title} · {string.Join(", ", tagNames)}";
        AutomationProperties.SetName(titleTags, description);
        ToolTip.SetTip(titleTags, description);
        return titleTags;
    }

    private async Task EditIndexTagsAsync(IndexedPreset preset, int? scene)
    {
        if (_indexCache?.Browsable is not { } scan || _indexError is not null) { return; }
        Guid deviceId = _indexCache.Device.Id;
        int generation = _profileGeneration;
        var annotation = _indexProfile.Find(deviceId, preset, scan.Firmware);
        var tags = new List<string>(scene is int s ? annotation?.SceneTags.GetValueOrDefault(s) ?? [] : annotation?.Tags ?? []);
        var dialog = new Window { Title = scene is int n ? $"Scene {n + 1} tags" : "Preset tags", Width = 440, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = SurfaceBrush };
        var layout = new StackPanel { Margin = new Thickness(18), Spacing = 12 };
        layout.Children.Add(new TextBlock { Text = preset.Name, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis });
        var pills = new WrapPanel(); layout.Children.Add(pills);
        var input = new AutoCompleteBox { Name = "IndexTagEditorInput", Watermark = "Add tag…", ItemsSource = IndexAvailableTags(), FilterMode = AutoCompleteFilterMode.Contains, MinimumPrefixLength = 0 };
        AutomationProperties.SetName(input, scene is null ? "Add preset tag" : "Add shared scene tag");
        if (scene is not null) { ToolTip.SetTip(input, "Tags are shared with matching favorites in this profile."); }
        layout.Children.Add(input);
        void Paint()
        {
            pills.Children.Clear();
            foreach (string tag in tags.ToArray())
            {
                var chip = BuildFavoriteTagChip(tag, false);
                var remove = IndexButton("×"); remove.Background = Brushes.Transparent; remove.BorderThickness = new Thickness(0);
                AutomationProperties.SetName(remove, "Remove tag " + tag);
                remove.Click += (_, _) => { tags.Remove(tag); Paint(); };
                ((StackPanel)chip.Child!).Children.Add(remove); pills.Children.Add(chip);
            }
        }
        void Add()
        {
            string tag = input.Text?.Trim() ?? "";
            if (tag.Length > 0 && tag.Length <= 120 && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) { tags.Add(tag); }
            input.Text = ""; Paint();
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(); e.Handled = true; } };
        var add = IndexButton("Add tag"); add.Click += (_, _) => Add();
        var save = IndexButton("Save tags", "IndexSaveTags");
        save.Click += (_, _) => { Add(); dialog.Close(true); };
        var cancel = IndexButton("Cancel"); cancel.Click += (_, _) => dialog.Close(false);
        layout.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, cancel, save } });
        Paint(); dialog.Content = new ScrollViewer { Content = layout };
        if (await dialog.ShowDialog<bool>(this) && generation == _profileGeneration && _indexCache.Device.Id == deviceId && SaveIndexProfile(profile =>
        {
            var target = profile.GetOrCreate(deviceId, preset, scan.Firmware);
            if (scene is int index) { target.SceneTags[index] = IndexProfile.NormalizeTags(tags); }
            else { target.Tags = IndexProfile.NormalizeTags(tags); }
        })) { RenderIndexResults(); }
    }

    private async Task ShowIndexReviewAsync()
    {
        if (_indexCache?.Browsable is not { } scan) { return; }
        var pending = _indexProfile.Pending(_indexCache).ToArray();
        var dialog = new Window { Title = "Review preset tags", Width = 680, Height = 480, Background = SurfaceBrush, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        var undo = IndexButton("Undo last tag reassignment");
        undo.IsEnabled = _indexProfile.ReviewHistory.Count > 0;
        stack.Children.Add(new TextBlock { Text = "Saved content changed. Choose where these tags belong. Nothing moves until you attach them.", Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
        foreach (var annotation in pending)
        {
            var item = new StackPanel { Spacing = 5, Margin = new Thickness(0, 6) };
            item.Children.Add(new TextBlock { Text = $"{annotation.Slot + _settings.DisplayOffset:D3} {annotation.Name} · {string.Join(", ", annotation.Tags)}", Foreground = TextBrush });
            var choices = scan.Presets.Values.Where(p => !p.NameOnlyEmpty).OrderBy(p => p.Slot).Select(p => new IndexReviewChoice(p, $"{p.Slot + _settings.DisplayOffset:D3} {p.Name}")).ToArray();
            var combo = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch };
            var sameContent = choices.Where(c => c.Preset.ContentSha256 == annotation.ContentSha256).ToArray();
            combo.SelectedItem = sameContent.Length == 1 ? sameContent[0] : choices.FirstOrDefault(c => c.Preset.Slot == annotation.Slot);
            item.Children.Add(combo);
            var mappings = new Dictionary<int, ComboBox>();
            var model = _indexCache!.Device.Variant.ToDeviceModel();
            item.Children.Add(new DeviceGoToButton("Go to preset", "IndexReviewGoToPreset", () => (combo.SelectedItem as IndexReviewChoice)?.Preset.Slot,
                slot => GoToDevice(slot, null, model), () => DeviceNavigationUnavailable(model)));
            foreach (var (scene, tags) in annotation.SceneTags.Where(p => p.Value.Count > 0))
            {
                var destination = new ComboBox { ItemsSource = Enumerable.Range(1, 8).Select(i => $"Scene {i}").ToArray(), SelectedIndex = scene, Width = 110 };
                mappings[scene] = destination;
                item.Children.Add(new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { new TextBlock { Text = $"S{scene + 1}: {string.Join(", ", tags)} →", Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) }, destination,
                    new DeviceGoToButton("Go to scene", "IndexReviewGoToScene", () => combo.SelectedItem is IndexReviewChoice ? destination.SelectedIndex + 1 : null,
                        selected => combo.SelectedItem is IndexReviewChoice choice && GoToDevice(choice.Preset.Slot, selected, model), () => DeviceNavigationUnavailable(model)) { Margin = new(8, 0) },
                }
                });
            }
            var attach = IndexButton("Attach tags to selected preset");
            attach.Click += (_, _) =>
            {
                if (combo.SelectedItem is IndexReviewChoice choice && SaveIndexProfile(profile => profile.Reattach(annotation.Id,
                    choice.Preset, scan.Firmware, mappings.ToDictionary(p => p.Key, p => p.Value.SelectedIndex))))
                { item.IsVisible = false; undo.IsEnabled = true; RenderIndexResults(); }
            };
            item.Children.Add(attach); stack.Children.Add(item);
        }
        undo.Click += (_, _) => { if (SaveIndexProfile(profile => profile.UndoReview())) { RenderIndexResults(); dialog.Close(); } };
        var close = IndexButton("Close"); close.Click += (_, _) => dialog.Close();
        stack.Children.Add(undo); stack.Children.Add(close);
        dialog.Content = new ScrollViewer { Content = stack };
        await dialog.ShowDialog(this);
    }

    private sealed record IndexReviewChoice(IndexedPreset Preset, string Label)
    {
        public override string ToString() => Label;
    }
}

// Match the preset/Favorites table rhythm, with scenes 1–4 and 5–8 kept together.
internal sealed class IndexSceneSectionPanel(Grid headings, Grid scenes) : StackPanel
{
    private int _columns;
    internal IBrush HeadingForeground { get; init; } = Brushes.Gray;
    internal IBrush HeadingBackground { get; init; } = Brushes.Transparent;
    internal static ColumnDefinitions CellColumns() => new("24,*,*,112");

    protected override Size MeasureOverride(Size availableSize)
    {
        const int columns = 2;
        if (_columns != columns)
        {
            _columns = columns;
            headings.Children.Clear(); headings.ColumnDefinitions.Clear();
            scenes.ColumnDefinitions.Clear(); scenes.RowDefinitions.Clear();
            var ranges = FavoriteTableLayout.Distribute(scenes.Children.Count, columns);
            for (int row = 0; row < ranges.Max(r => r.Count); row++) { scenes.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }
            for (int column = 0; column < columns; column++)
            {
                if (column > 0)
                {
                    headings.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(FavoriteTableLayout.Gap)));
                    scenes.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(FavoriteTableLayout.Gap)));
                }
                headings.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                scenes.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var heading = new Grid { ColumnDefinitions = CellColumns(), Background = HeadingBackground };
                foreach (var (text, cell) in new[] { ("#", 0), ("Scene", 1), ("Saved amp", 2) })
                {
                    var label = new TextBlock { Text = text, FontSize = 11, Foreground = HeadingForeground, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(label, cell); heading.Children.Add(label);
                }
                Grid.SetColumn(heading, column * 2); headings.Children.Add(heading);
                var range = ranges[column];
                for (int index = range.Start; index < range.Start + range.Count; index++)
                {
                    Grid.SetColumn(scenes.Children[index], column * 2);
                    Grid.SetRow(scenes.Children[index], index - range.Start);
                }
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
#endif
