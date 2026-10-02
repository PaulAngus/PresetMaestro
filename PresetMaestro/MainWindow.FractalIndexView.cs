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
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private Control? _indexPageControl;
    private ListBox? _indexList;
    private StackPanel? _indexInspector;
    private Border? _indexInspectorFrame;
    private TextBlock? _indexStatus, _indexCount, _indexEmpty;
    private Button? _indexSyncButton, _indexResumeButton, _indexCancelButton, _indexReviewButton;
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
        if (_currentPage == AppPage.PresetIndex) { host.Content = _indexPageControl; }
    }

    partial void AddIndexNavigation(StackPanel nav, Func<string, Control, AppPage, EntryMode?, Button> create)
    {
        var button = create("Preset Index", _indexPageControl!, AppPage.PresetIndex, null);
        button.Click += (_, _) => { RefreshIndexContext(); };
        nav.Children.Add(button);
    }

    private Control BuildIndexPage()
    {
        var page = new Grid { Name = "PresetIndexPage", Margin = new Thickness(20, 12, 20, 20) };
        var card = new Border { Background = ThemeBrush("AppBrush"), BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16, 11, 12, 12) };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto") };
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        title.Children.Add(new TextBlock { Text = "Preset Index", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _indexReviewButton = IndexButton("Needs review", "IndexReview");
        _indexReviewButton.Click += async (_, _) => await ShowIndexReviewAsync();
        _indexSyncButton = IndexButton("Sync device", "IndexSync");
        _indexCheckButton = IndexButton("Check library", "IndexCheckLibrary");
        _indexCheckButton.Click += async (_, _) => await CheckAssignedLibraryAsync();
        _indexSyncButton.Click += async (_, _) => await SyncIndexAsync();
        _indexResumeButton = IndexButton("Resume", "IndexResume");
        _indexResumeButton.Click += async (_, _) => await SyncIndexAsync(resume: true);
        _indexCancelButton = IndexButton("Cancel", "IndexCancel");
        _indexCancelButton.Click += (_, _) => _presetNamesCts?.Cancel();
        actions.Children.Add(_indexReviewButton); actions.Children.Add(_indexCheckButton); actions.Children.Add(_indexSyncButton);
        actions.Children.Add(_indexResumeButton); actions.Children.Add(_indexCancelButton);
        Grid.SetColumn(actions, 1); title.Children.Add(actions); content.Children.Add(title);
        var syncInfo = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 0, 10) };
        syncInfo.Children.Add(new TextBlock { Name = "IndexSyncNotice", Text = "Sync can take several minutes, or longer over slower MIDI connections. You can cancel and resume.", FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap });
        _indexMatchStatus = new TextBlock { Name = "IndexMatchStatus", FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        syncInfo.Children.Add(_indexMatchStatus);
        _indexProgressText = new TextBlock { Name = "IndexProgressText", Text = _indexProgressMessage, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        _indexProgress = new ProgressBar { Name = "IndexProgress", Height = 14, Minimum = 0, Maximum = 512, Foreground = AccentBrush, Background = UiBorderBrush };
        AutomationProperties.SetName(_indexProgress, "Device index sync progress");
        _indexProgressPanel = new StackPanel { Spacing = 7, Children = { _indexProgressText, _indexProgress } };
        syncInfo.Children.Add(_indexProgressPanel);
        Grid.SetRow(syncInfo, 1); content.Children.Add(syncInfo);
        var search = BuildIndexSearch(); Grid.SetRow(search, 2); content.Children.Add(search);
        _indexCount = new TextBlock { Name = "IndexResultCount", FontSize = 11, Foreground = SecondaryBrush, Margin = new Thickness(0, 4, 0, 4) };
        Grid.SetRow(_indexCount, 3); content.Children.Add(_indexCount);
        var results = new Grid { Margin = new Thickness(0, 5, 0, 5), MinHeight = 100 };
        _indexList = new ListBox { Name = "IndexPresetList", Background = SurfaceBrush, BorderThickness = new Thickness(0), ItemContainerTheme = CompactListItemTheme() };
        _indexList.ItemsPanel = new FuncTemplate<Panel?>(() => new IndexTablesPanel());
        ScrollViewer.SetHorizontalScrollBarVisibility(_indexList, ScrollBarVisibility.Disabled);
        _indexList.SelectionChanged += (_, _) =>
        {
            if (_indexRendering) { return; }
            _indexSelectedSlot = (_indexList.SelectedItem as ListBoxItem)?.Tag is IndexedPreset preset ? preset.Slot : null;
            if (_indexList.SelectedIndex >= 0) { _indexList.ScrollIntoView(_indexList.SelectedIndex); }
            PaintIndexSelection(); RenderIndexInspector();
        };
        results.Children.Add(_indexList);
        _indexEmpty = new TextBlock { Name = "IndexEmptyState", TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        results.Children.Add(_indexEmpty);
        var listFrame = new Border { Child = results, Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 0), ClipToBounds = true };
        Grid.SetRow(listFrame, 4); content.Children.Add(listFrame);
        _indexInspector = new StackPanel { Name = "IndexInspector", Spacing = 0 };
        _indexInspectorFrame = new Border { Name = "IndexInspectorFrame", Child = _indexInspector, Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(0, 3, 0, 0), Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(_indexInspectorFrame, 5); content.Children.Add(_indexInspectorFrame);
        _indexStatus = new TextBlock { Name = "IndexStatus", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush, Margin = new Thickness(0, 7, 0, 0) };
        Grid.SetRow(_indexStatus, 6); content.Children.Add(_indexStatus);
        card.Child = content; page.Children.Add(card);
        RenderIndexResults(); UpdateIndexButtons();
        return page;
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
        if (_indexList is null || _indexCount is null || _indexInspector is null) { return; }
        var scan = _indexCache?.Browsable;
        var chips = _indexSearchChips.ToList();
        if (!string.IsNullOrWhiteSpace(_indexSearchBox?.Text)) { chips.Add(IndexPreviewChip(_indexSearchBox.Text.Trim())); }
        var saved = scan?.Presets.Values.OrderBy(p => p.Slot).ToArray() ?? [];
        // Use exactly the Favorites picker's empty-edge policy. Unknown names and interior gaps remain.
        var visibleSlots = PresetSelection.TrimExplicitEmptyEdges(saved.Select(p => new PresetChoice(p.Slot, p.Name)).ToArray())
            .Select(p => p.Slot).ToHashSet();
        var presets = saved.Where(p => visibleSlots.Contains(p.Slot)).ToArray();
        if (_indexSelectedSlot is int selected && !visibleSlots.Contains(selected)) { _indexSelectedSlot = null; }
        var filtered = presets.Where(p => IndexSearch.Matches(p, _indexProfile.Find(_indexCache!.Device.Id, p, scan!.Firmware),
            chips.Where(c => !c.IsTag).Select(c => c.Value), chips.Where(c => c.IsTag).Select(c => c.Value), _indexAllTags, _settings.DisplayOffset)).ToArray();
        _indexRendering = true;
        _indexList.Items.Clear();
        foreach (var preset in filtered)
        {
            var annotation = _indexProfile.Find(_indexCache!.Device.Id, preset, scan!.Firmware);
            var row = new Grid { Height = 32, ColumnDefinitions = new ColumnDefinitions("42,*"), Margin = new Thickness(4, 0) };
            row.Children.Add(new TextBlock { Text = (preset.Slot + _settings.DisplayOffset).ToString("D3"), FontSize = 12, Foreground = AccentBrush, VerticalAlignment = VerticalAlignment.Center });
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
            var item = new ListBoxItem { Tag = preset, Content = row, Padding = new Thickness(3, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, $"Preset {preset.Slot + _settings.DisplayOffset}: {preset.Name}");
            ToolTip.SetTip(item, string.Join(" · ", new[] { preset.Name }.Concat(annotation?.Tags ?? [])
                .Concat(annotation?.SceneTags.SelectMany(p => p.Value.Select(t => $"S{p.Key + 1}: {t}")) ?? [])));
            _indexList.Items.Add(item);
        }
        _indexList.SelectedItem = _indexList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((IndexedPreset)i.Tag!).Slot == _indexSelectedSlot);
        _indexRendering = false;
        _indexEmpty!.IsVisible = filtered.Length == 0;
        _indexEmpty.Text = _indexCache is null ? "Assign a device library in Config → Device library for this profile. Then return here to sync its saved presets."
            : _indexScanning && presets.Length == 0 ? "Sync is in progress. Completed reads are being saved; you can cancel and resume."
            : saved.Length > 0 && presets.Length == 0 ? "No populated presets. Empty slots at the start and end are hidden, as in Select Preset."
            : presets.Length == 0 ? "No saved presets have been indexed yet. Connect this device and choose Sync device." : "No presets match this search.";
        int pending = _indexCache is null ? 0 : _indexProfile.Pending(_indexCache).Count();
        _indexReviewButton!.Content = $"Needs review ({pending})";
        _indexReviewButton.IsVisible = pending > 0 || _indexProfile.ReviewHistory.Count > 0;
        string state = _indexCache?.Committed is not null ? "complete saved index" : "partial saved index";
        _indexCount.Text = $"{filtered.Length} of {presets.Length} presets" + (scan is null ? "" : $" · {state} · {scan.StartedAt.LocalDateTime:g}" + (_indexCache!.Imported ? " · imported / offline" : ""));
        if (!_indexScanning)
        {
            _indexProgress!.Value = scan?.Presets.Count ?? 0;
            _indexProgress.Maximum = _indexCache is null ? 512 : FractalDeviceDefinition.For(_indexCache.Device.Variant).PresetSlots;
            _indexStatus!.Text = _indexCache?.Committed is not null && _indexCache.LastAttempt is { Status: not "Complete" }
                ? "Showing the last complete index. An unfinished scan is available to resume."
                : "Click a preset to inspect it. Tags are saved in this profile. Scene tags use S1–S8 hints.";
        }
        PaintIndexSelection(); RenderIndexInspector();
    }

    private void PaintIndexSelection()
    {
        if (_indexList is null) { return; }
        int i = 0;
        foreach (var row in _indexList.Items.OfType<ListBoxItem>())
        {
            row.Background = row.IsSelected ? ThemeBrush("SelectedBrush") : (i++ % 2 == 0 ? SurfaceBrush : InsetBrush);
            row.BorderBrush = row.IsSelected ? AccentBrush : Brushes.Transparent;
            row.BorderThickness = new Thickness(3, 0, 0, 0);
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
        _indexInspector.Children.Clear();
        _indexInspectorFrame!.IsVisible = false;
        if (_indexSelectedSlot is not int slot || _indexCache?.Browsable is not { } scan || !scan.Presets.TryGetValue(slot, out var preset)) { return; }
        _indexInspectorFrame.IsVisible = true;
        if (preset.NameOnlyEmpty)
        {
            _indexInspector.Children.Add(new TextBlock { Text = $"{slot + _settings.DisplayOffset:D3}  <EMPTY> · Reported empty by the device; preset data was not downloaded.", Foreground = SecondaryBrush, FontSize = 12, Margin = new Thickness(6), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var annotation = _indexProfile.Find(_indexCache.Device.Id, preset, scan.Firmware);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = InsetBrush };
        header.Children.Add(BuildIndexTagLine($"{slot + _settings.DisplayOffset:D3}  {preset.Name}", annotation?.Tags ?? [], null, preset));
        var close = IndexButton("Close", "IndexCloseInspector");
        close.Click += (_, _) => { _indexSelectedSlot = null; _indexList!.SelectedItem = null; RenderIndexInspector(); };
        Grid.SetColumn(close, 1); header.Children.Add(close); _indexInspector.Children.Add(header);
        var scenes = new Grid { Name = "IndexScenes", ColumnDefinitions = new ColumnDefinitions("*,14,*"), RowDefinitions = new RowDefinitions("32,32,32,32,0,0,0,0") };
        for (int scene = 0; scene < 8; scene++)
        {
            var tags = annotation?.SceneTags.GetValueOrDefault(scene) ?? [];
            var row = BuildIndexTagLine($"{scene + 1}   {(preset.SceneNames[scene].Length > 0 ? preset.SceneNames[scene] : "(unnamed scene)")}", tags, scene, preset);
            row.Background = scene % 2 == 0 ? SurfaceBrush : InsetBrush;
            Grid.SetColumn(row, scene < 4 ? 0 : 2); Grid.SetRow(row, scene % 4); scenes.Children.Add(row);
        }
        var headings = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*"), Height = 24 };
        for (int group = 0; group < 2; group++)
        {
            var heading = new Grid { ColumnDefinitions = IndexSceneColumns(preset) };
            heading.Children.Add(new TextBlock { Text = "Scene", FontSize = 11, Foreground = SecondaryBrush, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center });
            for (int amp = 0; amp < preset.Amps.Length; amp++)
            {
                var text = new TextBlock { Text = $"Amp {preset.Amps[amp].BlockNumber}", FontSize = 11, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(text, amp + 2); heading.Children.Add(text);
            }
            Grid.SetColumn(heading, group * 2); headings.Children.Add(heading);
        }
        var sceneSection = new IndexSceneSectionPanel(headings, scenes)
        {
            Name = "IndexSceneSection",
            Children = { IndexSectionHeading("Scenes & tags"), headings, scenes },
        };
        var ampSection = new StackPanel { Name = "IndexAmpSection" };
        ampSection.Children.Add(IndexSectionHeading("Amp channels"));
        ampSection.Children.Add(BuildIndexAmpTable(preset));
        _indexInspector.Children.Add(new IndexInspectorSectionsPanel
        {
            Margin = new Thickness(6, 4, 6, 6),
            Children = { sceneSection, ampSection },
        });
    }

    private static TextBlock IndexSectionHeading(string title) => new()
    {
        Text = title,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = TextBrush,
        Height = 28,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Thickness(6, 5),
    };

    private static ColumnDefinitions IndexSceneColumns(IndexedPreset preset) =>
        new("*,100" + string.Concat(preset.Amps.Select(_ => ",44")));

    private static Control BuildIndexAmpTable(IndexedPreset preset)
    {
        if (preset.Amps.Length == 0)
        {
            return new TextBlock { Name = "IndexAmpChannels", Text = "No Amp blocks in this preset.", FontSize = 12, Foreground = SecondaryBrush, Margin = new Thickness(8) };
        }
        // Content-sized columns and four short rows; the surrounding Auto row gives spare height to the preset list.
        var table = new Grid { Name = "IndexAmpChannels", ColumnDefinitions = new ColumnDefinitions("60," + string.Join(',', preset.Amps.Select(_ => "Auto"))), RowDefinitions = new RowDefinitions("24,32,32,32,32"), HorizontalAlignment = HorizontalAlignment.Left };
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
                var text = new TextBlock { Text = channel.Model.DisplayName, FontSize = 12, Foreground = TextBrush, MaxWidth = 310, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 24, 0) };
                ToolTip.SetTip(text, channel.Model.DisplayName);
                Grid.SetColumn(text, column + 1); Grid.SetRow(text, channel.Channel + 1); table.Children.Add(text);
            }
        }
        return table;
    }

    private Grid BuildIndexTagLine(string title, IEnumerable<string> tags, int? scene, IndexedPreset preset)
    {
        var row = new Grid { Height = 32, ColumnDefinitions = scene.HasValue ? IndexSceneColumns(preset) : new ColumnDefinitions("*,Auto"), ClipToBounds = true };
        var tagPanel = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tag in tags) { tagPanel.Children.Add(BuildFavoriteTagChip(tag, false)); }
        var titleTags = new FavoriteTitleTagsPanel
        {
            Margin = new Thickness(6, 0),
            Children =
        {
            new TextBlock { Text = title, Foreground = TextBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, tagPanel,
        }
        };
        row.Children.Add(titleTags);
        if (scene is int sceneNumber)
        {
            for (int column = 0; column < preset.Amps.Length; column++)
            {
                var amp = preset.Amps[column];
                var state = amp.Scenes[sceneNumber];
                var cell = new Border
                {
                    BorderBrush = UiBorderBrush,
                    BorderThickness = new Thickness(column == 0 ? 1 : 0, 0, 0, 0),
                    Child = new TextBlock { Text = $"{(char)('A' + state.Channel)}{(state.Bypassed ? " off" : "")}", FontSize = 11, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                string description = $"Scene {sceneNumber + 1}, Amp {amp.BlockNumber}, channel {(char)('A' + state.Channel)}: {amp.Channels[state.Channel].Model.DisplayName} ({(state.Bypassed ? "bypassed" : "engaged")})";
                ToolTip.SetTip(cell, description); AutomationProperties.SetName(cell, description);
                Grid.SetColumn(cell, column + 2); row.Children.Add(cell);
            }
        }
        string scope = scene.HasValue ? "scene" : "preset";
        var edit = IndexButton(tags.Any() ? $"Edit {scope} tags" : $"+ {scope} tag", scene is int s ? $"IndexScene{s + 1}Tags" : "IndexPresetTags");
        AutomationProperties.SetName(edit, scene is int index ? $"Edit tags for scene {index + 1}: {preset.SceneNames[index]}" : $"Edit tags for preset {preset.Name}");
        edit.Background = Brushes.Transparent; edit.BorderThickness = new Thickness(0);
        edit.Click += async (_, _) => await EditIndexTagsAsync(preset, scene);
        Grid.SetColumn(edit, 1); row.Children.Add(edit);
        return row;
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
            foreach (var (scene, tags) in annotation.SceneTags.Where(p => p.Value.Count > 0))
            {
                var destination = new ComboBox { ItemsSource = Enumerable.Range(1, 8).Select(i => $"Scene {i}").ToArray(), SelectedIndex = scene, Width = 110 };
                mappings[scene] = destination;
                item.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = $"S{scene + 1}: {string.Join(", ", tags)} →", Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center }, destination } });
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

// One compact row per preset; Amp channels and scenes are built only for the selection.
internal sealed class IndexSceneSectionPanel(Grid headings, Grid scenes) : StackPanel
{
    private bool _twoColumns = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        bool twoColumns = availableSize.Width >= 720;
        if (_twoColumns != twoColumns)
        {
            _twoColumns = twoColumns;
            headings.ColumnDefinitions[1].Width = new GridLength(twoColumns ? 14 : 0);
            headings.ColumnDefinitions[2].Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            headings.Children[1].IsVisible = twoColumns;
            scenes.ColumnDefinitions[1].Width = new GridLength(twoColumns ? 14 : 0);
            scenes.ColumnDefinitions[2].Width = twoColumns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            for (int row = 4; row < 8; row++) { scenes.RowDefinitions[row].Height = new GridLength(twoColumns ? 0 : 32); }
            for (int scene = 0; scene < scenes.Children.Count; scene++)
            {
                Grid.SetColumn(scenes.Children[scene], twoColumns && scene >= 4 ? 2 : 0);
                Grid.SetRow(scenes.Children[scene], twoColumns ? scene % 4 : scene);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}

internal sealed class IndexInspectorSectionsPanel : Panel
{
    private const double Gap = 16;
    private bool _sideBySide;
    private double _ampWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2) { return default; }
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 1200;
        Children[1].Measure(new Size(width, double.PositiveInfinity));
        _ampWidth = Children[1].DesiredSize.Width;
        _sideBySide = width >= 400 + Gap + _ampWidth;
        Children[0].Measure(new Size(_sideBySide ? width - Gap - _ampWidth : width, double.PositiveInfinity));
        return new Size(width, _sideBySide ? Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height)
            : Children[0].DesiredSize.Height + Gap + Children[1].DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 2) { return finalSize; }
        double sceneWidth = _sideBySide ? Math.Max(0, finalSize.Width - Gap - _ampWidth) : finalSize.Width;
        Children[0].Arrange(new Rect(0, 0, sceneWidth, Children[0].DesiredSize.Height));
        Children[1].Arrange(_sideBySide
            ? new Rect(sceneWidth + Gap, 0, _ampWidth, Children[1].DesiredSize.Height)
            : new Rect(0, Children[0].DesiredSize.Height + Gap, finalSize.Width, Children[1].DesiredSize.Height));
        return finalSize;
    }
}

internal sealed class IndexTablesPanel : Panel, INavigableContainer
{
    private int _columns = 2, _rows;
    private double _width;
    protected override Size MeasureOverride(Size availableSize)
    {
        _width = double.IsFinite(availableSize.Width) ? availableSize.Width : 900;
        _columns = Math.Max(1, (int)(_width / 340));
        _rows = (Children.Count + _columns - 1) / _columns;
        foreach (var child in Children) { child.Measure(new Size(Math.Max(0, _width / _columns - 12), 32)); }
        return new Size(_width, _rows * 32);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_rows == 0) { return finalSize; }
        double width = _width / _columns;
        for (int i = 0; i < Children.Count; i++) { Children[i].Arrange(new Rect(i / _rows * width, i % _rows * 32, Math.Max(0, width - 12), 32)); }
        return finalSize;
    }
    public IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (Children.Count == 0) { return null; }
        int current = from is Control control ? Children.IndexOf(control) : 0;
        int delta = direction switch { NavigationDirection.Up or NavigationDirection.Previous => -1, NavigationDirection.Down or NavigationDirection.Next => 1, NavigationDirection.Left => -_rows, NavigationDirection.Right => _rows, _ => 0 };
        int index = direction switch { NavigationDirection.First => 0, NavigationDirection.Last => Children.Count - 1, _ => Math.Clamp(current + delta, 0, Children.Count - 1) };
        return Children[index];
    }
}
#endif
