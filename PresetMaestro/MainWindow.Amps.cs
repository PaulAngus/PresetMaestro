#if FRACTAL_INDEX
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private static readonly string[] AllAmpManufacturers = ["All manufacturers"];
    private Control? _ampsPage;
    private Button? _ampsNavigation, _presetIndexNavigation;
    private TextBox? _ampsSearch;
    private ComboBox? _ampsManufacturer;
    private enum AmpUsageFilter { All, Used, Unused }
    private ComboBox? _ampsUsage;
    private TextBlock? _ampsUnusedNotice;
    private AmpUsageFilter SelectedAmpUsage => _ampsUsage?.SelectedItem is ComboBoxItem { Tag: AmpUsageFilter usage } ? usage : AmpUsageFilter.All;
    private ListBox? _ampsList;
    private TextBlock? _ampsCoverage, _ampsCount, _ampsEmpty;
    private Button? _ampsCoverageDetails;
    private StackPanel? _ampsFirmwareNotice;
    private TextBlock? _ampsUsageNotice;
    private Grid? _ampsDirectory;
    private ScrollViewer? _ampsDetailScroll;
    private StackPanel? _ampsDetail;
    private string? _ampsSelectedFamily;
    private string? _ampsSelectedVariant;
    private HashSet<string>? _ampsIncludedVariants;
    private Guid? _ampsDeviceId;
    private string? _ampsFirmware;
    private bool _renderingAmps;
    private bool _ampsDetailOpen;
    private Vector _ampsDirectoryOffset;
    private int _ampSceneMode;
    private string? _ampReferenceError;
    private static readonly string[] AmpSceneModes = ["Any saved channel", "Saved scene selects", "Saved scene engaged"];
    private AmpBrowserDirectory _ampsCatalog = new("", "", false, []);
    private AmpContainsFilter? _ampContains;
    private StackPanel? _ampFilterRow;
    private TextBlock? _ampFilterLabel;
    private AmpReferenceStore AmpReferences => new(Path.Combine(_profileStore?.DirectoryPath ?? Path.GetDirectoryName(Core.SettingsManager.SettingsPath)!, "amp-references.json"));

    private Control BuildAmpsPage()
    {
        string search = _ampsSearch?.Text ?? "";
        string manufacturer = _ampsManufacturer?.SelectedItem as string ?? "All manufacturers";
        var usage = SelectedAmpUsage;
        var body = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto") };
        body.Children.Add(new TextBlock { Text = "Amps", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TextBrush, Margin = new(0, 0, 0, 10) });
        var searchRow = new Grid { ColumnDefinitions = new("*,12,Auto") };
        _ampsSearch = new TextBox { Name = "AmpsSearch", Text = search, Watermark = "Search manufacturer, real amp or Fractal model…", MinHeight = 32, Background = InsetBrush };
        AutomationProperties.SetName(_ampsSearch, "Search amps");
        _ampsSearch.TextChanged += (_, _) => FilterAmps();
        searchRow.Children.Add(_ampsSearch);
        var clear = IndexButton("Clear search", "AmpsClearSearch");
        clear.Click += (_, _) => _ampsSearch.Text = "";
        Grid.SetColumn(clear, 2); searchRow.Children.Add(clear); Grid.SetRow(searchRow, 1); body.Children.Add(searchRow);
        var filters = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8) };
        _ampsManufacturer = new ComboBox { Name = "AmpsManufacturer", Width = 220, MinHeight = 30, Margin = new(0, 0, 16, 0) };
        AutomationProperties.SetName(_ampsManufacturer, "Manufacturer");
        _ampsManufacturer.ItemsSource = new[] { manufacturer }; _ampsManufacturer.SelectedIndex = 0;
        _ampsManufacturer.SelectionChanged += (_, _) => FilterAmps();
        _ampsUsage = new ComboBox { Name = "AmpsPresetUsage", Width = 230, MinHeight = 30, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_ampsUsage, "Preset usage");
        _ampsUsage.ItemsSource = new[]
        {
            new ComboBoxItem { Name = "AmpsAll", Content = "All amps", Tag = AmpUsageFilter.All },
            new ComboBoxItem { Name = "AmpsUsedOnly", Content = "Used in my presets", Tag = AmpUsageFilter.Used },
            new ComboBoxItem { Name = "AmpsUnusedOnly", Content = "Not used in my presets", Tag = AmpUsageFilter.Unused },
        };
        _ampsUsage.SelectedIndex = (int)usage;
        _ampsUsage.SelectionChanged += (_, _) => FilterAmps();
        _ampsCount = new TextBlock { Name = "AmpsCount", Foreground = SecondaryBrush, FontSize = 12, Margin = new(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        filters.Children.Add(_ampsManufacturer); filters.Children.Add(_ampsUsage); filters.Children.Add(_ampsCount);
        _ampsUnusedNotice = new TextBlock { Name = "AmpsUnusedNotice", FontSize = 14, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new(0, 0, 0, 8) };
        var filterArea = new StackPanel { Children = { filters, _ampsUnusedNotice } };
        Grid.SetRow(filterArea, 2); body.Children.Add(filterArea);
        _ampsFirmwareNotice = new StackPanel { Name = "AmpsFirmwareNotice", Spacing = 6, Margin = new(0, 0, 0, 10), IsVisible = false };
        _ampsUsageNotice = AmpText("", 14);
        _ampsFirmwareNotice.Children.Add(_ampsUsageNotice);
        var openIndex = IndexButton("Open Preset Index", "AmpsOpenPresetIndex");
        openIndex.Click += (_, _) => _presetIndexNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        _ampsFirmwareNotice.Children.Add(openIndex);
        Grid.SetRow(_ampsFirmwareNotice, 3); body.Children.Add(_ampsFirmwareNotice);
        var stage = new Grid { RowDefinitions = new("*,Auto") };
        _ampsDirectory = new Grid();
        _ampsList = new ListBox { Name = "AmpsDirectory", Background = AppBrush, BorderThickness = new(0), ItemContainerTheme = CompactListItemTheme() };
        _ampsList.ItemsPanel = new FuncTemplate<Panel?>(() => new AmpDirectoryPanel());
        ScrollViewer.SetHorizontalScrollBarVisibility(_ampsList, ScrollBarVisibility.Disabled);
        _ampsList.SelectionChanged += (_, _) =>
        {
            if (!_renderingAmps && _ampsList.SelectedItem is ListBoxItem { Tag: BrowserAmpFamily family })
            {
                if (_ampsSelectedFamily != family.Family.Id) { _ampsSelectedVariant = null; _ampsIncludedVariants = null; }
                _ampsSelectedFamily = family.Family.Id;
                foreach (var row in _ampsList.Items.OfType<ListBoxItem>()) { row.Background = row.IsSelected ? ThemeBrush("SelectedBrush") : SurfaceBrush; }
                if (_ampsDetailOpen) { RenderAmpDetail(); }
            }
        };
        _ampsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _ampsList.SelectedItem is ListBoxItem { Tag: BrowserAmpFamily family })
            { OpenAmpFamily(family); e.Handled = true; }
        };
        _ampsDirectory.Children.Add(_ampsList);
        _ampsEmpty = new TextBlock { Name = "AmpsEmpty", Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _ampsDirectory.Children.Add(_ampsEmpty); stage.Children.Add(_ampsDirectory);
        _ampsDetail = new StackPanel { Name = "AmpsDetail", Spacing = 8, Margin = new(14) };
        _ampsDetailScroll = new ScrollViewer { Content = new Border { Child = _ampsDetail, Background = SurfaceBrush, BorderBrush = AccentBrush, BorderThickness = new(0, 3, 0, 0) }, IsVisible = false, MaxHeight = 280, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 12, 0, 0) };
        Grid.SetRow(_ampsDetailScroll, 1);
        stage.Children.Add(_ampsDetailScroll);
        var frame = new Border { Child = stage, Background = AppBrush, ClipToBounds = true };
        Grid.SetRow(frame, 4); body.Children.Add(frame);
        _ampsCoverage = new TextBlock { Name = "AmpsCoverage", Foreground = SecondaryBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        _ampsCoverage.IsVisible = false;
        _ampsCoverageDetails = IndexButton("Catalogue status", "AmpsCatalogueStatus");
        _ampsCoverageDetails.FontSize = 12;
        _ampsCoverageDetails.Click += (_, _) => _ampsCoverage.IsVisible = !_ampsCoverage.IsVisible;
        var coverage = new StackPanel { Margin = new(0, 8, 0, 0), Children = { _ampsCoverageDetails, _ampsCoverage } };
        Grid.SetRow(coverage, 5); body.Children.Add(coverage);
        var page = new Border { Name = "AmpsPage", Margin = new(20, 12, 20, 20), Padding = new(16, 11, 12, 12), Background = AppBrush, BorderBrush = UiBorderBrush, BorderThickness = new(1), CornerRadius = new(9), Child = body };
        RefreshAmpsContext();
        return page;
    }

    private void RefreshAmpsContext()
    {
        if (_ampsList is null) { return; }
        bool changed = _ampsDeviceId != _indexCache?.Device.Id || !AmpBrowserCatalog.SameFirmware(_ampsFirmware, _indexCache?.Device.Firmware);
        _ampsDeviceId = _indexCache?.Device.Id; _ampsFirmware = _indexCache?.Device.Firmware;
        if (changed) { _ampsSelectedFamily = null; _ampsSelectedVariant = null; _ampsIncludedVariants = null; _ampsDetailOpen = false; _ampContains = null; }
        _ampsCatalog = AmpBrowserCatalog.Build(_indexCache);
        string selected = _ampsManufacturer?.SelectedItem as string ?? "All manufacturers";
        _renderingAmps = true;
        var manufacturers = AllAmpManufacturers.Concat(_ampsCatalog.Families.Select(f => f.Family.Manufacturer).Distinct()).ToArray();
        _ampsManufacturer!.ItemsSource = manufacturers;
        _ampsManufacturer.SelectedItem = manufacturers.Contains(selected) ? selected : manufacturers[0];
        _ampsFirmwareNotice!.IsVisible = _indexCache is not null && !_ampsCatalog.HasUsageData;
        _ampsUsageNotice!.Text = _ampsCatalog.IsReferencePreview
            ? "Firmware has not been detected. Browse the FM9 reference catalogue below. Connect in Config, then choose Sync device in Preset Index to read amp usage."
            : _indexCache?.Browsable is null
                ? "Amp usage has not been synced. Preset-name sync updates names only. Choose Sync device in Preset Index to read amp data."
                : string.IsNullOrWhiteSpace(_indexCache.Browsable.EffectiveFirmware) && !_indexCache.Imported
                    ? "Check this library with your device to show which amps your saved presets use. Open Sync options in Config and choose Quick check or Full check."
                    : "Your device’s software has changed since this amp information was saved. Choose Sync device in Preset Index to refresh the saved amp data.";
        var usageOptions = _ampsUsage!.Items.OfType<ComboBoxItem>().ToArray();
        usageOptions[1].IsEnabled = _ampsCatalog.HasUsageData;
        usageOptions[2].IsEnabled = _ampsCatalog.CanListUnusedAmps;
        ToolTip.SetTip(_ampsUsage, "Usage includes every saved amp channel, even if no scene selects it. Not used shows families with no variants in your presets.");
        if (_ampsUsage.SelectedItem is ComboBoxItem { IsEnabled: false }) { _ampsUsage.SelectedIndex = 0; }
        _ampsUnusedNotice!.IsVisible = _ampsCatalog.HasUsageData && !_ampsCatalog.CanListUnusedAmps;
        _ampsUnusedNotice.Text = !_ampsCatalog.HasCatalogueRoster && !_ampsCatalog.IsComplete
            ? "The app does not yet include an amp catalogue for this device’s software, so it cannot list unused amps."
            : "To find amps not used in any preset, complete a library sync in Preset Index.";
        _renderingAmps = false;
        _ampsCoverage!.Text = _ampsCatalog.Coverage + (_indexCache is null ? "" : $" · Catalogue {_ampsCatalog.Version}" +
            (_indexCache.Browsable is { } scan ? $" · Saved scan {scan.StartedAt.LocalDateTime:g}" : " · No saved scan") +
            (_detectedDevice is null || !_midi.InputOpen || _indexCache.Imported ? " · Offline" : "")) +
            (_ampReferenceError is null ? "" : " · " + _ampReferenceError);
        _ampsCoverageDetails!.Content = _ampsCatalog.IsReferencePreview ? "Reference catalogue · Firmware not detected · Details" :
            !_ampsCatalog.IsComplete ? "Catalogue incomplete · Availability & sources" : "Catalogue status & sources";
        RenderAmpsDirectory(); RenderAmpDetail();
    }

    private void RenderAmpsDirectory()
    {
        if (_ampsList is null || _renderingAmps) { return; }
        var scroll = _ampsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = !_ampsDetailOpen ? scroll?.Offset ?? _ampsDirectoryOffset : _ampsDirectoryOffset;
        string manufacturer = _ampsManufacturer?.SelectedItem as string ?? "All manufacturers";
        var usage = SelectedAmpUsage;
        var families = _ampsCatalog.Families.Where(f => (manufacturer == "All manufacturers" || manufacturer == f.Family.Manufacturer) &&
            f.Matches(_ampsSearch?.Text ?? "") && (usage switch
            {
                AmpUsageFilter.Used => f.MatchingPresets > 0,
                AmpUsageFilter.Unused => f.UsageComplete && f.MatchingPresets == 0,
                _ => true,
            })).ToArray();
        _renderingAmps = true;
        _ampsList.Items.Clear();
        foreach (var family in families)
        {
            var content = new StackPanel();
            var group = families.Where(f => f.Family.Manufacturer == family.Family.Manufacturer).ToArray();
            if (ReferenceEquals(group[0], family))
            {
                var heading = new Grid { ColumnDefinitions = new("*,Auto") };
                heading.Children.Add(new TextBlock { Name = "AmpManufacturerHeading", Text = family.Family.Manufacturer, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush });
                var count = new TextBlock { Text = $"{group.Length} {(group.Length == 1 ? "family" : "families")}", FontSize = 12, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(count, 1); heading.Children.Add(count);
                content.Children.Add(new Border { Child = heading, Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new(1), Padding = new(12, 10), CornerRadius = new(5, 5, 0, 0) });
            }
            var row = new Grid { ColumnDefinitions = new("*,Auto") };
            row.Children.Add(new TextBlock { Name = "AmpFamilyName", Text = family.Family.Name, FontSize = 14, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var variantCount = new TextBlock { Text = $"{family.Variants.Length} {(family.Variants.Length == 1 ? "variant" : "variants")} ›", FontSize = 12, Foreground = SecondaryBrush, Margin = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(variantCount, 1); row.Children.Add(variantCount);
            content.Children.Add(new Border { Child = row, BorderBrush = UiBorderBrush, BorderThickness = new(1, 0, 1, 1), Padding = new(12, 9), CornerRadius = ReferenceEquals(group[^1], family) ? new(0, 0, 5, 5) : new(0) });
            var item = new ListBoxItem { Content = content, Tag = family, Background = SurfaceBrush, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            item.Tapped += (_, _) => OpenAmpFamily(family);
            AutomationProperties.SetName(item, $"{family.Family.Manufacturer} {family.Family.Name}, {family.Variants.Length} Fractal model variants, {family.UsageLabel}");
            ToolTip.SetTip(item, family.Family.Name + " · " + family.UsageLabel);
            _ampsList.Items.Add(item);
        }
        _ampsList.SelectedItem = _ampsList.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((BrowserAmpFamily)i.Tag!).Family.Id == _ampsSelectedFamily);
        foreach (var row in _ampsList.Items.OfType<ListBoxItem>()) { row.Background = row.IsSelected ? ThemeBrush("SelectedBrush") : SurfaceBrush; }
        _renderingAmps = false;
        _ampsCount!.Text = (_ampsCatalog.IsReferencePreview ? "Reference catalog · " : usage switch
        {
            AmpUsageFilter.Used => "Used in my presets · ",
            AmpUsageFilter.Unused => "Not used in my presets · ",
            _ => "All available amps · ",
        }) + $"{families.Length} families · {families.Sum(f => f.Variants.Length)} variants";
        _ampsEmpty!.IsVisible = families.Length == 0;
        _ampsEmpty.Text = _indexCache is null ? "Assign a device library in Config to browse amps." : _ampsCatalog.Families.Length == 0
            ? "No roster is verified for this device and firmware. Sync device in Preset Index to browse the observed models."
            : usage == AmpUsageFilter.Used ? "No matching amps were found in the saved preset index. Choose All amps to browse the catalogue."
            : usage == AmpUsageFilter.Unused ? "No unused amps match these filters. Clear search, choose All manufacturers, or choose All amps."
            : "No amps match these filters. Clear search or choose All manufacturers.";
        if (scroll is not null) { Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded); }
    }

    private void FilterAmps()
    {
        if (_renderingAmps) { return; }
        _ampsSelectedFamily = null; _ampsSelectedVariant = null; _ampsIncludedVariants = null; _ampsDirectoryOffset = default; _ampsDetailOpen = false;
        RenderAmpsDirectory(); RenderAmpDetail();
    }

    private void OpenAmpFamily(BrowserAmpFamily family)
    {
        _ampsDirectoryOffset = _ampsList!.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;
        if (_ampsSelectedFamily != family.Family.Id) { _ampsSelectedVariant = null; _ampsIncludedVariants = null; }
        _ampsSelectedFamily = family.Family.Id; _ampsDetailOpen = true;
        RenderAmpDetail();
    }

    private void RenderAmpDetail()
    {
        if (_ampsDetail is null) { return; }
        var family = _ampsDetailOpen ? _ampsCatalog.Families.FirstOrDefault(f => f.Family.Id == _ampsSelectedFamily) : null;
        _ampsDetailScroll!.IsVisible = family is not null;
        _ampsDirectory!.IsVisible = true;
        _ampsDetail.Children.Clear();
        if (family is null) { return; }
        var inspector = new Grid { ColumnDefinitions = new("220,16,*,16,210") };
        var identity = new StackPanel { Spacing = 5 };
        var models = new StackPanel { Spacing = 4 };
        var actions = new StackPanel { Spacing = 8 };
        Grid.SetColumn(models, 2); Grid.SetColumn(actions, 4);
        inspector.Children.Add(identity); inspector.Children.Add(models); inspector.Children.Add(actions);
        _ampsDetail.Children.Add(inspector);
        var back = IndexButton("Close", "AmpsBackToDirectory");
        back.Click += (_, _) =>
        {
            _ampsDetailOpen = false;
            RenderAmpDetail(); _ampsList!.Focus();
            Dispatcher.UIThread.Post(() => { var scroll = _ampsList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(); if (scroll is not null) { scroll.Offset = _ampsDirectoryOffset; } }, DispatcherPriority.Loaded);
        };
        identity.Children.Add(AmpText(family.Family.Manufacturer, 12));
        identity.Children.Add(new TextBlock { Text = family.Family.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
        identity.Children.Add(AmpText(family.UsageLabel, 12));
        models.Children.Add(AmpText("Fractal variants", 14));
        _ampsIncludedVariants ??= family.Variants.Select(v => v.Id).ToHashSet();
        var variantRows = new WrapPanel { Orientation = Orientation.Horizontal };
        models.Children.Add(variantRows);
        var find = IndexButton("Find presets", "AmpsFindPresets");
        find.IsEnabled = _ampsCatalog.HasUsageData && _ampsIncludedVariants.Count > 0;
        find.Click += (_, _) =>
        {
            if (_indexCache is null || !_ampsCatalog.HasUsageData) { return; }
            var variants = family.Variants.Where(v => _ampsIncludedVariants!.Contains(v.Id)).ToArray();
            if (variants.Length == 0) { return; }
            _ampContains = new(_indexCache.Device.Id, _indexCache.Device.Variant, _indexCache.Device.Firmware,
                variants.Length == 1 ? variants[0].Name : family.Family.Manufacturer + " " + family.Family.Name, variants.Select(v => v.ModelId).ToArray());
            _ampSceneMode = 0;
            if (_ampSceneChoice is not null) { _ampSceneChoice.SelectedIndex = 0; }
            _indexSearchChips.Clear(); if (_indexSearchBox is not null) { _indexSearchBox.Text = ""; }
            _indexSelectedSlot = null; RenderIndexSearchChips();
            _presetIndexNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        actions.Children.Add(find);
        actions.Children.Add(AmpText("Searches saved amp channels.", 12));
        ToolTip.SetTip(find, "Includes saved channels that no scene selects. Scene selection and bypass can be filtered in Preset Index.");
        actions.Children.Add(back);
        foreach (var variant in family.Variants)
        {
            var check = new CheckBox { Name = "AmpsVariant", Tag = variant.Id, Content = AmpText(variant.Name), IsChecked = _ampsIncludedVariants.Contains(variant.Id), MinHeight = 32, Width = 230, Margin = new(0, 0, 12, 0) };
            AutomationProperties.SetName(check, "Include " + variant.Name);
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true) { _ampsIncludedVariants.Add(variant.Id); }
                else { _ampsIncludedVariants.Remove(variant.Id); }
                _ampsSelectedVariant = _ampsIncludedVariants.Count == 1 ? _ampsIncludedVariants.Single() : null;
                find.IsEnabled = _ampsCatalog.HasUsageData && _ampsIncludedVariants.Count > 0;
            };
            variantRows.Children.Add(check);
        }
        var evidence = new StackPanel { Spacing = 6, Children = { AmpText("Mapping: " + family.Family.Confidence, 12), AmpText("Mapping source: " + family.Family.Source, 12) } };
        foreach (var variant in family.Variants)
        {
            evidence.Children.Add(AmpText(variant.Name + ": " + variant.IdentityEvidence, 11));
            evidence.Children.Add(AmpText(variant.MappingEvidence, 11));
            evidence.Children.Add(AmpText($"{variant.Name} · #{variant.ModelId} · {variant.SpecificModel} · {(variant.IdentityVerified ? "Device-verified name" : "Identity needs review")}", 12));
        }
        evidence.IsVisible = false;
        var showEvidence = IndexButton("Catalogue details & sources", "AmpsEvidence");
        showEvidence.FontSize = 12;
        showEvidence.Click += (_, _) => evidence.IsVisible = !evidence.IsVisible;
        _ampsDetail.Children.Add(evidence);
        var references = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var wiki = IndexButton("Wiki ↗", "AmpsWiki"); wiki.Click += (_, _) => OpenAmpReference(AmpBrowserCatalog.Wiki); references.Children.Add(wiki);
        var links = IndexButton("References", "AmpsReferences");
        links.Click += (_, _) =>
        {
            try
            {
                var saved = AmpReferences.Load().Where(r => r.CatalogId == family.Family.Id || family.Variants.Any(v => v.Id == r.CatalogId)).ToArray();
                var menu = new ContextMenu();
                foreach (var reference in saved.Prepend(new AmpReference(family.Family.Id, AmpBrowserCatalog.Wiki)).DistinctBy(r => r.Url))
                {
                    var item = new MenuItem { Header = reference.Url, MaxWidth = 700 };
                    item.Click += (_, _) => OpenAmpReference(reference.Url); menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                var add = new MenuItem { Header = "Add Wiki link…", Name = "AmpsAddWikiLink" };
                add.Click += async (_, _) => await AddAmpReferenceAsync(_ampsSelectedVariant ?? family.Family.Id);
                menu.Items.Add(add); links.ContextMenu = menu; menu.Open(links);
            }
            catch (Exception ex) { _ampsCoverage!.Text = "Could not read references: " + ex.Message; }
        };
        try { links.Content = $"References ({AmpReferences.Load().Where(r => r.CatalogId == family.Family.Id || family.Variants.Any(v => v.Id == r.CatalogId)).Select(r => r.Url).Append(AmpBrowserCatalog.Wiki).Distinct().Count()})"; }
        catch (Exception ex) { _ampsCoverage!.Text = "Could not read references: " + ex.Message; }
        references.Children.Add(links); identity.Children.Add(references);
        identity.Children.Add(showEvidence);
        void ArrangeInspector(double width)
        {
            bool narrow = width < 780;
            inspector.ColumnDefinitions = new(narrow ? "*" : "200,16,*,16,190");
            inspector.RowDefinitions = new(narrow ? "Auto,Auto,Auto" : "Auto");
            Grid.SetColumn(models, narrow ? 0 : 2); Grid.SetColumn(actions, narrow ? 0 : 4);
            Grid.SetRow(models, narrow ? 1 : 0); Grid.SetRow(actions, narrow ? 2 : 0);
        }
        inspector.SizeChanged += (_, e) => ArrangeInspector(e.NewSize.Width);
        ArrangeInspector(_ampsDetail.Bounds.Width);
    }

    private static TextBlock AmpText(string text, double size = 14) => new() { Text = text, FontSize = Math.Max(12, size), Foreground = size <= 12 ? SecondaryBrush : TextBrush, TextWrapping = TextWrapping.Wrap };

    private void OpenAmpReference(string url)
    {
        try { Process.Start(new ProcessStartInfo(AmpReferenceStore.NormalizeUrl(url)) { UseShellExecute = true }); }
        catch (Exception ex) { _ampsCoverage!.Text = "Could not open Wiki link: " + ex.Message; }
    }

    private async Task AddAmpReferenceAsync(string catalogId)
    {
        var input = new TextBox { Name = "AmpReferenceUrl", Watermark = "https://wiki.fractalaudio.com/wiki/index.php?title=…" };
        var error = AmpText("The link is personal reading material and does not change the amp mapping.", 12);
        var save = IndexButton("Add link", "AmpReferenceSave"); var cancel = IndexButton("Cancel");
        var dialog = new Window { Title = "Add Wiki link", Width = 540, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = SurfaceBrush, RequestedThemeVariant = RequestedThemeVariant };
        save.Click += (_, _) =>
        {
            try { AmpReferences.Merge([new(catalogId, input.Text ?? "")]); dialog.Close(); RenderAmpDetail(); }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        cancel.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); } };
        dialog.Content = new StackPanel { Margin = new(18), Spacing = 12, Children = { input, error, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } } } };
        await dialog.ShowDialog(this);
    }

    private ComboBox? _ampSceneChoice;
    private Control BuildAmpFilterRow()
    {
        _ampFilterRow = new StackPanel { Name = "IndexAmpFilter", Spacing = 4, IsVisible = false };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        var back = IndexButton("← Back to Amps", "IndexBackToAmps");
        back.Click += (_, _) => _ampsNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        _ampFilterLabel = new TextBlock { Name = "IndexAmpFilterLabel", Foreground = AccentBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        var clear = IndexButton("Clear amp filter", "IndexClearAmpFilter");
        clear.Click += (_, _) => { _ampContains = null; _ampSceneMode = 0; RenderIndexResults(); };
        _ampSceneChoice = new ComboBox { Name = "IndexAmpSceneUsage", ItemsSource = AmpSceneModes, SelectedIndex = _ampSceneMode, MinHeight = 28, FontSize = 12, Margin = new(8, 0) };
        AutomationProperties.SetName(_ampSceneChoice, "Narrow amp matches by saved scene state");
        _ampSceneChoice.SelectionChanged += (_, _) => { _ampSceneMode = _ampSceneChoice.SelectedIndex; RenderIndexResults(); };
        actions.Children.Add(back); actions.Children.Add(_ampFilterLabel); actions.Children.Add(clear); actions.Children.Add(_ampSceneChoice);
        _ampFilterRow.Children.Add(actions);
        _ampFilterRow.Children.Add(AmpText("Scene filters describe saved programmed settings. Effective live usage cannot be determined when Scene Ignore or unsaved edits apply.", 11));
        return _ampFilterRow;
    }

    private bool MatchesAmpScene(IndexedPreset preset) => _ampContains is null || _ampSceneMode == 0 ||
        Enumerable.Range(0, 8).Any(scene => _ampContains.ModelIds.Any(id => _ampSceneMode == 1 ? preset.Selects(scene, id) : preset.Uses(scene, id)));
}

internal sealed class AmpDirectoryPanel : Panel, INavigableContainer
{
    private readonly Dictionary<Control, Rect> _positions = [];
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 900;
        return LayoutGroups(width, measure: true);
    }
    private Size LayoutGroups(double width, bool measure)
    {
        int columns = Math.Max(1, (int)(width / 340));
        double columnWidth = width / columns;
        var heights = new double[columns];
        _positions.Clear();
        foreach (var group in Children.GroupBy(c => (c as ListBoxItem)?.Tag is BrowserAmpFamily f ? f.Family.Manufacturer : ""))
        {
            int column = Array.IndexOf(heights, heights.Min());
            foreach (var child in group)
            {
                if (measure) { child.Measure(new Size(Math.Max(0, columnWidth - 8), double.PositiveInfinity)); }
                _positions[child] = new Rect(column * columnWidth, heights[column], Math.Max(0, columnWidth - 8), child.DesiredSize.Height);
                heights[column] += child.DesiredSize.Height;
            }
            heights[column] += 14;
        }
        return new(width, heights.Max());
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        LayoutGroups(finalSize.Width, measure: false);
        foreach (var child in Children) { child.Arrange(_positions[child]); }
        return finalSize;
    }
    public IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        if (Children.Count == 0) { return null; }
        int index = from is Control control ? Children.IndexOf(control) : 0;
        if (direction is NavigationDirection.Left or NavigationDirection.Right && from is Control origin && _positions.TryGetValue(origin, out var bounds))
        {
            return _positions.Where(p => direction == NavigationDirection.Left ? p.Value.X < bounds.X : p.Value.X > bounds.X)
                .OrderBy(p => Math.Abs(p.Value.Y - bounds.Y) + Math.Abs(p.Value.X - bounds.X)).Select(p => p.Key).FirstOrDefault() ?? origin;
        }
        int delta = direction switch { NavigationDirection.Up or NavigationDirection.Previous => -1, NavigationDirection.Down or NavigationDirection.Next => 1, _ => 0 };
        return Children[direction switch { NavigationDirection.First => 0, NavigationDirection.Last => Children.Count - 1, _ => Math.Clamp(index + delta, 0, Children.Count - 1) }];
    }
}
#endif
