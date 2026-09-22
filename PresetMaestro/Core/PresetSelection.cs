using System.Globalization;

namespace PresetMaestro.Core;

public sealed record PresetChoice(int Slot, string Name, int DisplayOffset = 0)
{
    // Sync stores the device name verbatim; there is no separate empty flag.
    public bool IsExplicitlyEmpty => string.Equals(Name.Trim(), "<EMPTY>", StringComparison.Ordinal);
    public int DisplayedSlot => Slot + DisplayOffset;
    public string Label => $"{DisplayedSlot:000}  {Name}";
}

public enum PresetNavigation { Left, Right, Up, Down, Home, End, First, Last, PageUp, PageDown }

public static class PresetSelection
{
    public static IReadOnlyList<PresetChoice> CreateCatalog(IReadOnlyDictionary<int, string> names, int displayOffset = 0, DeviceModel deviceModel = DeviceModel.FM9) =>
        Enumerable.Range(0, DevicePresets.Capacity(deviceModel)).Select(slot => new PresetChoice(slot,
            names.TryGetValue(slot, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim() : "(name unavailable)", displayOffset)).ToArray();

    // Unknown names are potentially populated; only explicit device markers can be trimmed.
    public static IReadOnlyList<PresetChoice> TrimExplicitEmptyEdges(IReadOnlyList<PresetChoice> catalog)
    {
        int first = 0, last = catalog.Count - 1;
        while (first <= last && catalog[first].IsExplicitlyEmpty) { first++; }
        while (last >= first && catalog[last].IsExplicitlyEmpty) { last--; }
        return catalog.Skip(first).Take(last - first + 1).ToArray();
    }

    public static IReadOnlyList<PresetChoice> Filter(IReadOnlyList<PresetChoice> presets, string? search)
    {
        string query = search?.Trim() ?? "";
        return presets.Where(p => query.Length == 0 ||
            p.DisplayedSlot.ToString("000", CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    // Reserve a readable cell width, with five columns at the reference window size.
    public static int ColumnsForWidth(double width) => double.IsFinite(width)
        ? Math.Clamp((int)(Math.Max(0, width) / 220), 2, 5) : 5;

    public static int RowsForColumnMajorLayout(int count, int columns)
    {
        if (count == 0)
        {
            return 0;
        }

        columns = Math.Max(1, columns);

        return (count + columns - 1) / columns;
    }

    public static IReadOnlyList<PresetChoice?> ArrangeColumnMajor(IReadOnlyList<PresetChoice> presets, int columns)
    {
        columns = Math.Max(1, columns);
        int rows = RowsForColumnMajorLayout(presets.Count, columns);
        if (rows == 0)
        {
            return Array.Empty<PresetChoice?>();
        }

        int shortSize = presets.Count / columns;
        int extra = presets.Count % columns;
        var arranged = new List<PresetChoice?>(rows * columns);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int columnSize = shortSize + (column < extra ? 1 : 0);
                int sourceIndex = column * shortSize + Math.Min(column, extra) + row;
                if (row < columnSize)
                {
                    arranged.Add(presets[sourceIndex]);
                }
                else
                {
                    arranged.Add(null);
                }
            }
        }

        return arranged;
    }

    public static int MoveIndex(int index, int count, int columns, PresetNavigation move, int pageRows = 1)
    {
        if (count == 0)
        {
            return -1;
        }

        columns = Math.Max(1, columns);
        index = Math.Clamp(index, 0, count - 1);
        int target = move switch
        {
            PresetNavigation.Left => index - 1,
            PresetNavigation.Right => index + 1,
            PresetNavigation.Up => index - columns,
            PresetNavigation.Down => index + columns,
            PresetNavigation.Home => index / columns * columns,
            PresetNavigation.End => index / columns * columns + columns - 1,
            PresetNavigation.First => 0,
            PresetNavigation.Last => count - 1,
            PresetNavigation.PageUp => index - columns * Math.Max(1, pageRows),
            PresetNavigation.PageDown => index + columns * Math.Max(1, pageRows),
            _ => index
        };
        return Math.Clamp(target, 0, count - 1);
    }
}
