using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private const string AllCategoriesSentinel = "All Favorites";

    private void AttachFavoritePresetPicker()
    {
        if (_favPresetSpinner.Parent is not StackPanel field)
        {
            return;
        }

        field.Children.Remove(_favPresetSpinner);

        _favPresetDisplayLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var pickerContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        pickerContent.Children.Add(_favPresetDisplayLabel);
        var chevron = new PathIcon
        {
            Data = Geometry.Parse("M5,1 L10,6 L5,11 Z"),
            Width = 10,
            Height = 12,
            Foreground = SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chevron, 1);
        pickerContent.Children.Add(chevron);
        _favPresetPickerButton = new Button
        {
            Name = "FavoritePresetPicker",
            MinHeight = 40,
            Padding = new Thickness(12, 0),
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = pickerContent,
        };
        AutomationProperties.SetName(_favPresetPickerButton, "Select preset");
        _favPresetPickerButton.Click += async (_, _) => await SelectFavoritePresetAsync();
        field.Children.Add(_favPresetPickerButton);

        _favPresetSpinner.ValueChanged += (_, _) => UpdateFavoritePresetDisplay();
        UpdateFavoritePresetDisplay();
    }

    private void UpdateFavoritePresetDisplay()
    {
        if (_favPresetDisplayLabel == null)
        {
            return;
        }

        if (_favPresetSpinner.Value is not decimal value)
        {
            _favPresetDisplayLabel.Text = string.Empty;
            return;
        }

        int displayed = (int)value;
        int slot = displayed - _settings.DisplayOffset;
        string name = slot is >= 0 and <= 511 &&
                      _settings.PresetNameCache.TryGetValue(slot, out string? cachedName) &&
                      !string.IsNullOrWhiteSpace(cachedName)
            ? cachedName.Trim()
            : "(name unavailable)";
        _favPresetDisplayLabel.Text = $"{displayed:000} · {name}";
    }

    private async Task SelectFavoritePresetAsync()
    {
        int offset = _settings.DisplayOffset;
        int currentSlot = Math.Clamp((int)(_favPresetSpinner.Value ?? offset) - offset, 0, 511);
        var dialog = new PresetSelectionWindow(_settings.PresetNameCache, currentSlot, _settings.DisplayOffset) { Icon = Icon };
        int? slot = _presetPickerOverride is not null
            ? await _presetPickerOverride(dialog)
            : await dialog.ShowDialog<int?>(this);
        if (slot.HasValue && _favEditingId != null)
        {
            _favPresetSpinner.Value = slot.Value + offset;
            PopulateFavoriteScenes();
            await ReadFavoriteScenesAsync();
        }
    }

    private Button _favTagFilter = null!;
    private TextBlock _favTagFilterLabel = null!;
    private Popup _favTagFilterPopup = null!;
    private Border _favTagFilterPopupBorder = null!;
    private StackPanel _favTagOptionsPanel = null!;
    private ToggleButton _favTagMatchAllButton = null!;
    private ToggleButton _favTagMatchAnyButton = null!;
    private TextBlock _favEmptyResults = null!;
    private readonly HashSet<string> _favSelectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _favAvailableTags = [];
    private bool _favTagMatchAll = true;
    private bool _refreshingTagOptions;
    private int? _favFilterSelectionId;
    private bool _refreshingFavorites;
    private double? _favPreviousMinWidth;
    private void RefreshFavoriteTagOptions()
    {
        var tags = _favorites.SelectMany(f => f.Tags).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        var retainedSelections = tags.Where(tag => _favSelectedTags.Contains(tag)).ToList();
        _favSelectedTags.Clear();
        _favSelectedTags.UnionWith(retainedSelections);

        _refreshingTagOptions = true;
        try
        {
            bool optionsUnchanged = _favAvailableTags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase) &&
                                    _favTagOptionsPanel.Children.Count == tags.Count + 1;
            if (optionsUnchanged)
            {
                foreach (var option in _favTagOptionsPanel.Children.OfType<CheckBox>())
                {
                    option.IsChecked = option.Tag is not string tag
                        ? _favSelectedTags.Count == 0
                        : _favSelectedTags.Contains(tag);
                }

                return;
            }

            _favAvailableTags.Clear();
            _favAvailableTags.AddRange(tags);
            _favTagOptionsPanel.Children.Clear();
            var allTags = new CheckBox
            {
                Name = "FavoriteTagAllTags",
                Content = "All tags",
                IsChecked = _favSelectedTags.Count == 0,
                MinHeight = 30,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(allTags, "Show favorites with all tags");
            allTags.Click += (_, _) => ClearFavoriteTagFilter();
            _favTagOptionsPanel.Children.Add(allTags);

            for (int index = 0; index < tags.Count; index++)
            {
                string tag = tags[index];
                var option = new CheckBox
                {
                    Name = $"FavoriteTagOption{index}",
                    Content = tag,
                    Tag = tag,
                    IsChecked = _favSelectedTags.Contains(tag),
                    MinHeight = 30,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                AutomationProperties.SetName(option, $"Filter favorites by tag {tag}");
                option.Click += OnFavoriteTagOptionClicked;
                _favTagOptionsPanel.Children.Add(option);
            }
        }
        finally
        {
            _refreshingTagOptions = false;
            UpdateFavoriteTagFilterPresentation();
        }
    }

    private void OnFavoriteTagOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (_refreshingTagOptions || sender is not CheckBox { Tag: string tag } option)
        {
            return;
        }

        if (option.IsChecked == true)
        {
            _favSelectedTags.Add(tag);
        }
        else
        {
            _favSelectedTags.Remove(tag);
        }

        RefreshFavoritesList(refreshCategories: false);
    }

    private void ClearFavoriteTagFilter()
    {
        if (_refreshingTagOptions)
        {
            return;
        }

        _favSelectedTags.Clear();
        RefreshFavoritesList(refreshCategories: false);
    }

    private void SetFavoriteTagMatchMode(bool matchAll)
    {
        if (_favTagMatchAll == matchAll)
        {
            UpdateFavoriteTagFilterPresentation();
            return;
        }

        _favTagMatchAll = matchAll;
        UpdateFavoriteTagFilterPresentation();
        RefreshFavoritesList(refreshCategories: false);
    }

    private void UpdateFavoriteTagFilterPresentation()
    {
        if (_favTagFilterLabel is null)
        {
            return;
        }

        _favTagFilterLabel.Text = _favSelectedTags.Count switch
        {
            0 => "Tags: All tags",
            1 => $"Tags: {_favSelectedTags.Single()}",
            _ => $"Tags: {_favSelectedTags.Count} selected",
        };

        _favTagMatchAllButton.IsChecked = _favTagMatchAll;
        _favTagMatchAnyButton.IsChecked = !_favTagMatchAll;
        _favTagMatchAllButton.Background = _favTagMatchAll ? AccentBrush : Brushes.Transparent;
        _favTagMatchAllButton.Foreground = _favTagMatchAll ? Brushes.White : TextBrush;
        _favTagMatchAnyButton.Background = _favTagMatchAll ? Brushes.Transparent : AccentBrush;
        _favTagMatchAnyButton.Foreground = _favTagMatchAll ? TextBrush : Brushes.White;
    }

    private void OnFavoriteTagPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _favTagFilterPopup.IsOpen = false;
            _favTagFilter.Focus();
            e.Handled = true;
        }
    }

    private string _favSelectedCategory = AllCategoriesSentinel;
    private int? _favEditingId; // null = editor hidden, 0 = creating new, >0 = editing that Favorite.Id
    private int _favSortColumn = -1;
    private bool _favSortAscending = true;
    private Favorite? _favDragSource;
    private Point? _favDragStart;
    private ListBoxItem? _favDragItem;
    private ListBoxItem? _favInsertionItem;
    private readonly Dictionary<ListBoxItem, Border> _favInsertionLines = [];
    private readonly Dictionary<ListBoxItem, Border> _favDetailStrips = [];
    private readonly Dictionary<ListBoxItem, TextBlock> _favDetailTexts = [];
    private readonly Dictionary<ListBoxItem, Grid> _favMainRows = [];
    private readonly Dictionary<ListBoxItem, Border> _favSelectionAccents = [];
    private bool _favInsertAfter;
    private bool _favIsDragging;
    private string? _favCategoryDragSource;
    private Point? _favCategoryDragStart;
    private ListBoxItem? _favCategoryDragItem;
    private ListBoxItem? _favCategoryInsertionItem;
    private readonly Dictionary<ListBoxItem, Border> _favCategoryInsertionLines = [];
    private bool _favCategoryInsertAfter;
    private bool _favCategoryIsDragging;
    private readonly List<string> _favEditingTags = [];
    private Border _favTagsEditor = null!;
    private WrapPanel _favTagsPanel = null!;
    private TextBox _favTagInput = null!;

    private IEnumerable<Favorite> GetFilteredFavorites()
    {
        string search = _favSearchBox.Text?.Trim() ?? string.Empty;

        IEnumerable<Favorite> result = _favorites.Where(f =>
            (_favSelectedCategory == AllCategoriesSentinel || string.Equals(f.Category, _favSelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (_favSelectedTags.Count == 0 || (_favTagMatchAll
                ? _favSelectedTags.All(selected => f.Tags.Contains(selected, StringComparer.OrdinalIgnoreCase))
                : _favSelectedTags.Any(selected => f.Tags.Contains(selected, StringComparer.OrdinalIgnoreCase)))) &&
            (search.Length == 0 ||
             f.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             f.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             f.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase))));

        if (_favSortColumn < 0)
        {
            return result;
        }

        Func<Favorite, IComparable> key = _favSortColumn switch
        {
            0 => f => f.Slot,
            1 => f => f.Name,
            2 => f => f.Category,
            3 => f => f.TagsDisplay,
            4 => f => f.Preset,
            5 => f => f.Scene,
            _ => f => f.Slot,
        };
        return _favSortAscending ? result.OrderBy(key) : result.OrderByDescending(key);
    }

    private void RefreshFavoriteCategoryTree()
    {
        var categories = GetOrderedCategories();
        if (_favSelectedCategory != AllCategoriesSentinel &&
            !categories.Any(category => string.Equals(category, _favSelectedCategory, StringComparison.OrdinalIgnoreCase)))
        {
            _favSelectedCategory = AllCategoriesSentinel;
        }

        _favCategoryTree.SelectionChanged -= OnFavCategorySelectionChanged;
        _favCategoryTree.Items.Clear();
        _favCategoryTree.Items.Add(BuildCategoryRow(AllCategoriesSentinel, _favorites.Count));
        foreach (var cat in categories)
        {
            _favCategoryTree.Items.Add(BuildCategoryRow(cat, _favorites.Count(f => string.Equals(f.Category, cat, StringComparison.OrdinalIgnoreCase))));
        }

        int wantIndex = _favSelectedCategory == AllCategoriesSentinel
            ? 0
            : categories.FindIndex(c => string.Equals(c, _favSelectedCategory, StringComparison.OrdinalIgnoreCase)) + 1;
        _favCategoryTree.SelectedIndex = wantIndex >= 0 && wantIndex < _favCategoryTree.Items.Count ? wantIndex : 0;
        _favCategoryTree.SelectionChanged += OnFavCategorySelectionChanged;
        UpdateCategoryRowStates();
    }

    private List<string> GetOrderedCategories()
    {
        var categories = _favorites.Select(f => f.Category).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var ordered = _settings.CategoryOrder
            .Where(saved => categories.Any(category => string.Equals(category, saved, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        ordered.AddRange(categories.Where(category => !ordered.Any(saved => string.Equals(category, saved, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(category => category));
        return ordered;
    }

    private void RefreshFavoritesList(bool refreshCategories = true, int? selectedFavoriteId = null, int? selectedSlot = null)
    {
        RefreshFavoriteTagOptions();
        if (selectedFavoriteId.HasValue || selectedSlot.HasValue)
        {
            _favFilterSelectionId = selectedFavoriteId ?? _favorites.FirstOrDefault(f => f.Slot == selectedSlot)?.Id;
        }

        _refreshingFavorites = true;
        if (refreshCategories)
        {
            RefreshFavoriteCategoryTree();
        }

        _favListBox.Items.Clear();
        _favInsertionLines.Clear();
        _favDetailStrips.Clear();
        _favDetailTexts.Clear();
        _favMainRows.Clear();
        _favSelectionAccents.Clear();
        foreach (var fav in GetFilteredFavorites())
        {
            _favListBox.Items.Add(BuildFavoriteRow(fav));
        }

        _favListBox.SelectedItem = _favListBox.Items.OfType<ListBoxItem>().FirstOrDefault(item =>
            item.Tag is Favorite favorite &&
            favorite.Id == _favFilterSelectionId);
        _refreshingFavorites = false;
        _favEmptyResults.IsVisible = _favListBox.Items.Count == 0;
        UpdateFavoriteRowStates();
        UpdateFavoriteCommandStates();
    }

    private ListBoxItem BuildFavoriteRow(Favorite fav)
    {
        var row = BuildFavColumnGrid(
        [
            new TextBlock { Text = fav.Slot.ToString(), Foreground = SecondaryBrush, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            new TextBlock { Text = fav.IsEmpty ? "— Empty —" : fav.Name, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = fav.IsEmpty ? SecondaryBrush : TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 10, 0) },
            new TextBlock { Text = fav.Category, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0), TextTrimming = TextTrimming.CharacterEllipsis },
            BuildFavoriteTagDisplay(fav.Tags),
            new TextBlock { Text = fav.IsEmpty ? string.Empty : fav.Preset.ToString(), Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0) },
            new TextBlock { Text = fav.IsEmpty ? string.Empty : fav.Scene.ToString(), Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0) },
        ]);
        var accent = new Border { Width = 4, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, IsHitTestVisible = false };
        var divider = new Border { Height = 1, Background = ThemeBrush("RowSeparatorBrush"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        var mainRow = new Grid { Height = 26, Background = Brushes.Transparent };
        mainRow.Children.Add(row); mainRow.Children.Add(accent);

        var detailText = new TextBlock
        {
            Name = "FavoriteDetailText",
            FontSize = 11,
            Foreground = SecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.None,
            Margin = new Thickness(11, 0),
        };
        var detailGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', FavoriteDetailColumns.Select(column => GridLengthToString(column.Width)))),
        };
        var categoryDetail = new TextBlock
        {
            Name = "FavoriteCategoryDetail",
            Text = fav.IsEmpty ? string.Empty : fav.Category,
            FontSize = 11,
            Foreground = SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(15, 0, 10, 0),
        };
        // Keep the category under the favorite name.  The combined preset/scene
        // detail can use the Tags column as well, so long cached names have room
        // to remain fully visible while their right edge stays under Scene.
        Grid.SetColumn(categoryDetail, 1);
        Grid.SetColumn(detailText, 2);
        Grid.SetColumnSpan(detailText, 3);
        detailGrid.Children.Add(categoryDetail);
        detailGrid.Children.Add(detailText);
        var detailStrip = new Border
        {
            Name = "FavoriteDetailStrip",
            // Detail lines are supporting information, not selection rows.
            // Keep their background neutral when details are shown for every favourite.
            Background = Brushes.Transparent,
            MinHeight = 18,
            IsVisible = false,
            Child = detailGrid,
        };
        Grid.SetRow(detailStrip, 1);
        Grid.SetRowSpan(divider, 2);

        var content = new Grid { RowDefinitions = new RowDefinitions("26,Auto"), Background = Brushes.Transparent };
        content.Children.Add(mainRow); content.Children.Add(detailStrip); content.Children.Add(divider);
        var item = new ListBoxItem
        {
            Content = content,
            Tag = fav,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        item.PointerEntered += (_, _) => UpdateFavoriteRowStates(item);
        item.PointerExited += (_, _) => UpdateFavoriteRowStates();
        var edit = new MenuItem { Header = "Edit" };
        edit.Click += (_, _) => ShowFavoriteEditor(fav, isNew: false);
        var clear = new MenuItem { Header = "Clear Slot" };
        clear.Click += (_, _) => ClearFavoriteSlot(fav);
        var remove = new MenuItem { Header = "Remove Slot and Shift Up…" };
        remove.Click += async (_, _) => await RemoveFavoriteSlotAsync(fav);
        item.ContextMenu = new ContextMenu { Items = { edit, new Separator(), clear, remove } };
        item.ContextMenu.Opened += (_, _) =>
        {
            _favListBox.SelectedItem = item;
            UpdateFavoriteCommandStates();
        };
        _favInsertionLines[item] = divider;
        _favDetailStrips[item] = detailStrip;
        _favDetailTexts[item] = detailText;
        _favMainRows[item] = mainRow;
        _favSelectionAccents[item] = accent;
        return item;
    }

    private void SynchronizeFavoriteColumnWidths(Grid header)
    {
        if (header.ColumnDefinitions.Count != FavColumns.Length)
        {
            return;
        }

        for (int index = 0; index < FavColumns.Length; index++)
        {
            FavColumns[index] = (FavColumns[index].Header, header.ColumnDefinitions[index].Width);
        }

        foreach (Grid mainRow in _favMainRows.Values)
        {
            if (mainRow.Children[0] is not Grid row || row.ColumnDefinitions.Count != FavColumns.Length)
            {
                continue;
            }

            for (int index = 0; index < FavColumns.Length; index++)
            {
                row.ColumnDefinitions[index].Width = FavColumns[index].Width;
            }
        }
    }

    private string FavoriteDetailText(Favorite favorite)
    {
        if (favorite.IsEmpty)
        {
            return string.Empty;
        }

        int slot = favorite.Preset - _settings.DisplayOffset;
        string presetName = slot is >= 0 and <= 511 &&
                            _settings.PresetNameCache.TryGetValue(slot, out string? cachedPresetName)
            ? cachedPresetName.Trim()
            : string.Empty;

        string sceneName = string.Empty;
        if (favorite.Scene is >= 1 and <= 8 &&
            _settings.SceneNameCaches.TryGetValue(_sceneCacheKey, out var sceneCache) &&
            sceneCache.TryGetValue(slot, out var entry) &&
            favorite.Scene <= entry.Names.Length)
        {
            sceneName = entry.Names[favorite.Scene - 1]?.Trim() ?? string.Empty;
        }

        return (presetName, sceneName) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{presetName} | {sceneName}",
            ({ Length: > 0 }, _) => presetName,
            (_, { Length: > 0 }) => sceneName,
            _ => string.Empty,
        };
    }

    private Control BuildFavoriteTagDisplay(IEnumerable<string> tags)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, ItemHeight = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0), MaxHeight = 22 };
        foreach (string tag in tags)
        {
            panel.Children.Add(BuildFavoriteTagChip(tag, removable: false));
        }

        return panel;
    }

    private Border BuildFavoriteTagChip(string tag, bool removable)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = tag, FontSize = 12, Foreground = TagTextBrush, VerticalAlignment = VerticalAlignment.Center });
        if (removable)
        {
            var remove = new Button { Content = "\u00D7", FontSize = 13, Foreground = TagRemoveBrush, Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 20, MinWidth = 18, Padding = new Thickness(2, 0), VerticalContentAlignment = VerticalAlignment.Center };
            remove.Click += (_, _) => { _favEditingTags.Remove(tag); RefreshFavoriteTagEditor(); _favTagInput.Focus(); };
            content.Children.Add(remove);
        }
        return new Border { Background = TagBrush, CornerRadius = new CornerRadius(11), Padding = new Thickness(removable ? 9 : 10, 2), Margin = new Thickness(0, 0, 6, 0), Height = 22, Child = content };
    }

    private void RefreshFavoriteTagEditor()
    {
        _favTagsPanel.Children.Clear();
        foreach (string tag in _favEditingTags)
        {
            _favTagsPanel.Children.Add(BuildFavoriteTagChip(tag, removable: true));
        }

        _favTagsPanel.Children.Add(_favTagInput);
    }

    private void CommitFavoriteTag()
    {
        string tag = _favTagInput.Text?.Trim() ?? string.Empty;
        _favTagInput.Text = string.Empty;
        if (tag.Length == 0 || _favEditingTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _favEditingTags.Add(tag);
        RefreshFavoriteTagEditor();
        _favTagInput.Focus();
    }

    private void OnFavTagInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        string text = _favTagInput.Text ?? string.Empty;
        int comma = text.IndexOf(',');
        if (comma < 0)
        {
            return;
        }

        string tag = text[..comma].Trim();
        _favTagInput.Text = text[(comma + 1)..];
        if (tag.Length == 0 || _favEditingTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _favEditingTags.Add(tag);
        RefreshFavoriteTagEditor();
        _favTagInput.Focus();
    }

    private void OnFavTagInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Tab) { CommitFavoriteTag(); e.Handled = true; }
        else if (e.Key is Key.Back && string.IsNullOrEmpty(_favTagInput.Text) && _favEditingTags.Count > 0)
        {
            _favEditingTags.RemoveAt(_favEditingTags.Count - 1);
            RefreshFavoriteTagEditor();
            e.Handled = true;
        }
    }

    private Favorite? SelectedFavorite() => (_favListBox.SelectedItem as ListBoxItem)?.Tag as Favorite;

    private void OnFavCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_favCategoryTree.SelectedIndex < 0)
        {
            return;
        }

        _favSelectedCategory = (_favCategoryTree.SelectedItem as ListBoxItem)?.Tag as string ?? AllCategoriesSentinel;
        RefreshFavoritesList(refreshCategories: false);
        UpdateCategoryRowStates();
    }

    private ListBoxItem BuildCategoryRow(string name, int count)
    {
        var nameLabel = new TextBlock { Text = name, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 8, 0) };
        var badge = new Border { Background = ThemeBrush("BadgeBrush"), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = count.ToString(), FontSize = 12, Foreground = SecondaryBrush } };
        var grid = new Grid { Height = 24, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = Brushes.Transparent };
        grid.Children.Add(nameLabel); Grid.SetColumn(badge, 1); grid.Children.Add(badge);
        grid.Children.Add(new Border { Width = 4, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, IsHitTestVisible = false });
        var insertionLine = new Border { Height = 2, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        grid.Children.Add(insertionLine);
        var item = new ListBoxItem { Content = grid, Tag = name, Margin = new Thickness(0, 0, 0, 3) };
        item.PointerEntered += (_, _) => UpdateCategoryRowStates(item);
        item.PointerExited += (_, _) => UpdateCategoryRowStates();
        if (name != AllCategoriesSentinel)
        {
            var rename = new MenuItem { Header = "Rename" };
            rename.Click += async (_, _) => await RenameFavoriteCategoryAsync(name);
            item.ContextMenu = new ContextMenu { Items = { rename } };
        }
        _favCategoryInsertionLines[item] = insertionLine;
        return item;
    }

    private void UpdateCategoryRowStates(ListBoxItem? hovered = null)
    {
        foreach (var item in _favCategoryTree.Items.OfType<ListBoxItem>())
        {
            var grid = (Grid)item.Content!;
            bool selected = item == _favCategoryTree.SelectedItem;
            grid.Background = selected ? ThemeBrush("SelectedBrush") : item == hovered ? ThemeBrush("HoverBrush") : Brushes.Transparent;
            ((Border)grid.Children[2]).IsVisible = selected;
            // The selected row uses the saturated accent background, so retain the
            // theme's primary text colour for legibility in dark mode.
            ((TextBlock)grid.Children[0]).Foreground = TextBrush;
            ((Border)grid.Children[1]).Background = selected ? ThemeBrush("SelectedBadgeBrush") : ThemeBrush("BadgeBrush");
        }
    }

    private void OnFavCategoryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var item = FavoriteRowFromSource(e.Source);
        if (item?.Tag is string category && category != AllCategoriesSentinel && e.GetCurrentPoint(_favCategoryTree).Properties.IsLeftButtonPressed)
        {
            _favCategoryDragSource = category;
            _favCategoryDragStart = e.GetPosition(_favCategoryTree);
            _favCategoryDragItem = item;
            _favCategoryIsDragging = false;
            e.Pointer.Capture(_favCategoryTree);
            _favCategoryTree.SelectedItem = item;
        }
    }

    private void OnFavCategoryPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_favCategoryDragSource is null || _favCategoryDragStart is null)
        {
            return;
        }

        var position = e.GetPosition(_favCategoryTree);
        if (Math.Abs(position.X - _favCategoryDragStart.Value.X) < 6 && Math.Abs(position.Y - _favCategoryDragStart.Value.Y) < 6)
        {
            return;
        }

        _favCategoryIsDragging = true;
        UpdateCategoryInsertionLine(position);
        e.Handled = true;
    }

    private void OnFavCategoryPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool wasDragging = _favCategoryIsDragging;
        if (_favCategoryIsDragging && _favCategoryDragSource is not null && _favCategoryInsertionItem?.Tag is string target)
        {
            ReorderCategory(_favCategoryDragSource, target, _favCategoryInsertAfter);
        }

        ClearCategoryDragState();
        e.Pointer.Capture(null);
        e.Handled = wasDragging;
    }

    private void OnFavCategoryPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearCategoryDragState();

    private void UpdateCategoryInsertionLine(Point position)
    {
        ListBoxItem? target = null;
        bool after = false;
        foreach (var row in _favCategoryTree.Items.OfType<ListBoxItem>())
        {
            if (row.Tag is not string category || category == AllCategoriesSentinel)
            {
                continue;
            }

            var origin = row.TranslatePoint(new Point(0, 0), _favCategoryTree);
            if (origin is null || position.Y < origin.Value.Y || position.Y > origin.Value.Y + row.Bounds.Height)
            {
                continue;
            }

            target = row;
            after = position.Y >= origin.Value.Y + row.Bounds.Height / 2;
            break;
        }
        if (target is null || target == _favCategoryDragItem)
        {
            return;
        }

        ClearCategoryInsertionLine();
        _favCategoryInsertionItem = target;
        _favCategoryInsertAfter = after;
        var line = _favCategoryInsertionLines[target];
        line.Background = AccentBrush;
        line.VerticalAlignment = after ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    private void ReorderCategory(string source, string target, bool after)
    {
        if (source == AllCategoriesSentinel || target == AllCategoriesSentinel)
        {
            return;
        }

        var categories = GetOrderedCategories();
        int sourceIndex = categories.FindIndex(category => string.Equals(category, source, StringComparison.OrdinalIgnoreCase));
        int targetIndex = categories.FindIndex(category => string.Equals(category, target, StringComparison.OrdinalIgnoreCase));
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        string category = categories[sourceIndex];
        categories.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        if (after)
        {
            targetIndex++;
        }

        categories.Insert(Math.Clamp(targetIndex, 0, categories.Count), category);
        _settings.CategoryOrder = categories;
        _saveSettings(_settings);
        RefreshFavoriteCategoryTree();
        RefreshFavoritesList(refreshCategories: false);
    }

    private void ClearCategoryDragState()
    {
        ClearCategoryInsertionLine();
        _favCategoryDragSource = null;
        _favCategoryDragStart = null;
        _favCategoryDragItem = null;
        _favCategoryIsDragging = false;
    }

    private void ClearCategoryInsertionLine()
    {
        if (_favCategoryInsertionItem is not null)
        {
            _favCategoryInsertionLines[_favCategoryInsertionItem].Background = Brushes.Transparent;
        }

        _favCategoryInsertionItem = null;
        _favCategoryInsertAfter = false;
    }

    private async void OnFavRenameCategory(object? sender, RoutedEventArgs e)
    {
        await RenameFavoriteCategoryAsync(_favSelectedCategory);
    }

    private async Task RenameFavoriteCategoryAsync(string category)
    {
        if (category == AllCategoriesSentinel)
        {
            return;
        }

        string? name = await ShowTextEntryAsync("Rename Collection", "Collection name", category);
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, category, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var favorite in _favorites.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)))
        {
            favorite.Category = name;
        }

        int orderIndex = _settings.CategoryOrder.FindIndex(savedCategory => string.Equals(savedCategory, category, StringComparison.OrdinalIgnoreCase));
        if (orderIndex >= 0) { _settings.CategoryOrder[orderIndex] = name; _saveSettings(_settings); }
        _favSelectedCategory = name;
        _saveFavorites(_favorites);
        RefreshFavoritesList();
    }

    private void RefreshFavoriteCategoryBoxItems()
    {
        string prev = _favCategoryBox.Text ?? string.Empty;
        _favCategoryBox.ItemsSource = _favorites.Select(f => f.Category).Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
        _favCategoryBox.Text = prev;
    }

    private void OnFavNew(object? sender, RoutedEventArgs e)
    {
        _favListBox.SelectedItem = null;
        ShowFavoriteEditor(new Favorite { Slot = _favorites.Count + 1 }, isNew: true);
    }

    private void OnFavEdit(object? sender, RoutedEventArgs e)
    {
        var favorite = SelectedFavorite();
        if (favorite != null)
        {
            ShowFavoriteEditor(favorite, isNew: false);
        }
    }

    internal void ShowFavoriteEditor(Favorite fav, bool isNew)
    {
        RefreshFavoriteCategoryBoxItems();

        _favEditingId = isNew ? 0 : fav.Id;
        _favEditorTitle.Text = isNew ? "New Favorite" : $"Edit: {(fav.IsEmpty ? "— Empty —" : fav.Name)}";
        _favNameBox.Text = fav.Name;
        _favCategoryBox.Text = fav.Category;
        _favEditingTags.Clear();
        _favEditingTags.AddRange(fav.Tags.Distinct(StringComparer.OrdinalIgnoreCase));
        _favTagInput.Text = string.Empty;
        RefreshFavoriteTagEditor();
        // A new favorite starts from the selection currently shown by the controller.
        // Keep the fields empty when no selection is known yet.
        _favPresetSpinner.Value = isNew
            ? _currentPreset is int currentPreset ? Math.Clamp(currentPreset, 0, (int)_favPresetSpinner.Maximum) : null
            : Math.Clamp(fav.Preset, 0, (int)_favPresetSpinner.Maximum);
        _favSceneSpinner.Value = isNew
            ? _activeScene is int activeScene ? Math.Clamp(activeScene, 1, 8) : null
            : Math.Clamp(fav.Scene == 0 ? 1 : fav.Scene, 1, 8);
        _favDeleteBtn.IsVisible = !isNew;
        _favDeleteBtn.IsEnabled = !isNew;

        _favEditorPlaceholder.IsVisible = false;
        _favEditorPanel.IsVisible = true;
        _favEditorCard.IsVisible = true;
        if (!_favoritesPage.Children.Contains(_favEditorCard))
        {
            _favPreviousMinWidth = MinWidth;
            MinWidth = Math.Max(MinWidth, 1200);
            if (Width < MinWidth)
            {
                Width = MinWidth;
            }

            _favoritesPage.ColumnDefinitions = new ColumnDefinitions("210,16,*,16,300");
            Grid.SetColumn(_favEditorCard, 4);
            _favoritesPage.Children.Add(_favEditorCard);
        }
    }

    private void HideFavoriteEditor()
    {
        if (_favPreviousMinWidth is double previousMinWidth)
        {
            MinWidth = previousMinWidth;
            _favPreviousMinWidth = null;
        }

        _favEditingId = null;
        _favEditorPanel.IsVisible = false;
        _favEditorPlaceholder.IsVisible = true;
        _favEditorCard.IsVisible = false;
        _favoritesPage.Children.Remove(_favEditorCard);
        _favoritesPage.ColumnDefinitions = new ColumnDefinitions("210,16,*");
    }

    private void OnFavListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingFavorites)
        {
            _favFilterSelectionId = SelectedFavorite()?.Id;
        }

        UpdateFavoriteCommandStates();
        UpdateFavoriteRowStates();
    }

    private void UpdateFavoriteCommandStates()
    {
        var favorite = SelectedFavorite();
        _favSendBtn.IsEnabled = favorite is { IsEmpty: false } && _midi.OutputOpen;
    }

    private void UpdateFavoriteRowStates(ListBoxItem? hovered = null)
    {
        foreach (var item in _favListBox.Items.OfType<ListBoxItem>())
        {
            var grid = (Grid)item.Content!;
            bool selected = item == _favListBox.SelectedItem;
            grid.Background = Brushes.Transparent;
            _favMainRows[item].Background = selected ? ThemeBrush("SelectedBrush") : item == hovered ? ThemeBrush("HoverBrush") : Brushes.Transparent;
            _favSelectionAccents[item].IsVisible = selected;

            bool showDetails = _showFavoriteDetailsForAll || selected;
            string detail = showDetails && item.Tag is Favorite favorite
                ? FavoriteDetailText(favorite)
                : string.Empty;
            _favDetailTexts[item].Text = detail;
            _favDetailStrips[item].IsVisible = detail.Length > 0 ||
                                               (showDetails && item.Tag is Favorite detailFavorite &&
                                                !detailFavorite.IsEmpty && detailFavorite.Category.Length > 0);
        }
    }

    private void UpdateFavoriteDetailsTogglePresentation()
    {
        bool showingAll = _showFavoriteDetailsForAll;
        _favDetailsToggleIcon.Data = Geometry.Parse(showingAll
            ? "M12,5 C7,5 3.2,8.1 2,12 C3.2,15.9 7,19 12,19 C17,19 20.8,15.9 22,12 C20.8,8.1 17,5 12,5 Z M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8 Z M3,2 L4.7,2 L21,20.3 L19.3,22 Z"
            : "M12,5 C7,5 3.2,8.1 2,12 C3.2,15.9 7,19 12,19 C17,19 20.8,15.9 22,12 C20.8,8.1 17,5 12,5 Z M12,8 A4,4 0 1 1 12,16 A4,4 0 1 1 12,8 Z");
        string action = showingAll
            ? "Show preset and scene details only for the selected favourite"
            : "Show preset and scene details for all favourites";
        AutomationProperties.SetName(_favDetailsToggleBtn, action);
        ToolTip.SetTip(_favDetailsToggleBtn, action);
    }

    private void OnFavListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var item = FavoriteRowFromSource(e.Source);
        var properties = e.GetCurrentPoint(_favListBox).Properties;
        if (item?.Tag is Favorite && properties.IsRightButtonPressed)
        {
            _favListBox.SelectedItem = item;
            UpdateFavoriteCommandStates();
            return;
        }

        if (item?.Tag is Favorite favorite && properties.IsLeftButtonPressed)
        {
            _favDragSource = favorite;
            _favDragStart = e.GetPosition(_favListBox);
            _favDragItem = item;
            _favIsDragging = false;
            e.Pointer.Capture(_favListBox);
            _favListBox.SelectedItem = item;
        }
    }

    private void OnFavListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_favDragSource is null || _favDragStart is null)
        {
            return;
        }

        var position = e.GetPosition(_favListBox);
        if (Math.Abs(position.X - _favDragStart.Value.X) < 6 && Math.Abs(position.Y - _favDragStart.Value.Y) < 6)
        {
            return;
        }

        _favIsDragging = true;
        UpdateFavoriteInsertionLine(e.GetPosition(_favListBox));
        e.Handled = true;
    }

    private void OnFavListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool wasDragging = _favIsDragging;
        if (_favIsDragging && _favDragSource is not null && _favInsertionItem?.Tag is Favorite target)
        {
            ReorderFavorite(_favDragSource, target, _favInsertAfter);
        }

        ClearFavoriteDragState();
        e.Pointer.Capture(null);
        e.Handled = wasDragging;
    }

    private void OnFavListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearFavoriteDragState();

    private void UpdateFavoriteInsertionLine(Point position)
    {
        ListBoxItem? target = null;
        bool after = false;
        foreach (var row in _favListBox.Items.OfType<ListBoxItem>())
        {
            var origin = row.TranslatePoint(new Point(0, 0), _favListBox);
            if (origin is null || position.Y < origin.Value.Y || position.Y > origin.Value.Y + row.Bounds.Height)
            {
                continue;
            }

            target = row;
            after = position.Y >= origin.Value.Y + row.Bounds.Height / 2;
            break;
        }
        if (target is null || target == _favDragItem)
        {
            return;
        }

        ClearFavoriteInsertionLine();
        _favInsertionItem = target;
        _favInsertAfter = after;
        var line = _favInsertionLines[target];
        line.Background = AccentBrush;
        line.VerticalAlignment = after ? VerticalAlignment.Bottom : VerticalAlignment.Top;
    }

    private void ReorderFavorite(Favorite source, Favorite target, bool after)
    {
        int sourceIndex = _favorites.IndexOf(source);
        int targetIndex = _favorites.FindIndex(f => f.Id == target.Id);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        _favorites.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        if (after)
        {
            targetIndex++;
        }

        _favorites.Insert(Math.Clamp(targetIndex, 0, _favorites.Count), source);
        FavoritesManager.RenumberSlots(_favorites);
        _saveFavorites(_favorites);
        RefreshFavoritesList();
    }

    private void ClearFavoriteDragState()
    {
        ClearFavoriteInsertionLine();
        _favDragSource = null;
        _favDragStart = null;
        _favDragItem = null;
        _favIsDragging = false;
    }

    private void ClearFavoriteInsertionLine()
    {
        if (_favInsertionItem is not null)
        {
            _favInsertionLines[_favInsertionItem].Background = Brushes.Transparent;
        }
        _favInsertionItem = null;
        _favInsertAfter = false;
    }

    private static ListBoxItem? FavoriteRowFromSource(object? source) => (source as Visual)?
        .FindAncestorOfType<ListBoxItem>();

    private async void OnFavSave(object? sender, RoutedEventArgs e)
    {
        if (_favEditingId == null)
        {
            return;
        }

        string name = _favNameBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            await ShowMessageAsync("Favorite", "Name is required.");
            return;
        }

        if (_favPresetSpinner.Value is not decimal preset)
        {
            await ShowMessageAsync("Favorite", "Preset is required.");
            return;
        }
        if (_favSceneSpinner.Value is not decimal scene)
        {
            await ShowMessageAsync("Favorite", "Scene is required.");
            return;
        }

        CommitFavoriteTag();
        var tags = _favEditingTags.ToList();

        if (_favEditingId == 0)
        {
            var created = new Favorite
            {
                Id = FavoritesManager.NextId(_favorites),
                Slot = _favorites.Count + 1,
                Name = name,
                Category = _favCategoryBox.Text?.Trim() ?? string.Empty,
                Tags = tags,
                Preset = (int)preset,
                Scene = (int)scene,
            };
            _favorites.Add(created);
        }
        else
        {
            int idx = _favorites.FindIndex(f => f.Id == _favEditingId.Value);
            if (idx < 0)
            {
                return;
            }

            _favorites[idx].Name = name;
            _favorites[idx].Category = _favCategoryBox.Text?.Trim() ?? string.Empty;
            _favorites[idx].Tags = tags;
            _favorites[idx].Preset = (int)preset;
            _favorites[idx].Scene = (int)scene;
        }

        FavoritesManager.RenumberSlots(_favorites);
        _saveFavorites(_favorites);
        HideFavoriteEditor();
        RefreshFavoritesList();
    }

    private void OnFavClearSlot(object? sender, RoutedEventArgs e)
    {
        if (_favEditingId is not int id || id == 0)
        {
            return;
        }

        var fav = _favorites.FirstOrDefault(f => f.Id == id);
        if (fav == null)
        {
            return;
        }

        ClearFavoriteSlot(fav);
    }

    private void OnFavDelete(object? sender, RoutedEventArgs e)
    {
        if (_favEditingId is not int id || id == 0)
        {
            return;
        }

        var fav = _favorites.FirstOrDefault(f => f.Id == id);
        if (fav == null)
        {
            return;
        }

        _deleteSlotDialogFavorite = fav;
        _deleteSlotDialogBackdrop.IsVisible = true;
        Dispatcher.UIThread.Post(() => _deleteSlotDialogRemoveBtn.Focus());
    }

    private Control BuildDeleteSlotDialog()
    {
        _deleteSlotDialogBackdrop = new Border
        {
            Name = "FavoriteDeleteDialogBackdrop",
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(112, 14, 26, 37)),
        };
        AutomationProperties.SetName(_deleteSlotDialogBackdrop, "Delete slot confirmation dialog");

        var dialogHost = new Grid { Background = Brushes.Transparent };
        _deleteSlotDialogBackdrop.Child = dialogHost;
        dialogHost.PointerPressed += (_, e) =>
        {
            if (e.Source == dialogHost)
            {
                CloseDeleteSlotDialog();
                e.Handled = true;
            }
        };
        dialogHost.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            CloseDeleteSlotDialog();
            e.Handled = true;
        };

        var actions = new StackPanel { Spacing = 8 };
        _deleteSlotDialogRemoveBtn = new Button
        {
            Name = "FavoriteDeleteShiftUp",
            Content = "Remove the slot and shift up",
            Background = DangerBrush,
            Foreground = Brushes.White,
            MinHeight = 38,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _deleteSlotDialogRemoveBtn.Click += (_, _) =>
        {
            Favorite? favorite = _deleteSlotDialogFavorite;
            CloseDeleteSlotDialog(returnFocus: false);
            if (favorite != null && ApplyFavoriteSlotRemoval(_favorites, favorite, confirmed: true))
            {
                CompleteFavoriteSlotRemoval(favorite);
            }
        };
        var clear = new Button
        {
            Name = "FavoriteDeleteClearSlot",
            Content = "Clear Slot",
            Foreground = DangerBrush,
            BorderBrush = DangerBrush,
            MinHeight = 38,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        clear.Click += (_, _) =>
        {
            Favorite? favorite = _deleteSlotDialogFavorite;
            CloseDeleteSlotDialog(returnFocus: false);
            if (favorite != null)
            {
                ClearFavoriteSlot(favorite);
            }
        };
        var cancel = new Button
        {
            Name = "FavoriteDeleteCancel",
            Content = "Cancel",
            MinHeight = 38,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        cancel.Click += (_, _) => CloseDeleteSlotDialog();
        actions.Children.Add(_deleteSlotDialogRemoveBtn);
        actions.Children.Add(clear);
        actions.Children.Add(cancel);

        var dialog = new Border
        {
            Name = "FavoriteDeleteDialog",
            Width = 400,
            Background = SurfaceBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16),
            BoxShadow = Elevation,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Delete slot?", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TextBrush },
                    new TextBlock { Text = "Remove this slot and shift later slots up, or clear it while keeping this slot available.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush },
                    actions,
                },
            },
        };
        AutomationProperties.SetName(dialog, "Delete slot?");
        dialogHost.Children.Add(dialog);
        _deleteSlotDialogBackdrop.PointerPressed += (_, e) =>
        {
            if (e.Source is Control source &&
                (ReferenceEquals(source, dialog) || source.GetVisualAncestors().Contains(dialog)))
            {
                return;
            }

            CloseDeleteSlotDialog();
            e.Handled = true;
        };
        return _deleteSlotDialogBackdrop;
    }

    private void CloseDeleteSlotDialog(bool returnFocus = true)
    {
        _deleteSlotDialogBackdrop.IsVisible = false;
        _deleteSlotDialogFavorite = null;
        if (returnFocus && _favDeleteBtn.IsVisible)
        {
            Dispatcher.UIThread.Post(() => _favDeleteBtn.Focus());
        }
    }

    private async void OnFavRemoveSlot(object? sender, RoutedEventArgs e)
    {
        if (_favEditingId is not int id || id == 0)
        {
            return;
        }

        var fav = _favorites.FirstOrDefault(f => f.Id == id);
        if (fav == null)
        {
            return;
        }

        await RemoveFavoriteSlotAsync(fav);
    }

    private void ClearFavoriteSlot(Favorite favorite)
    {
        if (!ApplyFavoriteSlotClear(_favorites, favorite))
        {
            return;
        }

        _saveFavorites(_favorites);
        HideFavoriteEditor();
        RefreshFavoritesList(selectedFavoriteId: favorite.Id);
    }

    internal async Task RemoveFavoriteSlotAsync(Favorite favorite)
    {
        int removedSlot = favorite.Slot;
        const string title = "Remove Slot?";
        string message = $"Remove slot {removedSlot}? Later favorites will move up one slot.";
        bool confirm = _confirmOverride is not null
            ? await _confirmOverride(title, message, "Remove Slot", "Cancel")
            : await ShowConfirmAsync(title, message, "Remove Slot", "Cancel");
        if (!ApplyFavoriteSlotRemoval(_favorites, favorite, confirm))
        {
            return;
        }

        CompleteFavoriteSlotRemoval(favorite, removedSlot);
    }

    private void CompleteFavoriteSlotRemoval(Favorite favorite, int? removedSlot = null)
    {
        int slot = removedSlot ?? favorite.Slot;
        _saveFavorites(_favorites);
        HideFavoriteEditor();
        int? selectedSlot = _favorites.Count == 0 ? null : Math.Min(slot, _favorites.Count);
        RefreshFavoritesList(selectedSlot: selectedSlot);
    }

    internal static bool ApplyFavoriteSlotClear(IList<Favorite> favorites, Favorite target) =>
        FavoritesManager.ClearSlot(favorites, target);

    internal static bool ApplyFavoriteSlotRemoval(IList<Favorite> favorites, Favorite target, bool confirmed) =>
        FavoritesManager.RemoveSlot(favorites, target, confirmed);

    private void OnFavCancel(object? sender, RoutedEventArgs e)
    {
        _favListBox.SelectedItem = null;
        HideFavoriteEditor();
    }

    private void OnFavSend(object? sender, RoutedEventArgs e)
    {
        HandleSend();
    }

    private void OnFavListDoubleClick(object? sender, RoutedEventArgs e)
    {
        var favorite = SelectedFavorite();
        if (favorite is { IsEmpty: false })
        {
            SendFavorite(favorite);
        }
    }

    private void OnFavFilterChanged(object? sender, TextChangedEventArgs e) => RefreshFavoritesList();

    private void OnFavColumnClick(int column)
    {
        if (_favSortColumn == column)
        {
            _favSortAscending = !_favSortAscending;
        }
        else { _favSortColumn = column; _favSortAscending = true; }
        RefreshFavoritesList();
    }

    // ── Tiny modal helpers (Avalonia has no built-in MessageBox) ────
    private async Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
        var tcs = new TaskCompletionSource<bool>();
        var yes = new Button { Content = confirmText, MinWidth = 75, Margin = new Thickness(0, 0, 8, 0) };
        var no = new Button { Content = cancelText, MinWidth = 75 };
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            MinHeight = 140,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } },
                },
            },
        };
        yes.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        no.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closing += (_, _) => tcs.TrySetResult(false);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            tcs.TrySetResult(false);
            dialog.Close();
            e.Handled = true;
        };
        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var ok = new Button { Content = "OK", Width = 75 };
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } },
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<string?> ShowTextEntryAsync(string title, string label, string initialValue)
    {
        var result = new TaskCompletionSource<string?>();
        var input = new TextBox { Text = initialValue };
        var save = new Button { Content = "Save", Width = 80 };
        var cancel = new Button { Content = "Cancel", Width = 80 };
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = label },
                    input,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { save, cancel } },
                },
            },
        };
        save.Click += (_, _) => { result.TrySetResult(input.Text); dialog.Close(); };
        cancel.Click += (_, _) => { result.TrySetResult(null); dialog.Close(); };
        await dialog.ShowDialog(this);
        return await result.Task;
    }
}
