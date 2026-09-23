using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PresetMaestro.Core;

namespace PresetMaestro;

// One ListBox owns every item. Resizing only lays out existing containers; it never
// rebuilds rows, changes selection, or writes slots. Headers share its scroll region.
internal sealed class FavoriteTablesPanel : Panel, INavigableContainer
{
    internal const double HeadingHeight = 30;
    internal int Columns { get; private set; } = 2;
    internal Action<int, double, double[]>? LayoutChanged { get; init; }
    internal IBrush Outline { get; set; } = Brushes.Gray;
    private double _width;
    private double[] _heights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 900;
        // A ScrollViewer can retain the same arranged extent after a measure with
        // a different viewport. Our distribution depends on that viewport too.
        InvalidateArrange();
        Columns = FavoriteTableLayout.TableCount(width);
        // Tables stay at their minimum width rather than stretching to fill the viewport.
        double cell = FavoriteTableLayout.MinimumWidth + 2;
        _width = Columns * cell + (Columns - 1) * FavoriteTableLayout.Gap;
        _heights = new double[Columns];
        var ranges = FavoriteTableLayout.Distribute(Children.Count, Columns);
        for (int column = 0; column < Columns; column++)
        {
            double height = HeadingHeight;
            var range = ranges[column];
            for (int index = range.Start; index < range.Start + range.Count; index++)
            {
                Children[index].Measure(new Size(Math.Max(0, cell - 2), double.PositiveInfinity));
                height += Children[index].DesiredSize.Height;
            }
            _heights[column] = height + 1;
        }
        LayoutChanged?.Invoke(Columns, _width, _heights);
        return new Size(_width, _heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double cell = (_width - (Columns - 1) * FavoriteTableLayout.Gap) / Columns;
        var ranges = FavoriteTableLayout.Distribute(Children.Count, Columns);
        for (int column = 0; column < Columns; column++)
        {
            double y = HeadingHeight;
            var range = ranges[column];
            for (int index = range.Start; index < range.Start + range.Count; index++)
            {
                var child = Children[index];
                child.Arrange(new Rect(column * (cell + FavoriteTableLayout.Gap) + 1, y, Math.Max(0, cell - 2), child.DesiredSize.Height));
                y += child.DesiredSize.Height;
            }
        }
        InvalidateVisual();
        return finalSize;
    }

    public IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int index = from is Control control ? Children.IndexOf(control) : -1;
        if (Children.Count == 0)
        {
            return null;
        }

        if (index < 0)
        {
            return Children[0];
        }

        var ranges = FavoriteTableLayout.Distribute(Children.Count, Columns);
        int column = Array.FindIndex(ranges, r => index >= r.Start && index < r.Start + r.Count);
        int row = index - ranges[column].Start;
        int target = index;
        switch (direction)
        {
            case NavigationDirection.Up: if (row > 0) { target--; } break;
            case NavigationDirection.Down: if (row + 1 < ranges[column].Count) { target++; } break;
            case NavigationDirection.Left:
            case NavigationDirection.Right:
                int next = column + (direction == NavigationDirection.Left ? -1 : 1);
                if (next >= 0 && next < Columns && ranges[next].Count > 0)
                {
                    target = ranges[next].Start + Math.Min(row, ranges[next].Count - 1);
                }

                break;
            case NavigationDirection.First: target = 0; break;
            case NavigationDirection.Last: target = Children.Count - 1; break;
            case NavigationDirection.Next: target = Math.Min(index + 1, Children.Count - 1); break;
            case NavigationDirection.Previous: target = Math.Max(index - 1, 0); break;
        }
        return Children[target];
    }
}

internal sealed class FavoriteTableOutlines : Control
{
    internal int Columns = 2;
    internal double WidthAvailable;
    internal double[] Heights = [];
    internal IBrush Outline = Brushes.Gray;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double cell = (WidthAvailable - (Columns - 1) * FavoriteTableLayout.Gap) / Columns;
        for (int column = 0; column < Heights.Length; column++)
        {
            context.DrawRectangle(null, new Pen(Outline, 1), new Rect(column * (cell + FavoriteTableLayout.Gap) + .5, .5, Math.Max(0, cell - 1), Heights[column] - 1), 5, 5);
        }
    }

}
