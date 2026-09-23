using Avalonia;
using Avalonia.Controls;

namespace PresetMaestro;

// Tags follow the measured title, sharing its field instead of lining up in a column.
internal sealed class FavoriteTitleTagsPanel : Panel
{
    private const double Gap = 9;
    private double _titleWidth;
    private double _gap;

    protected override Size MeasureOverride(Size availableSize)
    {
        var title = Children[0];
        var tags = Children[1];
        title.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        tags.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        _gap = tags.DesiredSize.Width > 0 ? Gap : 0;
        double naturalTitle = title.DesiredSize.Width;
        double width = double.IsFinite(availableSize.Width)
            ? availableSize.Width : naturalTitle + _gap + tags.DesiredSize.Width;
        // A long title keeps at least 55% of the shared space. Short titles leave
        // all remaining room for tags; untagged titles can use the whole field.
        _titleWidth = Math.Min(naturalTitle, Math.Max(width * 0.55, width - _gap - tags.DesiredSize.Width));
        title.Measure(new Size(_titleWidth, availableSize.Height));
        tags.Measure(new Size(Math.Max(0, width - _titleWidth - _gap), availableSize.Height));
        return new Size(_titleWidth + _gap + tags.DesiredSize.Width, Math.Max(title.DesiredSize.Height, tags.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double titleWidth = Math.Min(_titleWidth, finalSize.Width);
        Children[0].Arrange(new Rect(0, 0, titleWidth, finalSize.Height));
        double start = Math.Min(finalSize.Width, titleWidth + _gap);
        Children[1].Arrange(new Rect(start, 0, finalSize.Width - start, finalSize.Height));
        return finalSize;
    }
}
