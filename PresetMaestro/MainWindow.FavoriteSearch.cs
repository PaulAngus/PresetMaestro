using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private const double TagMatchSegmentWidth = 66;
    private sealed record SearchChip(string Value, bool IsTag);
    private readonly List<SearchChip> _favSearchChips = [];
    private readonly List<string> _favAvailableTags = [];
    private bool _favTagMatchAll = true;
    private bool _updatingSearch;
    private Grid _favSearchHost = null!;
    private WrapPanel _favSearchPanel = null!;
    private Popup _favSearchPopup = null!;
    private ListBox _favSearchSuggestions = null!;
    private ToggleButton _favTagMatchAllButton = null!;
    private ToggleButton _favTagMatchAnyButton = null!;
    private TranslateTransform _favTagMatchThumbTransform = null!;
    private TextBlock _favResultCount = null!;
    private bool HasFavoriteSearch => _favSearchChips.Count > 0 || !string.IsNullOrWhiteSpace(_favSearchBox.Text);

    private Control BuildFavoriteSearch()
    {
        _favSearchHost = new Grid { Name = "FavoriteSearch", ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        _favSearchPanel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _favSearchBox = new TextBox
        {
            Name = "FavoriteSearchInput",
            Watermark = "Add tag or search term…",
            MinWidth = 218,
            MinHeight = 26,
            Padding = new Thickness(4, 2),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        AutomationProperties.SetName(_favSearchBox, "Add tag or search term");
        _favSearchBox.TextChanged += (_, _) =>
        {
            if (_updatingSearch)
            {
                return;
            }

            RefreshSearchSuggestions();
            RefreshFavoritesList(refreshOptions: false);
        };
        _favSearchBox.AddHandler(KeyDownEvent, OnFavoriteSearchKeyDown, RoutingStrategies.Tunnel);
        _favSearchPanel.Children.Add(_favSearchBox);
        var inset = new Border
        {
            Name = "FavoriteSearchEntry",
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 2),
            MinHeight = 32,
            Child = _favSearchPanel
        };
        inset.PointerPressed += (_, e) => { if (e.Source == inset) { _favSearchBox.Focus(); } };
        _favSearchHost.Children.Add(inset);
        _favSearchSuggestions = new ListBox
        {
            Name = "FavoriteSearchSuggestions",
            MinWidth = 300,
            MaxHeight = 240,
            Background = SurfaceBrush,
            ItemContainerTheme = CompactListItemTheme(),
            Focusable = false
        };
        _favSearchPopup = new Popup
        {
            PlacementTarget = inset,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            Child = new Border
            {
                BorderBrush = UiBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(4),
                Background = SurfaceBrush,
                Child = _favSearchSuggestions
            }
        };
        _favSearchHost.Children.Add(_favSearchPopup);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(new TextBlock { Text = "Tag Match", FontSize = 12, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _favTagMatchAllButton = BuildTagMatchButton("FavoriteTagMatchAll", "All tags");
        _favTagMatchAnyButton = BuildTagMatchButton("FavoriteTagMatchAny", "Any tag");
        AutomationProperties.SetName(_favTagMatchAllButton, "Match all tag chips");
        AutomationProperties.SetName(_favTagMatchAnyButton, "Match any tag chip");
        _favTagMatchAllButton.Click += (_, _) => SetFavoriteTagMatchMode(true);
        _favTagMatchAnyButton.Click += (_, _) => SetFavoriteTagMatchMode(false);
        _favTagMatchThumbTransform = new TranslateTransform(_favTagMatchAll ? TagMatchSegmentWidth : 0, 0)
        {
            Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(180) } }
        };
        var modeGrid = new Grid { Width = TagMatchSegmentWidth * 2, Height = 26, ColumnDefinitions = new ColumnDefinitions("*,*") };
        var thumb = new Border
        {
            Width = TagMatchSegmentWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = SurfaceBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            RenderTransform = _favTagMatchThumbTransform,
            IsHitTestVisible = false
        };
        Grid.SetColumnSpan(thumb, 2);
        modeGrid.Children.Add(thumb);
        modeGrid.Children.Add(_favTagMatchAnyButton);
        Grid.SetColumn(_favTagMatchAllButton, 1);
        modeGrid.Children.Add(_favTagMatchAllButton);
        actions.Children.Add(new Border
        {
            Name = "FavoriteTagMatchSlider",
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            Child = modeGrid
        });
        var clear = new Button { Name = "FavoriteClearSearch", Content = "Clear search", MinHeight = 28, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 0) };
        clear.Click += (_, _) => { ResetFavoriteSearch(); RefreshFavoritesList(); _favSearchBox.Focus(); };
        actions.Children.Add(clear);
        Grid.SetColumn(actions, 2); _favSearchHost.Children.Add(actions);
        UpdateFavoriteTagFilterPresentation();
        RenderSearchChips();
        return _favSearchHost;
    }

    private static ToggleButton BuildTagMatchButton(string name, string text) => new()
    {
        Name = name,
        Content = text,
        FontSize = 12,
        FontWeight = FontWeight.Normal,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Template = new FuncControlTemplate<ToggleButton>((owner, _) => new ContentPresenter
        {
            Content = owner.Content,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        })
    };

    private void RefreshFavoriteTagOptions()
    {
        var tags = _favorites.SelectMany(f => f.Tags).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        bool changed = !_favAvailableTags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase);
        _favAvailableTags.Clear(); _favAvailableTags.AddRange(tags);
        if (_favSearchChips.RemoveAll(c => c.IsTag && !tags.Contains(c.Value, StringComparer.OrdinalIgnoreCase)) > 0)
        {
            RenderSearchChips();
        }

        if (changed)
        {
            RefreshSearchSuggestions();
        }
    }

    private SearchChip PreviewChip(string text) => _favAvailableTags.FirstOrDefault(tag => string.Equals(tag, text, StringComparison.OrdinalIgnoreCase)) is string exact
        ? new SearchChip(exact, true) : new SearchChip(text, false);

    private bool MatchesFavoriteSearch(Favorite favorite)
    {
        var chips = _favSearchChips.AsEnumerable();
        string input = _favSearchBox.Text?.Trim() ?? "";
        if (input.Length > 0)
        {
            chips = chips.Append(PreviewChip(input));
        }

        var tags = chips.Where(c => c.IsTag).ToList();
        bool tagMatch = tags.Count == 0 || (_favTagMatchAll
            ? tags.All(c => favorite.Tags.Contains(c.Value, StringComparer.OrdinalIgnoreCase))
            : tags.Any(c => favorite.Tags.Contains(c.Value, StringComparer.OrdinalIgnoreCase)));
        string searchable = string.Join("\n", favorite.Name, favorite.TagsDisplay, favorite.Slot.ToString(),
            favorite.IsEmpty ? "" : favorite.Preset.ToString(), favorite.IsEmpty ? "" : favorite.Scene.ToString(), FavoriteDetailText(favorite));
        return tagMatch && chips.Where(c => !c.IsTag).All(c => searchable.Contains(c.Value, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshSearchSuggestions()
    {
        if (_favSearchSuggestions is null)
        {
            return;
        }

        _favSearchSuggestions.Items.Clear();
        string input = _favSearchBox.Text?.Trim() ?? "";
        if (input.Length == 0) { _favSearchPopup.IsOpen = false; return; }
        var choices = _favAvailableTags.Where(t => t.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Select(t => new SearchChip(t, true)).Append(new SearchChip(input, false));
        foreach (var chip in choices)
        {
            var button = new Button
            {
                Content = chip.IsTag ? chip.Value : $"Search text “{chip.Value}”",
                Tag = chip,
                Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MinHeight = 28
            };
            button.Click += (_, _) => CommitSearchChip(chip);
            _favSearchSuggestions.Items.Add(new ListBoxItem { Content = button, Tag = chip, Focusable = false });
        }
        // Enter without arrow navigation always preserves the live preview's meaning.
        _favSearchSuggestions.SelectedIndex = -1;
        _favSearchPopup.IsOpen = _favSearchBox.IsKeyboardFocusWithin;
    }

    private void CommitSearchChip(SearchChip chip)
    {
        if (!_favSearchChips.Any(c => c.IsTag == chip.IsTag && string.Equals(c.Value, chip.Value, StringComparison.OrdinalIgnoreCase)))
        {
            _favSearchChips.Add(chip);
        }

        _updatingSearch = true;
        _favSearchBox.Text = "";
        _updatingSearch = false;
        _favSearchPopup.IsOpen = false;
        RenderSearchChips();
        RefreshFavoritesList(refreshOptions: false);
        _favSearchBox.Focus();
    }

    private void RenderSearchChips()
    {
        // Keep the live TextBox attached so filtering and chip edits retain keyboard focus.
        foreach (var child in _favSearchPanel.Children.Where(c => c != _favSearchBox).ToArray())
        {
            _favSearchPanel.Children.Remove(child);
        }

        for (int index = 0; index < _favSearchChips.Count; index++)
        {
            var chip = _favSearchChips[index];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            row.Children.Add(new TextBlock { Text = (chip.IsTag ? "" : "Text: ") + chip.Value, FontSize = 12, Foreground = TagTextBrush, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 380, TextTrimming = TextTrimming.CharacterEllipsis });
            ToolTip.SetTip(row, chip.Value);
            var remove = new Button { Name = $"FavoriteSearchRemove{index}", Content = "×", MinHeight = 22, Padding = new Thickness(3, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = TagRemoveBrush };
            AutomationProperties.SetName(remove, $"Remove {(chip.IsTag ? "tag" : "text search")} {chip.Value}");
            remove.Click += (_, _) => { _favSearchChips.Remove(chip); RenderSearchChips(); RefreshFavoritesList(refreshOptions: false); _favSearchBox.Focus(); };
            row.Children.Add(remove);
            _favSearchPanel.Children.Insert(index, new Border { Background = TagBrush, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 3, 0), Margin = new Thickness(0, 2, 5, 2), Child = row });
        }
    }

    private void OnFavoriteSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string input = _favSearchBox.Text?.Trim() ?? "";
            if (input.Length > 0)
            {
                CommitSearchChip(_favSearchPopup.IsOpen && _favSearchSuggestions.SelectedItem is ListBoxItem { Tag: SearchChip choice } ? choice : PreviewChip(input));
            }

            e.Handled = true;
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (!_favSearchPopup.IsOpen)
            {
                RefreshSearchSuggestions();
            }

            _favSearchSuggestions.SelectedIndex = Math.Clamp(_favSearchSuggestions.SelectedIndex + (e.Key == Key.Down ? 1 : -1), -1, _favSearchSuggestions.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { _favSearchPopup.IsOpen = false; e.Handled = true; }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(_favSearchBox.Text) && _favSearchChips.Count > 0)
        {
            _favSearchChips.RemoveAt(_favSearchChips.Count - 1); RenderSearchChips(); RefreshFavoritesList(refreshOptions: false); e.Handled = true;
        }
    }

    private void ResetFavoriteSearch()
    {
        _updatingSearch = true;
        _favSearchChips.Clear(); _favTagMatchAll = true; _favSearchBox.Text = "";
        _favSearchPopup.IsOpen = false;
        _updatingSearch = false;
        RenderSearchChips(); UpdateFavoriteTagFilterPresentation();
    }

    private void SetFavoriteTagMatchMode(bool matchAll)
    {
        _favTagMatchAll = matchAll; UpdateFavoriteTagFilterPresentation(); RefreshFavoritesList(refreshOptions: false);
    }

    private void UpdateFavoriteTagFilterPresentation()
    {
        _favTagMatchAllButton.IsChecked = _favTagMatchAll;
        _favTagMatchAnyButton.IsChecked = !_favTagMatchAll;
        _favTagMatchAllButton.Foreground = _favTagMatchAll ? AccentBrush : SecondaryBrush;
        _favTagMatchAnyButton.Foreground = !_favTagMatchAll ? AccentBrush : SecondaryBrush;
        _favTagMatchThumbTransform.X = _favTagMatchAll ? TagMatchSegmentWidth : 0;
    }

    private bool FavoriteSearchHasFocus => _favSearchHost?.IsKeyboardFocusWithin == true || _favSearchPopup?.IsKeyboardFocusWithin == true;
}
