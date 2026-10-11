using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class SceneSearchTests
{
    [Fact]
    public void SceneNameMatchesDoNotReturnParentPresets()
    {
        var sceneOnly = FractalIndexWorkflowTests.Preset(0) with { Name = "Bassman Tweed", SceneNames = ["Jum scene"], Amps = [] };
        var presetName = sceneOnly with { Slot = 1, Name = "Jumped Plexis", SceneNames = ["Clean"] };

        Assert.False(IndexSearch.Matches(sceneOnly, null, ["jum"], [], true, 0));
        Assert.True(IndexSearch.Matches(presetName, null, ["jum"], [], true, 0));
        var scene = Assert.Single(SceneSearch.Find([sceneOnly, presetName], _ => null, ["jum"], [], true));
        Assert.Same(sceneOnly, scene.Preset);
        Assert.Equal(0, scene.Index);
    }

    [Fact]
    public void NamesAreIndependentOfPresetNamesNumbersAmpsAndTags()
    {
        var preset = FractalIndexWorkflowTests.Preset(1) with { Name = "Clean parent", SceneNames = ["Clean", "Warm CLEAN", "Lead", "", "Rhythm", "Solo", "Clean", "Ambient"] };
        var tags = new PresetAnnotation { Tags = ["Jazz"], SceneTags = { [2] = ["Clean", "Jazz"], [0] = ["Live"] } };
        var results = SceneSearch.Find([preset], _ => tags, ["clean"], [], true);
        Assert.Equal([0, 1, 6], results.Select(s => s.Index));
        foreach (string term in new[] { "parent", "Jazz", "001", "Plexi" })
        { Assert.Empty(SceneSearch.Find([preset], _ => tags, [term], [], true)); }
        Assert.Single(SceneSearch.Find([preset], _ => tags, ["clean", "warm"], [], true));
        Assert.Equal(8, SceneSearch.Find([preset], _ => tags, [], [], true).Length);
        Assert.Equal(3, SceneSearch.Find([preset], _ => tags, [], [], true).Single(s => s.Name == "").Index);
        Assert.False(IndexSearch.Matches(preset, tags, ["Jazz"], [], true, 1));
    }

    [Fact]
    public void PresetTextSearchMatchesOnlyNamesWhileExplicitTagFiltersRemainAvailable()
    {
        var preset = FractalIndexWorkflowTests.Preset(0);
        var tags = new PresetAnnotation { Tags = ["Jazz"], SceneTags = { [0] = ["Live"] } };
        Assert.True(IndexSearch.Matches(preset, tags, ["BASSMAN", "tweed"], [], true, 1));
        foreach (string term in new[] { "Jumped", "Plexi", "141", "001", "Jazz", "Live" })
        { Assert.False(IndexSearch.Matches(preset, tags, [term], [], true, 1)); }
        Assert.True(IndexSearch.Matches(preset, tags, [], ["Jazz", "Live"], true, 1));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    public void TagsMustBelongToTheMatchingScene(bool allTags, int count)
    {
        var preset = FractalIndexWorkflowTests.Preset(0);
        var tags = new PresetAnnotation { Tags = ["Parent"], SceneTags = { [0] = ["Live"], [1] = ["Clean"], [2] = ["Live", "Clean"] } };
        Assert.Equal(count, SceneSearch.Find([preset], _ => tags, [], ["live", "clean"], allTags).Length);
        Assert.Empty(SceneSearch.Find([preset], _ => tags, [], ["Parent"], allTags));
        Assert.Empty(SceneSearch.Find([preset], _ => tags, ["Jumped"], ["Clean"], allTags));
    }

    [Fact]
    public void EmptyMissingAndDuplicateScenesUseOnlySavedEntriesInPresetOrder()
    {
        var first = FractalIndexWorkflowTests.Preset(2) with { SceneNames = ["Clean", "Clean"] };
        var last = FractalIndexWorkflowTests.Preset(3) with { SceneNames = ["Clean"] };
        var empty = FractalIndexWorkflowTests.Preset(0) with { Name = "<EMPTY>", NameOnlyEmpty = true };
        var missing = FractalIndexWorkflowTests.Preset(1) with { SceneNames = [] };
        var results = SceneSearch.Find([last, empty, first, missing], _ => null, ["clean"], [], true);
        Assert.Equal([(2, 0), (2, 1), (3, 0)], results.Select(s => (s.Preset.Slot, s.Index)));
        Assert.Same(first, results[0].Preset);
    }
}
