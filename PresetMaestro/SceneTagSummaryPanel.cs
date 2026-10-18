#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PresetMaestro;

// Tag content must not contribute a minimum width to a row's star columns.
// Show complete pills followed by a count instead of clipping a pill in half.
internal sealed class SceneTagSummaryPanel : Panel
{
    private readonly Control[] _tags;
    private readonly TextBlock[] _overflow;
    internal int VisibleTagCount { get; private set; }
    internal double NaturalWidth => _tags.Sum(tag => tag.DesiredSize.Width);

    public SceneTagSummaryPanel(IEnumerable<Control> tags, IBrush foreground)
    {
        _tags = tags.ToArray();
        _overflow = Enumerable.Range(1, _tags.Length).Select(count => new TextBlock
        {
            Text = $"+{count}",
            FontSize = 12,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        }).ToArray();
        ClipToBounds = true;
        foreach (var tag in _tags) { Children.Add(tag); }
        foreach (var label in _overflow)
        {
            AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
            Children.Add(label);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) { child.Measure(new Size(double.PositiveInfinity, 20)); }
        return new Size(0, 20);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double used = 0;
        int visible = _tags.Length;
        if (NaturalWidth > finalSize.Width)
        {
            visible = 0;
            while (visible < _tags.Length)
            {
                double next = used + _tags[visible].DesiredSize.Width;
                int remaining = _tags.Length - visible - 1;
                double countWidth = remaining > 0 ? _overflow[remaining - 1].DesiredSize.Width : 0;
                if (next + countWidth > finalSize.Width) { break; }
                used = next; visible++;
            }
        }
        VisibleTagCount = visible;
        used = 0;
        for (int index = 0; index < _tags.Length; index++)
        {
            var tag = _tags[index];
            tag.Arrange(new Rect(index < visible ? used : finalSize.Width + 1, 0, tag.DesiredSize.Width, finalSize.Height));
            if (index < visible) { used += tag.DesiredSize.Width; }
        }
        int hidden = _tags.Length - visible;
        for (int index = 0; index < _overflow.Length; index++)
        {
            var label = _overflow[index];
            label.Arrange(new Rect(index == hidden - 1 ? used : finalSize.Width + 1, 0, label.DesiredSize.Width, finalSize.Height));
        }
        return finalSize;
    }
}
#endif
