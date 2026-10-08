#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private FavoriteTablesPanel? _indexTablesPanel;
    private bool _indexInspectorDismissed;
    private bool _indexRevealQueued;

    private void RevealIndexSelection()
    {
        if (_indexRevealQueued) { return; }
        _indexRevealQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _indexRevealQueued = false;
            if (_indexList?.SelectedIndex >= 0) { _indexList.ScrollIntoView(_indexList.SelectedIndex); }
        }, DispatcherPriority.Loaded);
    }

    private void ConfigureIndexTables()
    {
        var list = _indexList!;
        list.Resources["ScrollBarSize"] = 6d;
        list.Styles.Add(new Style(selector => selector.OfType<ScrollBar>())
        {
            Setters = { new Setter(ScrollBar.WidthProperty, 6d), new Setter(ScrollBar.MinWidthProperty, 6d), new Setter(ScrollBar.BackgroundProperty, InsetBrush) },
        });
        list.Styles.Add(new Style(selector => selector.OfType<ScrollBar>().Template().OfType<Thumb>())
        {
            Setters = { new Setter(Thumb.WidthProperty, 6d), new Setter(Thumb.MinHeightProperty, 20d), new Setter(Thumb.CornerRadiusProperty, new CornerRadius(3)), new Setter(Thumb.BackgroundProperty, SecondaryBrush), new Setter(Thumb.OpacityProperty, .5d) },
        });
        foreach (string name in new[] { "PART_LineUpButton", "PART_LineDownButton" })
        {
            list.Styles.Add(new Style(selector => selector.OfType<ScrollBar>().Template().OfType<RepeatButton>().Name(name))
            {
                Setters = { new Setter(RepeatButton.IsVisibleProperty, false) },
            });
        }
        var headers = new Grid { Name = "IndexTableHeaders", Height = FavoriteTablesPanel.HeadingHeight, VerticalAlignment = VerticalAlignment.Top };
        var outlines = new FavoriteTableOutlines { IsHitTestVisible = false, Outline = UiBorderBrush };
        bool headerUpdateQueued = false;
        int pendingCount = 0;
        double pendingWidth = 0;
        list.ItemsPanel = new FuncTemplate<Panel?>(() => _indexTablesPanel = new FavoriteTablesPanel
        {
            Name = "IndexTableLayout",
            // The selected middle option: three tables in an 800px client area,
            // five at 1440px, with room for preset names beside the number field.
            MinimumTableWidth = 224,
            LayoutChanged = (count, width, heights) =>
            {
                outlines.Columns = count; outlines.WidthAvailable = width; outlines.Heights = heights; outlines.InvalidateVisual();
                pendingCount = count; pendingWidth = width;
                if (headerUpdateQueued) { return; }
                if (headers.Children.Count == count && headers.Width == width) { return; }
                headerUpdateQueued = true;
                Dispatcher.UIThread.Post(() =>
                {
                    headerUpdateQueued = false;
                    headers.Width = pendingWidth;
                    if (headers.Children.Count == pendingCount) { return; }
                    headers.Children.Clear(); headers.ColumnDefinitions.Clear();
                    for (int table = 0; table < pendingCount; table++)
                    {
                        if (table > 0) { headers.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(FavoriteTableLayout.Gap))); }
                        headers.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                        var heading = new Grid { Name = $"IndexTable{table}Header", ColumnDefinitions = new("48,*"), Background = InsetBrush, Margin = new(1, 1, 1, 0) };
                        heading.Children.Add(new TextBlock { Text = "Preset", FontSize = 11, Foreground = SecondaryBrush, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center });
                        var name = new TextBlock { Text = "Name", FontSize = 11, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0) };
                        Grid.SetColumn(name, 1); heading.Children.Add(name);
                        Grid.SetColumn(heading, table * 2); headers.Children.Add(heading);
                    }
                }, DispatcherPriority.Loaded);
            },
        });
        list.Template = new FuncControlTemplate<ListBox>((owner, scope) =>
        {
            var presenter = new ItemsPresenter { Name = "PART_ItemsPresenter", ItemsPanel = owner.ItemsPanel };
            scope.Register("PART_ItemsPresenter", presenter);
            var content = new Grid { Children = { outlines, presenter } };
            var headerLayer = new Canvas { Children = { headers } };
            content.Children.Add(headerLayer);
            var viewer = new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                AllowAutoHide = false,
            };
            scope.Register("PART_ScrollViewer", viewer);
            return viewer;
        });
        list.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width == e.NewSize.Width) { return; }
            _indexTablesPanel?.InvalidateMeasure();
            RevealIndexSelection();
        };
        list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!_indexInspectorDismissed || IndexItemFromSource(e.Source) is not ListBoxItem item || item != list.SelectedItem) { return; }
            _indexInspectorDismissed = false; RenderIndexInspector(); RevealIndexSelection();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        list.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (list.SelectedItem is not ListBoxItem current || _indexTablesPanel is null) { return; }
            ListBoxItem? target = null;
            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End)
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
                target = _indexTablesPanel.GetControl(direction, current, false) as ListBoxItem;
            }
            else if (e.Key is Key.PageUp or Key.PageDown)
            {
                int index = list.SelectedIndex;
                var range = FavoriteTableLayout.Distribute(list.ItemCount, _indexTablesPanel.Columns)
                    .First(r => index >= r.Start && index < r.Start + r.Count);
                int step = Math.Max(1, (int)(list.Bounds.Height / Math.Max(26, current.Bounds.Height)));
                int indexTarget = Math.Clamp(index + (e.Key == Key.PageUp ? -step : step), range.Start, range.Start + range.Count - 1);
                target = list.Items[indexTarget] as ListBoxItem;
            }
            else if (e.Key == Key.Enter)
            {
                _indexInspectorDismissed = false; RenderIndexInspector(); RevealIndexSelection(); e.Handled = true;
            }
            else if (e.Key == Key.Escape && _indexInspectorFrame?.IsVisible == true)
            {
                CloseIndexInspector(); e.Handled = true;
            }
            if (target is not null)
            {
                list.SelectedItem = target; target.Focus(); RevealIndexSelection(); e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }
}

// The dock takes height only; its catalog always has the complete available width.
internal sealed class IndexDockLayout : Grid
{
    private bool _dockOpen;
    internal bool DockOpen
    {
        get => _dockOpen;
        set { if (_dockOpen == value) { return; } _dockOpen = value; InvalidateMeasure(); }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : 600;
        double dockHeight = 0;
        if (DockOpen)
        {
            var dock = Children.Single(child => GetRow(child) == 2);
            dock.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            dockHeight = Math.Min(dock.DesiredSize.Height, Math.Min(360, Math.Max(0, Math.Min(height * .52, height - 152))));
        }
        RowDefinitions[1].Height = new GridLength(DockOpen ? 12 : 0);
        RowDefinitions[2].Height = new GridLength(dockHeight);
        return base.MeasureOverride(availableSize);
    }
}
#endif
