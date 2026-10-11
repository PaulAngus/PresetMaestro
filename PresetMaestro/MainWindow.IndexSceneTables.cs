#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private void ConfigureIndexSceneTables()
    {
        var list = _indexSceneList!;
        ConfigureIndexTableScrollBar(list);
        var headers = new Grid { Name = "IndexSceneHeaders", Height = FavoriteTablesPanel.HeadingHeight };
        var outlines = new FavoriteTableOutlines { IsHitTestVisible = false, Outline = UiBorderBrush };
        bool queued = false;
        int pendingCount = 0;
        double pendingWidth = 0;
        list.ItemsPanel = new FuncTemplate<Panel?>(() => _indexSceneTablesPanel = new SceneTablesPanel
        {
            Name = "IndexSceneTableLayout",
            LayoutChanged = (count, width, heights) =>
            {
                outlines.Columns = count; outlines.WidthAvailable = width; outlines.Heights = heights; outlines.InvalidateVisual();
                pendingCount = count; pendingWidth = width;
                if (queued || headers.Children.Count == count && headers.Width == width) { return; }
                queued = true;
                Dispatcher.UIThread.Post(() =>
                {
                    queued = false;
                    headers.Width = pendingWidth;
                    if (headers.Children.Count == pendingCount) { return; }
                    headers.Children.Clear(); headers.ColumnDefinitions.Clear();
                    for (int table = 0; table < pendingCount; table++)
                    {
                        if (table > 0) { headers.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(FavoriteTableLayout.Gap))); }
                        headers.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                        var heading = new Grid { Name = $"IndexSceneTable{table}Header", ColumnDefinitions = SceneResultColumns(), Background = InsetBrush, Margin = new(4, 1, 4, 0) };
                        string[] labels = ["Scene name", "Scene", "Preset name", "Preset", "Scene tags"];
                        for (int column = 0; column < labels.Length; column++)
                        {
                            var label = new TextBlock { Text = labels[column], FontSize = 11, Foreground = SecondaryBrush, Margin = new(6, 0), VerticalAlignment = VerticalAlignment.Center };
                            Grid.SetColumn(label, column); heading.Children.Add(label);
                        }
                        Grid.SetColumn(heading, table * 2); headers.Children.Add(heading);
                    }
                }, DispatcherPriority.Loaded);
            },
        });
        list.Template = new FuncControlTemplate<ListBox>((owner, scope) =>
        {
            var presenter = new ItemsPresenter { Name = "PART_ItemsPresenter", ItemsPanel = owner.ItemsPanel };
            scope.Register("PART_ItemsPresenter", presenter);
            var content = new Grid { Children = { outlines, presenter, new Canvas { Children = { headers } } } };
            var viewer = new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Content = content,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
                AllowAutoHide = false,
            };
            scope.Register("PART_ScrollViewer", viewer);
            return viewer;
        });
        list.SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width == e.NewSize.Width) { return; }
            _indexSceneTablesPanel?.InvalidateMeasure(); RevealIndexSelection();
        };
    }
}
#endif
