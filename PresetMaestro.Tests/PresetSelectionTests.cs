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
    [InlineData(100, 2)]
    public void ColumnCountAdaptsToAvailableWidth(double width, int expected) =>
        Assert.Equal(expected, PresetSelection.ColumnsForWidth(width));

    [Fact]
    public void FullCatalogIsArrangedInBalancedContiguousColumns()
    {
        var catalog = PresetSelection.CreateCatalog(new Dictionary<int, string>());
        var arranged = PresetSelection.ArrangeColumnMajor(catalog, 5);

        Assert.Equal(103, PresetSelection.RowsForColumnMajorLayout(catalog.Count, 5));
        Assert.Equal(new[] { 0, 103, 206, 308, 410 }, arranged.Take(5).Select(p => p!.Slot));
        Assert.Equal(new[] { 1, 104, 207, 309, 411 }, arranged.Skip(5).Take(5).Select(p => p!.Slot));
        Assert.Equal(Enumerable.Range(0, 103), arranged.Where((_, index) => index % 5 == 0).Select(p => p!.Slot));
        Assert.Equal(512, arranged.OfType<PresetChoice>().Select(p => p.Slot).Distinct().Count());
        Assert.Equal(3, arranged.Count(p => p == null));
        Assert.Equal(511, arranged[509]!.Slot);
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

    [Theory]
    [InlineData(DeviceModel.FM3, 512)]
    [InlineData(DeviceModel.FM9, 512)]
    [InlineData(DeviceModel.AxeFxIII, 1024)]
    public void TrimmingPreservesInternalEmptiesAndRawBoundaries(DeviceModel model, int capacity)
    {
        var names = Enumerable.Range(0, capacity).ToDictionary(i => i, _ => "<EMPTY>");
        names[10] = "First"; names[18] = "Last";
        var full = PresetSelection.CreateCatalog(names, 1, model);
        Assert.Equal(capacity, full.Count);
        var trimmed = PresetSelection.TrimExplicitEmptyEdges(full);
        Assert.Equal(Enumerable.Range(10, 9), trimmed.Select(p => p.Slot));
        Assert.Equal("011  First", trimmed[0].Label);
        Assert.True(trimmed[1].IsExplicitlyEmpty);
        Assert.Equal(11, trimmed[1].Slot);
        Assert.Equal(10, Assert.Single(PresetSelection.Filter(trimmed, "011")).Slot);
        Assert.Empty(PresetSelection.Filter(trimmed, "001"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("(name unavailable)")]
    public void UnknownNamesPreventTrimming(string? unknown)
    {
        var names = Enumerable.Range(0, 512).ToDictionary(i => i, _ => "<EMPTY>");
        names.Remove(0);
        if (unknown != null) { names[0] = unknown; }
        names[511] = "Last";
        Assert.Equal(512, PresetSelection.TrimExplicitEmptyEdges(PresetSelection.CreateCatalog(names)).Count);
        Assert.Equal(512, PresetSelection.TrimExplicitEmptyEdges(PresetSelection.CreateCatalog(new Dictionary<int, string>())).Count);
    }

    [Fact]
    public void AllExplicitEmptyCatalogHasNoDisplayRange()
    {
        var names = Enumerable.Range(0, 1024).ToDictionary(i => i, _ => "<EMPTY>");
        Assert.Empty(PresetSelection.TrimExplicitEmptyEdges(PresetSelection.CreateCatalog(names, deviceModel: DeviceModel.AxeFxIII)));
        names[1023] = "Last";
        var last = Assert.Single(PresetSelection.TrimExplicitEmptyEdges(PresetSelection.CreateCatalog(names, 1, DeviceModel.AxeFxIII)));
        Assert.Equal(1023, last.Slot);
        Assert.Equal("1024  Last", last.Label);
        Assert.Equal(last, Assert.Single(PresetSelection.Filter(new[] { last }, "1024")));
    }

    [Theory]
    [InlineData(0, 225, 5)]
    [InlineData(10, 9, 5)]
    [InlineData(100, 7, 2)]
    [InlineData(10, 9, 2)]
    [InlineData(10, 9, 3)]
    [InlineData(10, 9, 4)]
    [InlineData(10, 2, 5)]
    public void ColumnsAreBalancedContiguousAndPadded(int start, int count, int columns)
    {
        var presets = Enumerable.Range(start, count).Select(i => new PresetChoice(i, "Name")).ToArray();
        var cells = PresetSelection.ArrangeColumnMajor(presets, columns);
        int next = start;
        for (int column = 0; column < columns; column++)
        {
            var entries = cells.Where((_, i) => i % columns == column).ToArray();
            int size = count / columns + (column < count % columns ? 1 : 0);
            Assert.Equal(Enumerable.Range(next, size), entries.Take(size).Select(p => p!.Slot));
            Assert.All(entries.Skip(size), p => Assert.Null(p));
            next += size;
        }
        Assert.Equal(start + count, next);
    }

    [Fact]
    public void ProfileRetainsConfiguredDeviceModel()
    {
        var profile = ProfileSettings.From(new AppSettings { DeviceModel = DeviceModel.AxeFxIII });
        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProfileSettings>(json)!;
        var settings = new AppSettings(); restored.ApplyTo(settings);
        Assert.Equal(DeviceModel.AxeFxIII, settings.DeviceModel);
        Assert.Equal(DeviceModel.FM9, System.Text.Json.JsonSerializer.Deserialize<ProfileSettings>("{}")!.DeviceModel);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SearchResultsRemainOrderedAndBalanced(int columns)
    {
        var names = Enumerable.Range(0, 512).ToDictionary(i => i, _ => "<EMPTY>");
        int[] slots = [10, 12, 15, 16, 20, 30, 40];
        foreach (int slot in slots) { names[slot] = "Match"; }
        var trimmed = PresetSelection.TrimExplicitEmptyEdges(PresetSelection.CreateCatalog(names));
        var filtered = PresetSelection.Filter(trimmed, "Match");
        var cells = PresetSelection.ArrangeColumnMajor(filtered, columns);
        var runs = Enumerable.Range(0, columns).Select(c => cells.Where((_, i) => i % columns == c).OfType<PresetChoice>().ToArray()).ToArray();
        Assert.Equal(slots, runs.SelectMany(run => run).Select(p => p.Slot));
        Assert.True(runs.Max(run => run.Length) - runs.Min(run => run.Length) <= 1);
        Assert.Equal(Enumerable.Range(10, 31), PresetSelection.Filter(trimmed, "").Select(p => p.Slot));
    }
}
