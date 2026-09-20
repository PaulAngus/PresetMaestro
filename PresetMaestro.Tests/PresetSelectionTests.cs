using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public class PresetSelectionTests
{
    [Fact]
    public void CatalogIncludesEverySlotInDeviceOrderRegardlessOfCacheOrder()
    {
        var catalog = PresetSelection.CreateCatalog(new Dictionary<int, string> { [511] = "Last", [9] = "Clean", [0] = "First", [999] = "Ignore" });
        Assert.Equal(Enumerable.Range(0, 512), catalog.Select(p => p.Slot));
        Assert.Equal("000  First", catalog[0].Label);
        Assert.Equal("009  Clean", catalog[9].Label);
        Assert.Equal("511  Last", catalog[511].Label);
        Assert.Equal("(name unavailable)", catalog[1].Name);
    }

    [Fact]
    public void CatalogDisplaysAndFiltersUsingTheConfiguredOffsetWhilePreservingDeviceSlots()
    {
        var catalog = PresetSelection.CreateCatalog(new Dictionary<int, string> { [0] = "First", [511] = "Last" }, displayOffset: 1);

        Assert.Equal(0, catalog[0].Slot);
        Assert.Equal(1, catalog[0].DisplayedSlot);
        Assert.Equal("001  First", catalog[0].Label);
        Assert.Equal("512  Last", catalog[511].Label);
        Assert.Equal(0, Assert.Single(PresetSelection.Filter(catalog, "001")).Slot);
        Assert.Equal(511, Assert.Single(PresetSelection.Filter(catalog, "512")).Slot);
    }

    [Fact]
    public void FilteringPreservesDeviceOrderAndMatchesNamesOrPaddedNumbers()
    {
        var catalog = PresetSelection.CreateCatalog(new Dictionary<int, string> { [11] = "Clean B", [2] = "Clean Z", [400] = "CLEAN A" });
        Assert.Equal(new[] { 2, 11, 400 }, PresetSelection.Filter(catalog, " clean ").Select(p => p.Slot));
        Assert.Equal(9, Assert.Single(PresetSelection.Filter(catalog, "009")).Slot);
        Assert.Empty(PresetSelection.Filter(catalog, "no match"));
        Assert.Equal(512, PresetSelection.Filter(catalog, " ").Count);
    }

    [Theory]
    [InlineData(1200, 5)]
    [InlineData(1600, 5)]
    [InlineData(900, 4)]
    [InlineData(700, 3)]
    [InlineData(500, 2)]
    [InlineData(100, 1)]
    public void ColumnCountAdaptsToAvailableWidth(double width, int expected) =>
        Assert.Equal(expected, PresetSelection.ColumnsForWidth(width));

    [Fact]
    public void FullCatalogIsArrangedVerticallyInFiveRequestedRanges()
    {
        var catalog = PresetSelection.CreateCatalog(new Dictionary<int, string>());
        var arranged = PresetSelection.ArrangeColumnMajor(catalog, 5);

        Assert.Equal(104, PresetSelection.RowsForColumnMajorLayout(catalog.Count, 5));
        Assert.Equal(new[] { 0, 104, 208, 312, 416 }, arranged.Take(5).Select(p => p!.Slot));
        Assert.Equal(new[] { 1, 105, 209, 313, 417 }, arranged.Skip(5).Take(5).Select(p => p!.Slot));
        Assert.Equal(Enumerable.Range(0, 104), arranged.Where((_, index) => index % 5 == 0).Select(p => p!.Slot));
        Assert.Equal(512, arranged.OfType<PresetChoice>().Select(p => p.Slot).Distinct().Count());
        Assert.Equal(8, arranged.Count(p => p == null));
        Assert.Equal(511, arranged[479]!.Slot);
    }

    [Fact]
    public void ColumnMajorArrangementHandlesShortFilteredLists()
    {
        var presets = new[] { new PresetChoice(2, "A"), new PresetChoice(9, "B"), new PresetChoice(12, "C") };
        var arranged = PresetSelection.ArrangeColumnMajor(presets, 2);

        Assert.Equal(new[] { 2, 12, 9 }, arranged.OfType<PresetChoice>().Select(p => p.Slot));
        Assert.Null(arranged[^1]);
    }

    [Theory]
    [InlineData(4, PresetNavigation.Right, 5)]
    [InlineData(7, PresetNavigation.Down, 12)]
    [InlineData(7, PresetNavigation.Up, 2)]
    [InlineData(7, PresetNavigation.Home, 5)]
    [InlineData(7, PresetNavigation.End, 9)]
    [InlineData(509, PresetNavigation.Down, 511)]
    [InlineData(511, PresetNavigation.Right, 511)]
    [InlineData(0, PresetNavigation.Left, 0)]
    [InlineData(7, PresetNavigation.First, 0)]
    [InlineData(7, PresetNavigation.Last, 511)]
    [InlineData(7, PresetNavigation.PageDown, 57)]
    public void NavigationUsesRowMajorOrderAndStaysInBounds(int index, PresetNavigation move, int expected) =>
        Assert.Equal(expected, PresetSelection.MoveIndex(index, 512, 5, move, 10));

    [Fact]
    public void NavigationHandlesFilteredAndEmptyResults()
    {
        Assert.Equal(2, PresetSelection.MoveIndex(0, 3, 5, PresetNavigation.Down));
        Assert.Equal(-1, PresetSelection.MoveIndex(-1, 0, 5, PresetNavigation.Right));
    }
}
