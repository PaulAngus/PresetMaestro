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
        string name = slot >= 0 && slot < DevicePresets.Capacity(PickerDeviceModel) &&
                      _settings.PresetNameCache.TryGetValue(slot, out string? cachedName) &&
                      !string.IsNullOrWhiteSpace(cachedName)
            ? cachedName.Trim()
            : "(name unavailable)";
        _favPresetDisplayLabel.Text = $"{displayed:000} · {name}";
    }

    private async Task SelectFavoritePresetAsync()
    {
        int offset = _settings.DisplayOffset;
        _favPresetSpinner.Maximum = EffectiveMaximum;
        int currentSlot = Math.Clamp((int)(_favPresetSpinner.Value ?? offset) - offset, 0, DevicePresets.Capacity(PickerDeviceModel) - 1);
        var dialog = new PresetSelectionWindow(_settings.PresetNameCache, currentSlot, _settings.DisplayOffset, PickerDeviceModel) { Icon = Icon };
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

    private int? _favFilterSelectionId;
    private bool _refreshingFavorites;
    private double? _favPreviousMinWidth;
    private TextBlock _favEmptyResults = null!;
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
    private readonly Dictionary<ListBoxItem, Border> _favActiveAccents = [];
    private bool _favInsertAfter;
    private bool _favIsDragging;
    private readonly List<string> _favEditingTags = [];
    private Border _favTagsEditor = null!;
    private WrapPanel _favTagsPanel = null!;
    private TextBox _favTagInput = null!;

    private IEnumerable<Favorite> GetFilteredFavorites()
    {
        IEnumerable<Favorite> result = _favorites.Where(MatchesFavoriteSearch);
        if (_favSortColumn < 0)
        {
            return result;
        }

        Func<Favorite, IComparable> key = _favSortColumn switch
        {
            0 => f => f.Slot,
            1 => f => f.Name,
            2 => f => f.Preset,
            3 => f => f.Scene,
            _ => f => f.Slot,
        };
        return _favSortAscending ? result.OrderBy(key) : result.OrderByDescending(key);
    }

    private void RefreshFavoritesList(bool refreshOptions = true, int? selectedFavoriteId = null, int? selectedSlot = null)
    {
        if (refreshOptions)
        {
            RefreshFavoriteTagOptions();
        }
        if (selectedFavoriteId.HasValue || selectedSlot.HasValue)
        {
            _favFilterSelectionId = selectedFavoriteId ?? _favorites.FirstOrDefault(f => f.Slot == selectedSlot)?.Id;
        }

        _refreshingFavorites = true;

        ClearFavoriteDragState();
        _favListBox.Items.Clear();
        _favInsertionLines.Clear();
        _favDetailStrips.Clear();
        _favDetailTexts.Clear();
        _favMainRows.Clear();
        _favSelectionAccents.Clear();
        _favActiveAccents.Clear();
        foreach (var fav in GetFilteredFavorites())
        {
            _favListBox.Items.Add(BuildFavoriteRow(fav));
        }

        _favListBox.SelectedItem = _favListBox.Items.OfType<ListBoxItem>().FirstOrDefault(item =>
            item.Tag is Favorite favorite &&
            favorite.Id == _favFilterSelectionId);
        _refreshingFavorites = false;
        _favEmptyResults.IsVisible = _favListBox.Items.Count == 0;
        _favResultCount.Text = $"{_favListBox.Items.Count} of {_favorites.Count} favorites";
        UpdateFavoriteRowStates();
        UpdateFavoriteCommandStates();
    }

    private ListBoxItem BuildFavoriteRow(Favorite fav)
    {
        var title = new TextBlock
        {
            Name = "FavoriteTitle",
            Text = fav.IsEmpty ? "— Empty —" : fav.Name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = fav.IsEmpty ? SecondaryBrush : TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(title, fav.Name);
        var titleAndTags = new FavoriteTitleTagsPanel { Margin = new Thickness(6, 0), ClipToBounds = true };
        titleAndTags.Children.Add(title);
        titleAndTags.Children.Add(BuildFavoriteTagDisplay(fav.Tags));
        var row = BuildFavColumnGrid(
        [
            new TextBlock { Text = fav.Slot.ToString(), Foreground = SecondaryBrush, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            titleAndTags,
            new TextBlock { Text = fav.IsEmpty ? string.Empty : fav.Preset.ToString(), Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) },
            new TextBlock { Text = fav.IsEmpty ? string.Empty : fav.Scene.ToString(), Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) },
        ]);
        var accent = new Border { Width = 4, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, IsHitTestVisible = false };
        var activeAccent = new Border { Name = "FavoriteActiveAccent", Width = 4, Background = DangerBrush, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, IsHitTestVisible = false };
        var divider = new Border { Height = 1, Background = ThemeBrush("RowSeparatorBrush"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        var mainRow = new Grid { Height = 26, Background = Brushes.Transparent, ClipToBounds = true };
        mainRow.Children.Add(row); mainRow.Children.Add(activeAccent); mainRow.Children.Add(accent);

        var detailText = new TextBlock
        {
            Name = "FavoriteDetailText",
            FontSize = 11,
            Foreground = SecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.None,
            Margin = new Thickness(6, 0, 12, 0),
        };
        detailText.TextWrapping = TextWrapping.Wrap;
        detailText.TextAlignment = TextAlignment.Right;
        var detailGrid = new Grid { ClipToBounds = true };
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
        _favActiveAccents[item] = activeAccent;
        return item;
    }

    private void SynchronizeFavoriteColumnWidths(Grid header)
    {
        if (_syncingFavoriteWidths || header.ColumnDefinitions.Count != FavColumns.Length)
        {
            return;
        }

        _syncingFavoriteWidths = true;
        for (int index = 0; index < FavColumns.Length; index++)
        {
            FavColumns[index] = (FavColumns[index].Header, header.ColumnDefinitions[index].Width);
        }

        foreach (var other in _favHeaders.Where(other => other != header))
        {
            for (int index = 0; index < FavColumns.Length; index++)
            {
                other.ColumnDefinitions[index].Width = FavColumns[index].Width;
            }
        }

        _syncingFavoriteWidths = false;

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
        string presetName = slot >= 0 && slot < DevicePresets.Capacity(PickerDeviceModel) &&
                            _settings.PresetNameCache.TryGetValue(slot, out string? cachedPresetName)
            ? cachedPresetName.Trim()
            : string.Empty;

        string sceneName = string.Empty;
        if (favorite.Scene is >= 1 and <= 8 &&
            FindFavoriteSceneCacheEntry(slot) is { } entry &&
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

    private SceneCacheEntry? FindFavoriteSceneCacheEntry(int slot)
    {
        // Prefer names for the configured MIDI pair. When viewing a profile
        // offline (or after its ports changed), retain access to its newest
        // cached names without querying the device merely to render a row.
        if (_settings.SceneNameCaches.TryGetValue(_sceneCacheKey, out var current) &&
            current.TryGetValue(slot, out var currentEntry))
        {
            return currentEntry;
        }

        return _settings.SceneNameCaches.Values
            .Select(cache => cache.TryGetValue(slot, out var entry) ? entry : null)
            .Where(entry => entry is not null)
            .OrderByDescending(entry => entry!.RetrievedAt)
            .FirstOrDefault();
    }

    private Control BuildFavoriteTagDisplay(IEnumerable<string> tags)
    {
        var panel = new WrapPanel { Name = "FavoriteInlineTags", Orientation = Orientation.Horizontal, ItemHeight = 22, VerticalAlignment = VerticalAlignment.Center, MaxHeight = 22, ClipToBounds = true };
        foreach (string tag in tags)
        {
            panel.Children.Add(BuildFavoriteTagChip(tag, removable: false));
        }

        ToolTip.SetTip(panel, string.Join(", ", tags));
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

        _favEditingId = isNew ? 0 : fav.Id;
        _favEditorTitle.Text = isNew ? "New Favorite" : $"Edit: {(fav.IsEmpty ? "— Empty —" : fav.Name)}";
        _favNameBox.Text = fav.Name;
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
            MinWidth = Math.Max(MinWidth, 1320);
            if (Width < MinWidth)
            {
                Width = MinWidth;
            }

            _favoritesPage.ColumnDefinitions = new ColumnDefinitions("*,16,300");
            Grid.SetColumn(_favEditorCard, 2);
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
        _favoritesPage.ColumnDefinitions = new ColumnDefinitions("*");
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
            bool activeOnHardware = item.Tag is Favorite activeFavorite &&
                                    !activeFavorite.IsEmpty &&
                                    _detectedPresetSlot is int presetSlot &&
                                    _detectedScene is int scene &&
                                    activeFavorite.Preset == presetSlot + _settings.DisplayOffset &&
                                    activeFavorite.Scene == scene;
            grid.Background = Brushes.Transparent;
            _favMainRows[item].Background = selected ? ThemeBrush("SelectedBrush") : item == hovered ? ThemeBrush("HoverBrush") : Brushes.Transparent;
            _favSelectionAccents[item].Background = activeOnHardware ? DangerBrush : AccentBrush;
            _favSelectionAccents[item].IsVisible = selected;
            _favActiveAccents[item].IsVisible = activeOnHardware && !selected;

            bool showDetails = _showFavoriteDetailsForAll || selected;
            string detail = showDetails && item.Tag is Favorite favorite
                ? FavoriteDetailText(favorite)
                : string.Empty;
            _favDetailTexts[item].Text = detail;
            _favDetailStrips[item].IsVisible = detail.Length > 0;
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
            if (e.ClickCount == 2 && !favorite.IsEmpty)
            {
                SendFavorite(favorite);
                e.Handled = true;
            }
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
            if (origin is null || position.Y < origin.Value.Y || position.Y > origin.Value.Y + row.Bounds.Height ||
                position.X < origin.Value.X || position.X > origin.Value.X + row.Bounds.Width)
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
        if (_favSortColumn >= 0 || HasFavoriteSearch)
        {
            return;
        }

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

    private static ListBoxItem? FavoriteRowFromSource(object? source) => source as ListBoxItem ?? (source as Visual)?.FindAncestorOfType<ListBoxItem>();

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
        var item = e.Source as ListBoxItem ?? FavoriteRowFromSource(e.Source);
        if (item?.Tag is Favorite { IsEmpty: false } favorite)
        {
            _favListBox.SelectedItem = item;
            SendFavorite(favorite);
            e.Handled = true;
        }
    }

    private void OnFavFilterChanged(object? sender, TextChangedEventArgs e) => RefreshFavoritesList();

    private void OnFavColumnClick(int column)
    {
        if (_favSortColumn == column)
        {
            if (_favSortAscending) { _favSortAscending = false; }
            else { _favSortColumn = -1; _favSortAscending = true; }
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
