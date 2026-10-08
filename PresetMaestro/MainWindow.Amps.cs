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
using PresetMaestro.Core;
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
    private StackPanel? _ampsFirmwareNotice;
    private TextBlock? _ampsUsageNotice;
    private Grid? _ampsDirectory;
    private IndexDockLayout? _ampsResultsPane;
    private Border? _ampsDetailFrame;
    private StackPanel? _ampsDetailHeader;
    private ScrollViewer? _ampsDetailScroll;
    private StackPanel? _ampsDetail;
    private string? _ampsSelectedFamily;
    private HashSet<string>? _ampsIncludedVariants;
    private Guid? _ampsDeviceId;
    private string? _ampsFirmware;
    private bool _renderingAmps;
    private bool _ampsDetailOpen;
    private bool _ampsRevealQueued;
    private Vector _ampsDirectoryOffset;
    private string? _ampReferenceError;
    private AmpBrowserDirectory _ampsCatalog = new("", "", false, []);
    private AmpContainsFilter? _ampContains;
    private StackPanel? _ampFilterRow;
    private bool _openingAmpPresets;
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
        var openConfig = IndexButton("Open Config", "AmpsOpenConfig");
        openConfig.Click += (_, _) => this.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "NavConfig").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        _ampsFirmwareNotice.Children.Add(openConfig);
        Grid.SetRow(_ampsFirmwareNotice, 3); body.Children.Add(_ampsFirmwareNotice);
        var stage = _ampsResultsPane = new IndexDockLayout { Name = "AmpsResultsPane", RowDefinitions = new("*,0,0") };
        _ampsDirectory = new Grid { RowDefinitions = new("28,*") };
        var tableHeaders = new Grid { Name = "AmpsTableHeaders", Height = 28, HorizontalAlignment = HorizontalAlignment.Left };
        _ampsDirectory.Children.Add(tableHeaders);
        _ampsList = new ListBox { Name = "AmpsDirectory", Background = SurfaceBrush, BorderThickness = new(0), ItemContainerTheme = CompactListItemTheme() };
        AutomationProperties.SetName(_ampsList, "Amp directory");
        _ampsList.ItemsPanel = new FuncTemplate<Panel?>(() => new AmpDirectoryPanel
        {
            LayoutChanged = (columns, width) => Dispatcher.UIThread.Post(() =>
            {
                tableHeaders.Width = width;
                if (tableHeaders.Children.Count == columns) { return; }
                tableHeaders.Children.Clear(); tableHeaders.ColumnDefinitions.Clear();
                for (int column = 0; column < columns; column++)
                {
                    tableHeaders.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                    tableHeaders.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));
                    var heading = new Grid { ColumnDefinitions = new("*,56"), Background = InsetBrush };
                    heading.Children.Add(new TextBlock { Text = "Amp family", FontSize = 11, Foreground = SecondaryBrush, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center });
                    var variants = new TextBlock { Text = "Variants", FontSize = 11, Foreground = SecondaryBrush, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                    Grid.SetColumn(variants, 1); heading.Children.Add(variants);
                    Grid.SetColumn(heading, column * 2); tableHeaders.Children.Add(heading);
                }
            }, DispatcherPriority.Loaded),
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_ampsList, ScrollBarVisibility.Disabled);
        _ampsList.SelectionChanged += (_, _) =>
        {
            if (!_renderingAmps && _ampsList.SelectedItem is ListBoxItem { Tag: BrowserAmpFamily family })
            {
                if (_ampsSelectedFamily != family.Family.Id) { _ampsIncludedVariants = null; }
                _ampsSelectedFamily = family.Family.Id;
                foreach (var row in _ampsList.Items.OfType<ListBoxItem>()) { row.Background = row.IsSelected ? ThemeBrush("SelectedBrush") : SurfaceBrush; }
                if (_ampsDetailOpen) { RenderAmpDetail(); RevealAmpSelection(); }
            }
        };
        _ampsList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _ampsList.SelectedItem is ListBoxItem { Tag: BrowserAmpFamily family })
            { OpenAmpFamily(family); e.Handled = true; }
        };
        Grid.SetRow(_ampsList, 1); _ampsDirectory.Children.Add(_ampsList);
        _ampsEmpty = new TextBlock { Name = "AmpsEmpty", Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap, MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(_ampsEmpty, 1); _ampsDirectory.Children.Add(_ampsEmpty); stage.Children.Add(_ampsDirectory);
        _ampsDetail = new StackPanel { Name = "AmpsDetail", Spacing = 8 };
        _ampsDetailHeader = new StackPanel { Name = "AmpsDetailHeader", Spacing = 8 };
        var detailHeader = new Border { Child = _ampsDetailHeader, BorderBrush = UiBorderBrush, BorderThickness = new(0, 0, 0, 1), Padding = new(12, 8) };
        _ampsDetailScroll = new ScrollViewer { Name = "AmpsDetailScroll", Content = _ampsDetail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(12, 8) };
        var detailLayout = new Grid { RowDefinitions = new("Auto,*") };
        detailLayout.Children.Add(detailHeader);
        Grid.SetRow(_ampsDetailScroll, 1); detailLayout.Children.Add(_ampsDetailScroll);
        _ampsDetailFrame = new Border { Name = "AmpsDetailFrame", Child = detailLayout, Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new(1), CornerRadius = new(6), ClipToBounds = true, IsVisible = false };
        Grid.SetRow(_ampsDetailFrame, 2); stage.Children.Add(_ampsDetailFrame);
        stage.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _ampsDetailOpen) { CloseAmpDetail(); e.Handled = true; }
        };
        _ampsList.SizeChanged += (_, _) => RevealAmpSelection();
        var frame = new Border { Child = stage, Background = SurfaceBrush, ClipToBounds = true };
        Grid.SetRow(frame, 4); body.Children.Add(frame);
        _ampsCoverage = new TextBlock { Name = "AmpsCoverage", Foreground = SecondaryBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
        _ampsCoverage.IsVisible = false;
        Grid.SetRow(_ampsCoverage, 5); body.Children.Add(_ampsCoverage);
        var page = new Border { Name = "AmpsPage", Margin = new(20, 12, 20, 20), Padding = new(16, 11, 12, 12), Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new(1), CornerRadius = new(9), Child = body };
        RefreshAmpsContext();
        return page;
    }

    private void RefreshAmpsContext()
    {
        if (_ampsList is null) { return; }
        bool changed = _ampsDeviceId != _indexCache?.Device.Id || !AmpBrowserCatalog.SameFirmware(_ampsFirmware, _indexCache?.Device.Firmware);
        _ampsDeviceId = _indexCache?.Device.Id; _ampsFirmware = _indexCache?.Device.Firmware;
        if (changed) { _ampsSelectedFamily = null; _ampsIncludedVariants = null; _ampsDetailOpen = false; _ampContains = null; }
        _ampsCatalog = AmpBrowserCatalog.Build(_indexCache);
        string selected = _ampsManufacturer?.SelectedItem as string ?? "All manufacturers";
        _renderingAmps = true;
        var manufacturers = AllAmpManufacturers.Concat(_ampsCatalog.Families.Select(f => f.Family.Manufacturer).Distinct()).ToArray();
        _ampsManufacturer!.ItemsSource = manufacturers;
        _ampsManufacturer.SelectedItem = manufacturers.Contains(selected) ? selected : manufacturers[0];
        _ampsFirmwareNotice!.IsVisible = _indexCache is not null && !_ampsCatalog.HasUsageData;
        _ampsUsageNotice!.Text = _ampsCatalog.IsReferencePreview
            ? "Firmware has not been detected. Browse the FM9 reference catalogue below. Connect in Config, then open Sync options and choose Sync device to read amp usage."
            : _indexCache?.Browsable is null
                ? "Amp usage has not been synced. Preset-name sync updates names only. Open Sync options in Config and choose Sync device to read amp data."
                : string.IsNullOrWhiteSpace(_indexCache.Browsable.EffectiveFirmware) && !_indexCache.Imported
                    ? "Check the connected device against its saved data to show which amps your saved presets use. Open Sync options in Config and choose Quick check or Full check."
                    : "Your device’s software has changed since this amp information was saved. Open Sync options in Config and choose Sync device to refresh the saved amp data.";
        var usageOptions = _ampsUsage!.Items.OfType<ComboBoxItem>().ToArray();
        usageOptions[1].IsEnabled = _ampsCatalog.HasUsageData;
        usageOptions[2].IsEnabled = _ampsCatalog.CanListUnusedAmps;
        ToolTip.SetTip(_ampsUsage, "Usage includes every saved amp channel, even if no scene selects it. Not used shows families with no variants in your presets.");
        if (_ampsUsage.SelectedItem is ComboBoxItem { IsEnabled: false }) { _ampsUsage.SelectedIndex = 0; }
        _ampsUnusedNotice!.IsVisible = _ampsCatalog.HasUsageData && !_ampsCatalog.CanListUnusedAmps;
        _ampsUnusedNotice.Text = !_ampsCatalog.HasCatalogueRoster && !_ampsCatalog.IsComplete
            ? "The app does not yet include an amp catalogue for this device’s software, so it cannot list unused amps."
            : "To find amps not used in any preset, complete a device sync through Sync options in Config.";
        _renderingAmps = false;
        // Catalogue coverage, identity evidence and mapping sources remain in the
        // catalogue/cache for a future Config view. This footer only shows errors.
        _ampsCoverage!.Text = _ampReferenceError ?? "";
        _ampsCoverage.IsVisible = _ampReferenceError is not null;
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
        var groups = families.GroupBy(f => f.Family.Manufacturer).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var family in families)
        {
            var content = new StackPanel();
            var group = groups[family.Family.Manufacturer];
            if (ReferenceEquals(group[0], family))
            {
                var heading = new Grid { ColumnDefinitions = new("*,Auto") };
                heading.Children.Add(new TextBlock { Name = "AmpManufacturerHeading", Text = family.Family.Manufacturer, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextBrush });
                var count = new TextBlock { Text = $"{group.Length} {(group.Length == 1 ? "family" : "families")}", FontSize = 12, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(count, 1); heading.Children.Add(count);
                content.Children.Add(new Border { Child = heading, MinHeight = 28, Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new(0, 0, 0, 1), Padding = new(6, 4) });
            }
            var row = new Grid { Name = "AmpFamilyRow", ColumnDefinitions = new("*,56"), MinHeight = 32 };
            row.Children.Add(new TextBlock { Name = "AmpFamilyName", Text = family.Family.Name, FontSize = 13, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(6, 2), VerticalAlignment = VerticalAlignment.Center });
            var variantCount = new TextBlock { Name = "AmpFamilyVariantCount", Text = $"{family.Variants.Length} ›", FontSize = 12, Foreground = SecondaryBrush, Margin = new(6, 2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(variantCount, 1); row.Children.Add(variantCount);
            string fractalNames = "Fractal: " + string.Join(" · ", family.Variants.Select(v => v.Name));
            content.Children.Add(new Border { Child = row, BorderBrush = ThemeBrush("RowSeparatorBrush"), BorderThickness = new(0, 0, 0, 1) });
            var item = new ListBoxItem { Content = content, Tag = family, Background = SurfaceBrush, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            item.Tapped += (_, _) => OpenAmpFamily(family);
            AutomationProperties.SetName(item, $"{family.Family.Manufacturer} {family.Family.Name}. {fractalNames}. {family.UsageLabel}");
            ToolTip.SetTip(item, family.Family.Name + "\n" + fractalNames + "\n" + family.UsageLabel);
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
        _ampsEmpty.Text = _indexCache is null ? "Assign a device in Config to browse amps." : _ampsCatalog.Families.Length == 0
            ? "No roster is verified for this device and firmware. Open Sync options in Config and choose Sync device to browse the observed models."
            : usage == AmpUsageFilter.Used ? "No matching amps were found in the saved preset index. Choose All amps to browse the catalogue."
            : usage == AmpUsageFilter.Unused ? "No unused amps match these filters. Clear search, choose All manufacturers, or choose All amps."
            : "No amps match these filters. Clear search or choose All manufacturers.";
        if (scroll is not null) { Dispatcher.UIThread.Post(() => scroll.Offset = offset, DispatcherPriority.Loaded); }
    }

    private void FilterAmps()
    {
        if (_renderingAmps) { return; }
        _ampsSelectedFamily = null; _ampsIncludedVariants = null; _ampsDirectoryOffset = default; _ampsDetailOpen = false;
        RenderAmpsDirectory(); RenderAmpDetail();
    }

    private void OpenAmpFamily(BrowserAmpFamily family)
    {
        _ampsDirectoryOffset = _ampsList!.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;
        if (_ampsSelectedFamily != family.Family.Id) { _ampsIncludedVariants = null; }
        _ampsSelectedFamily = family.Family.Id; _ampsDetailOpen = true;
        RenderAmpDetail(); RevealAmpSelection();
    }

    private void RevealAmpSelection()
    {
        if (!_ampsDetailOpen || _ampsRevealQueued) { return; }
        _ampsRevealQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _ampsRevealQueued = false;
            if (_ampsDetailOpen && _ampsList?.SelectedIndex >= 0) { _ampsList.ScrollIntoView(_ampsList.SelectedIndex); }
        }, DispatcherPriority.Loaded);
    }

    private void RenderAmpDetail()
    {
        if (_ampsDetail is null) { return; }
        var family = _ampsDetailOpen ? _ampsCatalog.Families.FirstOrDefault(f => f.Family.Id == _ampsSelectedFamily) : null;
        _ampsDetailFrame!.IsVisible = family is not null;
        _ampsResultsPane!.DockOpen = family is not null;
        _ampsDirectory!.IsVisible = true;
        _ampsDetail.Children.Clear();
        _ampsDetailHeader!.Children.Clear();
        _ampsDetailScroll!.Offset = default;
        if (family is null) { return; }
        var heading = new Grid { ColumnDefinitions = new("*,16,Auto") };
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(new TextBlock { Name = "AmpsDetailTitle", Text = family.Family.Manufacturer + " · " + family.Family.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
        identity.Children.Add(AmpText($"{family.Variants.Length} amp {(family.Variants.Length == 1 ? "model" : "models")} · {family.UsageLabel}", 12));
        heading.Children.Add(identity);
        var close = IndexButton("Close details", "AmpsBackToDirectory");
        close.MinHeight = 28; close.FontSize = 12; close.Padding = new(8, 2);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new PathIcon { Data = Geometry.Parse("M1,0 L6,5 L11,0 L12,1 L7,6 L12,11 L11,12 L6,7 L1,12 L0,11 L5,6 L0,1 Z"), Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "Close details", VerticalAlignment = VerticalAlignment.Center },
            },
        };
        AutomationProperties.SetName(close, "Close details");
        ToolTip.SetTip(close, "Close details (Esc)");
        close.Click += (_, _) => CloseAmpDetail();
        Grid.SetColumn(close, 2);
        _ampsIncludedVariants ??= family.Variants.Select(v => v.Id).ToHashSet();
        var find = IndexButton("", "AmpsFindPresets");
        find.MinHeight = 28; find.FontSize = 12; find.Padding = new(8, 2);
        find.HorizontalAlignment = HorizontalAlignment.Right;
        find.VerticalAlignment = VerticalAlignment.Center;
        find.Background = AccentBrush; find.Foreground = Brushes.White;
        UpdateSearchButton();
        find.Click += (_, _) =>
        {
            if (_indexCache is null || !_ampsCatalog.HasUsageData) { return; }
            var variants = family.Variants.Where(v => _ampsIncludedVariants!.Contains(v.Id)).ToArray();
            if (variants.Length == 0) { return; }
            _ampContains = new(_indexCache.Device.Id, _indexCache.Device.Variant, _indexCache.Device.Firmware,
                variants.Length == 1 ? variants[0].Name : family.Family.Manufacturer + " " + family.Family.Name, variants.Select(v => v.ModelId).ToArray());
            _indexSelectedSlot = null;
            _openingAmpPresets = true;
            try
            {
                _presetIndexNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                _indexList?.Focus();
            }
            finally { _openingAmpPresets = false; }
        };
        heading.Children.Add(close);
        _ampsDetailHeader.Children.Add(heading);
        var searchHeading = new StackPanel { Spacing = 2 };
        searchHeading.Children.Add(new TextBlock { Text = "Find presets by amp model", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = TextBrush });
        searchHeading.Children.Add(AmpText("Choose models to match in your saved presets.", 12));
        var searchRow = new Grid { Name = "AmpsModelSearchToolbar", ColumnDefinitions = new("*,12,Auto") };
        searchRow.Children.Add(searchHeading);
        Grid.SetColumn(find, 2); searchRow.Children.Add(find);
        _ampsDetailHeader.Children.Add(searchRow);
        var modelHeadings = new Grid { Name = "AmpsModelTableHeaders", Height = 28, ColumnDefinitions = new("124,*,64"), Background = InsetBrush };
        AddModelHeading("Use in search", 0);
        AddModelHeading("Amp model name on your Fractal", 1);
        AddModelHeading("Info", 2);
        var variantRows = new Grid { Name = "AmpsVariantRows" };
        _ampsDetail.Children.Add(new StackPanel { Name = "AmpsVariantTables", Children = { modelHeadings, variantRows } });
        var displayedWikiUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in family.Variants)
        {
            string realAmp = family.Family.Confidence == "unmapped" ? "Real amp not mapped" : variant.SpecificModel;
            var label = new TextBlock { Name = "AmpsVariantFractalModel", Text = variant.Name, FontSize = 13, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var check = new CheckBox { Name = "AmpsVariant", Tag = variant.Id, IsChecked = _ampsIncludedVariants.Contains(variant.Id), MinHeight = 28, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(check, $"Use {variant.Name} in preset search. Real amp: {realAmp}");
            ToolTip.SetTip(check, $"Search presets for {variant.Name}\n{realAmp}");
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true) { _ampsIncludedVariants.Add(variant.Id); }
                else { _ampsIncludedVariants.Remove(variant.Id); }
                UpdateSearchButton();
            };
            var row = new Grid { Name = "AmpsVariantRow", ColumnDefinitions = new("124,*,64"), MinHeight = 32 };
            row.Children.Add(check);
            var modelCell = new Border { Name = "AmpsVariantModelLabel", Tag = variant.Id, Child = label, Padding = new(8, 4), Background = Brushes.Transparent };
            ToolTip.SetTip(modelCell, $"Name on your Fractal: {variant.Name}\n{realAmp}");
            modelCell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(modelCell).Properties.IsLeftButtonPressed) { return; }
                check.Focus(); check.IsChecked = check.IsChecked != true; e.Handled = true;
            };
            Grid.SetColumn(modelCell, 1); row.Children.Add(modelCell);
            if (variant.WikiUrl is not null && WikiLink("Wiki", variant.Name, new(variant.Id, variant.WikiUrl)) is { } link)
            { link.HorizontalAlignment = HorizontalAlignment.Right; link.Margin = new(0, 0, 8, 0); Grid.SetColumn(link, 2); row.Children.Add(link); }
            var separator = new Border { BorderBrush = ThemeBrush("RowSeparatorBrush"), BorderThickness = new(0, 0, 0, 1), IsHitTestVisible = false };
            Grid.SetColumnSpan(separator, 3); row.Children.Add(separator);
            variantRows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(row, variantRows.Children.Count); variantRows.Children.Add(row);
        }
        var additionalReferences = new WrapPanel { Name = "AmpsWikiLinks", Orientation = Orientation.Horizontal };
        if (!family.Variants.Any(v => v.WikiUrl is not null)) { AddReference("Amp models", new(family.Family.Id, AmpBrowserCatalog.Wiki)); }
        try
        {
            foreach (var reference in AmpReferences.Load().Where(r => r.CatalogId == family.Family.Id || family.Variants.Any(v => v.Id == r.CatalogId)))
            { AddReference(AmpReferenceTitle(reference.Url), reference); }
        }
        catch (Exception ex) { ShowAmpReferenceError("Could not read Wiki links: " + ex.Message); }
        if (additionalReferences.Children.Count > 0) { _ampsDetail.Children.Add(additionalReferences); }

        void UpdateSearchButton()
        {
            var variants = family.Variants.Where(v => _ampsIncludedVariants!.Contains(v.Id)).ToArray();
            string text = $"Show presets for {variants.Length} {(variants.Length == 1 ? "model" : "models")}";
            find.Content = text;
            find.IsEnabled = _ampsCatalog.HasUsageData && variants.Length > 0;
            AutomationProperties.SetName(find, text + (variants.Length == 0 ? ". Choose an amp model first." : ": " + string.Join(" or ", variants.Select(v => v.Name))));
            ToolTip.SetTip(find, !_ampsCatalog.HasUsageData ? "Sync the device to search its saved presets."
                : variants.Length == 0 ? "Choose at least one amp model to search."
                : "Show saved presets containing any checked model in their saved Amp block channels.");
        }
        void AddModelHeading(string text, int column)
        {
            var label = new TextBlock { Text = text, FontSize = 12, Foreground = SecondaryBrush, Margin = new(8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (column == 2) { label.HorizontalAlignment = HorizontalAlignment.Right; }
            Grid.SetColumn(label, column); modelHeadings.Children.Add(label);
        }
        void AddReference(string title, AmpReference reference)
        {
            if (WikiLink(title, title, reference) is not { } link) { return; }
            link.Margin = new(6, 0, 12, 0); additionalReferences.Children.Add(link);
        }
        HyperlinkButton? WikiLink(string text, string title, AmpReference reference)
        {
            var uri = new Uri(AmpReferenceStore.NormalizeUrl(reference.Url));
            if (!displayedWikiUrls.Add(uri.GetLeftPart(UriPartial.Query) + Uri.UnescapeDataString(uri.Fragment))) { return null; }
            var link = new HyperlinkButton
            {
                Name = "AmpsWikiLink",
                Tag = reference,
                Padding = new(0),
                MinHeight = 28,
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = text + " ↗", Foreground = AccentBrush, FontSize = 12, TextDecorations = TextDecorations.Underline, TextWrapping = TextWrapping.Wrap },
            };
            AutomationProperties.SetName(link, "Read " + title + " on the Fractal Audio Wiki");
            ToolTip.SetTip(link, reference.Url);
            link.Click += (_, _) => OpenAmpReference(reference.Url);
            return link;
        }
    }

    private void CloseAmpDetail()
    {
        var source = _ampsList?.SelectedItem as ListBoxItem;
        _ampsDetailOpen = false;
        RenderAmpDetail();
        if (source?.Focus() != true) { _ampsList?.Focus(); }
        Dispatcher.UIThread.Post(() =>
        {
            var scroll = _ampsList?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null) { scroll.Offset = _ampsDirectoryOffset; }
        }, DispatcherPriority.Loaded);
    }

    private static TextBlock AmpText(string text, double size = 14) => new() { Text = text, FontSize = Math.Max(12, size), Foreground = size <= 12 ? SecondaryBrush : TextBrush, TextWrapping = TextWrapping.Wrap };

    private static string AmpReferenceTitle(string url)
    {
        var uri = new Uri(url);
        if (uri.Fragment.Length > 1) { return Uri.UnescapeDataString(uri.Fragment[1..]).Replace('_', ' '); }
        var title = uri.Query.TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("title=", StringComparison.Ordinal));
        return title is null ? "Additional Wiki page" : Uri.UnescapeDataString(title[6..]).Replace('_', ' ');
    }

    private void ShowAmpReferenceError(string message)
    {
        _ampsCoverage!.Text = message;
        _ampsCoverage.IsVisible = true;
    }

    private void OpenAmpReference(string url)
    {
        try { Process.Start(new ProcessStartInfo(AmpReferenceStore.NormalizeUrl(url)) { UseShellExecute = true }); }
        catch (Exception ex) { ShowAmpReferenceError("Could not open Wiki link: " + ex.Message); }
    }

    private Control BuildAmpDetailsHeading()
    {
        _ampFilterRow = new StackPanel { Name = "IndexAmpDetailsHeading", Spacing = 12, IsVisible = false };
        var back = IndexButton("← Back to Amps", "IndexBackToAmps");
        back.Click += (_, _) =>
        {
            _ampsNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            _ampsPage?.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "AmpsFindPresets")?.Focus();
        };
        _ampFilterRow.Children.Add(back);
        _ampFilterRow.Children.Add(AmpText("Presets containing this amp", 14));
        return _ampFilterRow;
    }
}

internal sealed class AmpDirectoryPanel : Panel, INavigableContainer
{
    private readonly Dictionary<Control, Rect> _positions = [];
    private int _columns;
    private double _width;
    internal Action<int, double>? LayoutChanged { get; init; }
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
        int columns = Math.Max(1, (int)(finalSize.Width / 340));
        if (_columns != columns || _width != finalSize.Width)
        { _columns = columns; _width = finalSize.Width; LayoutChanged?.Invoke(columns, finalSize.Width); }
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
