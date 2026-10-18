#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Controls;

namespace PresetMaestro;

// Match Favorites' title/tag spacing without letting content widen a table column.
internal sealed class SceneNameTagsPanel(TextBlock title, SceneTagSummaryPanel tags) : Panel
{
    private const double Gap = 9;
    private double _naturalTitleWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        title.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        tags.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        _naturalTitleWidth = title.DesiredSize.Width;
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : _naturalTitleWidth + tags.NaturalWidth + Gap;
        var (titleWidth, gap) = Divide(width);
        title.Measure(new Size(titleWidth, availableSize.Height));
        tags.Measure(new Size(Math.Max(0, width - titleWidth - gap), availableSize.Height));
        // Row content must never supply a minimum width to the Grid's star columns.
        return new Size(0, Math.Max(title.DesiredSize.Height, tags.DesiredSize.Height));
    }

    private (double TitleWidth, double Gap) Divide(double width)
    {
        double gap = tags.NaturalWidth > 0 ? Gap : 0;
        double titleWidth = Math.Min(_naturalTitleWidth, Math.Max(width * .55, width - gap - tags.NaturalWidth));
        return (titleWidth, gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (titleWidth, gap) = Divide(finalSize.Width);
        title.Arrange(new Rect(0, 0, titleWidth, finalSize.Height));
        double start = Math.Min(finalSize.Width, titleWidth + gap);
        tags.Arrange(new Rect(start, 0, finalSize.Width - start, finalSize.Height));
        return finalSize;
    }
}
#endif
