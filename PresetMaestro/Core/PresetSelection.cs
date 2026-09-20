using System.Globalization;

namespace PresetMaestro.Core;

public sealed record PresetChoice(int Slot, string Name, int DisplayOffset = 0)
{
    public int DisplayedSlot => Slot + DisplayOffset;
    public string Label => $"{DisplayedSlot:000}  {Name}";
}

public enum PresetNavigation { Left, Right, Up, Down, Home, End, First, Last, PageUp, PageDown }

public static class PresetSelection
{
    public static IReadOnlyList<PresetChoice> CreateCatalog(IReadOnlyDictionary<int, string> names, int displayOffset = 0) =>
        Enumerable.Range(0, 512).Select(slot => new PresetChoice(slot,
            names.TryGetValue(slot, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim() : "(name unavailable)", displayOffset)).ToArray();

    public static IReadOnlyList<PresetChoice> Filter(IReadOnlyList<PresetChoice> presets, string? search)
    {
        string query = search?.Trim() ?? "";
        return presets.Where(p => query.Length == 0 ||
            p.DisplayedSlot.ToString("000", CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    // Reserve a readable cell width, with five columns at the reference window size.
    public static int ColumnsForWidth(double width) => double.IsFinite(width)
        ? Math.Clamp((int)(Math.Max(0, width) / 220), 1, 5) : 5;

    public static int RowsForColumnMajorLayout(int count, int columns)
    {
        if (count == 0)
        {
            return 0;
        }

        columns = Math.Max(1, columns);

        // At the normal five-column size, use the requested 104-slot ranges:
        // 000-103, 104-207, 208-311, 312-415, and 416-511.
        if (count == 512 && columns == 5)
        {
            return 104;
        }

        return (count + columns - 1) / columns;
    }

    public static IReadOnlyList<PresetChoice?> ArrangeColumnMajor(IReadOnlyList<PresetChoice> presets, int columns)
    {
        int rows = RowsForColumnMajorLayout(presets.Count, columns);
        if (rows == 0)
        {
            return Array.Empty<PresetChoice?>();
        }

        var arranged = new List<PresetChoice?>(rows * columns);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int sourceIndex = column * rows + row;
                if (sourceIndex < presets.Count)
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
