using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly List<Grid> _favHeaders = [];
    private Grid _favHeaderGroup = null!;
    private FavoriteTablesPanel? _favTablesPanel;
    private bool _syncingFavoriteWidths;
    private bool _favHeaderUpdateQueued;
    private (int Count, double Width) _favPendingHeaderLayout;

    private bool _favViewportLayoutQueued;

    private void QueueFavoriteViewportLayout()
    {
        if (_currentPage != AppPage.Favorites || _favViewportLayoutQueued)
        {
            return;
        }
        _favViewportLayoutQueued = true;
        // On native DPI-scaled windows, shrinking can leave nested shell/toolbar
        // measurements cached at the previous width. Queue from the Window size
        // event, after its native resize completes. Reuse every control and focus.
        Dispatcher.UIThread.Post(() =>
        {
            _favViewportLayoutQueued = false;
            if (Content is Control content)
            {
                foreach (var control in content.GetVisualDescendants().OfType<Control>())
                {
                    control.InvalidateMeasure();
                    control.InvalidateArrange();
                }
                content.InvalidateMeasure();
                content.InvalidateArrange();
            }
        }, DispatcherPriority.Loaded);
    }

    private void ConfigureFavoriteTables()
    {
        _favHeaders.Clear();
        _favHeaderGroup = new Grid { Name = "FavoriteTableHeaders", VerticalAlignment = VerticalAlignment.Top, Height = FavoriteTablesPanel.HeadingHeight };
        var outlines = new FavoriteTableOutlines { IsHitTestVisible = false, Outline = UiBorderBrush };
        _favListBox.Name = "FavoriteTables";
        _favListBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.PageUp or Key.PageDown) || _favTablesPanel is null ||
                _favListBox.SelectedItem is not ListBoxItem current)
            {
                return;
            }
            int index = _favListBox.SelectedIndex;
            var range = FavoriteTableLayout.Distribute(_favListBox.Items.Count, _favTablesPanel.Columns)
                .First(r => index >= r.Start && index < r.Start + r.Count);
            int step = Math.Max(1, (int)(_favListBox.Bounds.Height / Math.Max(26, current.Bounds.Height)));
            int target = Math.Clamp(index + (e.Key == Key.PageUp ? -step : step), range.Start, range.Start + range.Count - 1);
            _favListBox.SelectedIndex = target;
            ((ListBoxItem)_favListBox.Items[target]!).Focus();
            _favListBox.ScrollIntoView(target);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _favListBox.ItemsPanel = new FuncTemplate<Panel?>(() => _favTablesPanel = new FavoriteTablesPanel
        {
            Name = "FavoriteTableLayout",
            Outline = UiBorderBrush,
            LayoutChanged = (count, width, heights) =>
            {
                outlines.Columns = count; outlines.WidthAvailable = width; outlines.Heights = heights; outlines.InvalidateVisual();
                _favPendingHeaderLayout = (count, width);
                if (!_favHeaderUpdateQueued && (_favHeaders.Count != count || _favHeaderGroup.Width != width))
                {
                    _favHeaderUpdateQueued = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _favHeaderUpdateQueued = false;
                        UpdateFavoriteHeaders(_favPendingHeaderLayout.Count, _favPendingHeaderLayout.Width);
                    }, DispatcherPriority.Loaded);
                }
            },
        });
        _favListBox.Template = new FuncControlTemplate<ListBox>((owner, scope) =>
        {
            var presenter = new ItemsPresenter { Name = "PART_ItemsPresenter", ItemsPanel = owner.ItemsPanel };
            scope.Register("PART_ItemsPresenter", presenter);
            var content = new Grid();
            content.Children.Add(outlines);
            content.Children.Add(presenter);
            // Headings follow the measured viewport but must not contribute their
            // previous explicit width to the next (possibly smaller) measure.
            var headerLayer = new Canvas();
            headerLayer.Children.Add(_favHeaderGroup);
            content.Children.Add(headerLayer);
            var viewer = new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                // Reserve a stable six-pixel gutter even for short lists.
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                AllowAutoHide = false,
            };
            scope.Register("PART_ScrollViewer", viewer);
            return viewer;
        });
    }

    private void UpdateFavoriteHeaders(int count, double width)
    {
        if (_favHeaders.Count == count) { _favHeaderGroup.Width = width; return; }
        _favHeaderGroup.Children.Clear();
        _favHeaderGroup.ColumnDefinitions.Clear();
        _favHeaders.Clear();
        for (int table = 0; table < count; table++)
        {
            if (table > 0)
            {
                _favHeaderGroup.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(FavoriteTableLayout.Gap)));
            }

            _favHeaderGroup.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            int number = table;
            var headings = BuildFavColumnGrid(FavColumns.Select((column, index) =>
            {
                var button = new Button
                {
                    Name = $"FavoriteTable{number}Heading{index}",
                    Content = column.Header,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(6, 0),
                    Foreground = SecondaryBrush,
                    FontSize = 11,
                    MinHeight = 0,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                AutomationProperties.SetName(button, $"Sort favorites by {column.Header}, table {number + 1}");
                button.Click += (_, _) => OnFavColumnClick(index);
                return (Control)button;
            }).ToArray());
            headings.Name = table == 0 ? "FavoriteTableHeader" : $"FavoriteTableHeader{table}";
            foreach (var definition in headings.ColumnDefinitions)
            {
                definition.PropertyChanged += (_, change) =>
                {
                    if (change.Property == ColumnDefinition.WidthProperty)
                    {
                        SynchronizeFavoriteColumnWidths(headings);
                    }
                };
            }

            for (int field = 0; field < FavColumns.Length - 1; field++)
            {
                var separator = new Border
                {
                    Name = $"FavoriteTable{table}HeaderSeparator{field}",
                    Width = 1,
                    Background = ThemeBrush("RowSeparatorBrush"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    IsHitTestVisible = false
                };
                Grid.SetColumn(separator, field); headings.Children.Add(separator);
                var splitter = new GridSplitter
                {
                    Name = $"FavoriteTable{table}Resize{field}",
                    Width = 6,
                    Background = Brushes.Transparent,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    ResizeDirection = GridResizeDirection.Columns,
                    ResizeBehavior = GridResizeBehavior.CurrentAndNext,
                    Cursor = new Cursor(StandardCursorType.SizeWestEast)
                };
                AutomationProperties.SetName(splitter, $"Resize {FavColumns[field].Header}, table {table + 1}");
                Grid.SetColumn(splitter, field); headings.Children.Add(splitter);
            }
            _favHeaders.Add(headings);
            var border = new Border
            {
                Name = $"FavoriteTable{table}",
                Background = InsetBrush,
                BorderBrush = UiBorderBrush,
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(5, 5, 0, 0),
                ClipToBounds = true,
                Child = headings
            };
            Grid.SetColumn(border, table * 2); _favHeaderGroup.Children.Add(border);
        }
        _favHeaderGroup.Width = width;
    }
}
