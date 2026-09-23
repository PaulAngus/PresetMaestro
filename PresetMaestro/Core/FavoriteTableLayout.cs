namespace PresetMaestro.Core;

internal static class FavoriteTableLayout
{
    public const double Gap = 18;
    public const double MinimumWidth = 440;
    public static int TableCount(double width) => Math.Max(2, (int)Math.Floor((width + Gap) / (MinimumWidth + Gap)));

    public static (int Start, int Count)[] Distribute(int count, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 2);
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
