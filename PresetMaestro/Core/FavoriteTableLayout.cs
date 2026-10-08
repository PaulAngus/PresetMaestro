namespace PresetMaestro.Core;

internal static class FavoriteTableLayout
{
    public const double Gap = 18;
    public const double MinimumWidth = 440;
    public static int TableCount(double width, double minimumWidth = MinimumWidth) => Math.Max(2, (int)Math.Floor((width + Gap) / (minimumWidth + Gap)));

    public static (int Start, int Count)[] Distribute(int count, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        // The shared distribution also serves single-column details panes.
        // Favorites' two-table minimum belongs in TableCount, not this calculation.
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        var result = new (int, int)[columns];
        int start = 0;
        for (int column = 0; column < columns; column++)
        {
            int length = count / columns + (column < count % columns ? 1 : 0);
            result[column] = (start, length);
            start += length;
        }
        return result;
    }
}
