#if FRACTAL_INDEX
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using PresetMaestro.Core;

namespace PresetMaestro;

// The preset table distribution with fixed-height, virtualized scene rows. One
// ListBox owns selection; only visible rows and the focused row need containers.
internal sealed class SceneTablesPanel : VirtualizingPanel
{
    // Preserve the prior scene-name minimum; reduce the preset-name allocation by 25%.
    internal const double MinimumTableWidth = 534;
    internal const double RowHeight = 26;
    internal int Columns { get; private set; } = 1;
    internal Action<int, double, double[]>? LayoutChanged { get; init; }
    private readonly Dictionary<int, Control> _realized = [];
    private (int Start, int Count)[] _ranges = [(0, 0)];
    private Rect _viewport;
    private double _width, _cell;
    private int _scrollToIndex = -1;
    private bool _inLayout;

    public SceneTablesPanel()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            if (_viewport == e.EffectiveViewport) { return; }
            _viewport = e.EffectiveViewport;
            InvalidateMeasure();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _inLayout = true;
        try
        {
            double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 900;
            Columns = FavoriteTableLayout.TableCount(width, MinimumTableWidth + 2, minimumTables: 1);
            _width = Math.Max(width, Columns * (MinimumTableWidth + 2) + (Columns - 1) * FavoriteTableLayout.Gap);
            _cell = (_width - (Columns - 1) * FavoriteTableLayout.Gap) / Columns;
            _ranges = FavoriteTableLayout.Distribute(Items.Count, Columns);
            double top = Math.Max(0, _viewport.Top);
            double bottom = _viewport.Height > 0 ? _viewport.Bottom : top + 400;
            int firstRow = Math.Max(0, (int)Math.Floor((top - FavoriteTablesPanel.HeadingHeight) / RowHeight) - 1);
            int lastRow = Math.Max(firstRow, (int)Math.Ceiling((bottom - FavoriteTablesPanel.HeadingHeight) / RowHeight) + 1);
            var wanted = new HashSet<int>();
            foreach (var range in _ranges)
            {
                for (int row = firstRow; row <= lastRow && row < range.Count; row++) { wanted.Add(range.Start + row); }
            }
            foreach (var (index, control) in _realized.ToArray())
            {
                if (!wanted.Contains(index) && index != _scrollToIndex && !control.IsKeyboardFocusWithin)
                { Unrealize(index, control); }
            }
            foreach (int index in wanted) { Realize(index); }
            foreach (var control in _realized.Values) { control.Measure(new Size(_cell - 2, RowHeight)); }
            var heights = _ranges.Select(r => FavoriteTablesPanel.HeadingHeight + r.Count * RowHeight + 1).ToArray();
            LayoutChanged?.Invoke(Columns, _width, heights);
            InvalidateArrange();
            return new Size(_width, heights.Max());
        }
        finally { _inLayout = false; }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, control) in _realized) { control.Arrange(ItemBounds(index)); }
        return finalSize;
    }

    private Rect ItemBounds(int index)
    {
        int column = Array.FindIndex(_ranges, r => index >= r.Start && index < r.Start + r.Count);
        return column < 0 ? default : new Rect(column * (_cell + FavoriteTableLayout.Gap) + 1,
            FavoriteTablesPanel.HeadingHeight + (index - _ranges[column].Start) * RowHeight, _cell - 2, RowHeight);
    }

    private Control Realize(int index)
    {
        if (_realized.TryGetValue(index, out var existing)) { return existing; }
        var generator = ItemContainerGenerator!;
        var item = Items[index];
        bool needsContainer = generator.NeedsContainer(item, index, out var key);
        var container = needsContainer ? generator.CreateContainer(item, index, key) : (Control)item!;
        generator.PrepareItemContainer(container, item, index);
        _realized.Add(index, container);
        AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private void Unrealize(int index, Control control)
    {
        ItemContainerGenerator!.ClearItemContainer(control);
        RemoveInternalChild(control);
        _realized.Remove(index);
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        foreach (var (index, control) in _realized.ToArray()) { Unrealize(index, control); }
        InvalidateMeasure();
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        _realized.Clear();
        base.OnItemsControlChanged(oldValue);
    }

    protected override Control? ContainerFromIndex(int index) => _realized.GetValueOrDefault(index);
    protected override int IndexFromContainer(Control container)
    {
        foreach (var (index, control) in _realized) { if (control == container) { return index; } }
        return -1;
    }
    protected override IEnumerable<Control>? GetRealizedContainers() => _realized.OrderBy(p => p.Key).Select(p => p.Value);

    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count || !IsEffectivelyVisible) { return null; }
        if (_inLayout) { return ContainerFromIndex(index); }
        if (!IsMeasureValid) { UpdateLayout(); }
        _scrollToIndex = index;
        try
        {
            var container = Realize(index);
            container.Measure(new Size(_cell - 2, RowHeight));
            container.Arrange(ItemBounds(index));
            container.BringIntoView();
            InvalidateMeasure(); UpdateLayout();
            container.BringIntoView();
            return container;
        }
        finally { _scrollToIndex = -1; }
    }

    internal int NavigationTarget(int index, NavigationDirection direction)
    {
        if (Items.Count == 0) { return -1; }
        if (index < 0) { return direction == NavigationDirection.Last ? Items.Count - 1 : 0; }
        int column = Array.FindIndex(_ranges, r => index >= r.Start && index < r.Start + r.Count);
        if (column < 0) { return index; }
        int row = index - _ranges[column].Start;
        switch (direction)
        {
            case NavigationDirection.Up: return row > 0 ? index - 1 : index;
            case NavigationDirection.Down: return row + 1 < _ranges[column].Count ? index + 1 : index;
            case NavigationDirection.Left:
            case NavigationDirection.Right:
                int next = column + (direction == NavigationDirection.Left ? -1 : 1);
                return next >= 0 && next < Columns && _ranges[next].Count > 0
                    ? _ranges[next].Start + Math.Min(row, _ranges[next].Count - 1) : index;
            case NavigationDirection.First: return 0;
            case NavigationDirection.Last: return Items.Count - 1;
            case NavigationDirection.Next: return Math.Min(index + 1, Items.Count - 1);
            case NavigationDirection.Previous: return Math.Max(index - 1, 0);
            default: return index;
        }
    }

    internal int PageTarget(int index, bool up, double viewportHeight)
    {
        if (index < 0 || index >= Items.Count) { return NavigationTarget(index, NavigationDirection.First); }
        var range = _ranges.First(r => index >= r.Start && index < r.Start + r.Count);
        int step = Math.Max(1, (int)(viewportHeight / RowHeight));
        return Math.Clamp(index + (up ? -step : step), range.Start, range.Start + range.Count - 1);
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
        => ScrollIntoView(NavigationTarget(from is Control control ? IndexFromContainer(control) : -1, direction));
}
#endif
