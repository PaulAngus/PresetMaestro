using System.Text.Json;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class LibraryMatchTests
{
    [Fact]
    public void NamePrecheckCountsRenamesNewAndClearedSlotsWithoutEmptyInflation()
    {
        var baseline = Baseline(100);
        var names = Enumerable.Range(0, 512).ToDictionary(i => i, i => i < 100 ? baseline.Presets[i].Name : "<EMPTY>");
        Assert.Equal((100, 100), LibraryMatch.CompareNames(baseline, names));
        names[99] = "Renamed";
        Assert.Equal((99, 100), LibraryMatch.CompareNames(baseline, names));
        names[0] = "<EMPTY>";
        names[511] = "New preset";
        Assert.Equal((98, 101), LibraryMatch.CompareNames(baseline, names));
        names[510] = ""; // A blank name is not proof of an empty preset.
        Assert.Equal((98, 102), LibraryMatch.CompareNames(baseline, names));
    }

    private static IndexScan Baseline(int count) => new()
    {
        ConnectedDeviceName = "Stage",
        Firmware = "12.00",
        Presets = Enumerable.Range(0, count).ToDictionary(i => i, i => FractalIndexWorkflowTests.Preset(i)),
    };

    [Theory]
    [InlineData("12.00", true)]
    [InlineData("13.00", false)]
    [InlineData(null, false)]
    public void RepairedLegacyScanStillChecksForLaterSoftwareChanges(string? firmware, bool accepted)
    {
        var baseline = Baseline(16);
        baseline.Firmware = null;
        baseline.FirmwareConfirmation = new("12.00", DateTimeOffset.UtcNow, true, 16, 16, 99);

        var result = LibraryMatch.Compare(baseline, baseline.Presets, true, "Stage", firmware,
            baseline.Presets.Keys.ToArray());

        Assert.Equal(16, result.Matched);
        Assert.Equal(accepted, result.MeetsThreshold(99));
        Assert.Equal(!accepted, result.Caution is not null);
    }

    [Fact]
    public void ExactThresholdUsesPopulatedUnionAndNeverRoundsUp()
    {
        var baseline = Baseline(100);
        var observed = new Dictionary<int, IndexedPreset>(baseline.Presets);
        observed[99] = observed[99] with { ContentSha256 = "changed" };
        observed[100] = observed[0] with { Slot = 100, Name = "<EMPTY>", ContentSha256 = "", NameOnlyEmpty = true, Amps = [] };
        var result = LibraryMatch.Compare(baseline, observed, false, "Stage", "12.00");
        Assert.Equal(99m, result.Percent);
        Assert.True(result.MeetsThreshold(99));
        Assert.False(result.MeetsThreshold(100));
        observed[100] = FractalIndexWorkflowTests.Preset(100);
        result = LibraryMatch.Compare(baseline, observed, false, "Stage", "12.00");
        Assert.Equal(101, result.Compared);
        Assert.False(result.MeetsThreshold(99));
        observed[0] = observed[0] with { Name = "<EMPTY>" };
        Assert.Equal(98, LibraryMatch.Compare(baseline, observed, false, "Stage", "12.00").Matched);
    }

    [Fact]
    public void SamplesChooseSixteenRandomPopulatedSlotsWithoutReplacement()
    {
        var baseline = Baseline(512);
        baseline.Presets[10] = baseline.Presets[10] with { Name = "<EMPTY>" };
        baseline.Presets[11] = baseline.Presets[11] with { ContentSha256 = baseline.Presets[0].ContentSha256 };
        var slots = LibraryMatch.SampleSlots(baseline, random: new Random(123));
        Assert.Equal(16, slots.Length);
        Assert.Equal(16, slots.Distinct().Count());
        Assert.DoesNotContain(10, slots);
        Assert.Equal(slots, LibraryMatch.SampleSlots(baseline, random: new Random(123)));
        Assert.False(slots.SequenceEqual(LibraryMatch.SampleSlots(baseline, random: new Random(456))));
        var observed = slots.ToDictionary(i => i, i => baseline.Presets[i]);
        var result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", slots);
        Assert.Contains("16 of 16 sampled", result.Describe(99));
        Assert.True(result.MeetsThreshold(99));
        observed.Remove(slots[0]);
        result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", slots);
        Assert.Equal(1, result.Failed);
        Assert.False(result.MeetsThreshold(1));
        Assert.Contains("unconfirmed", result.Describe(1));
    }

    [Fact]
    public void SmallSamplesIncludeAllPopulatedSlotsAndSceneChangesPreventAMatch()
    {
        var baseline = Baseline(8);
        baseline.Presets[7] = baseline.Presets[7] with { NameOnlyEmpty = true };
        var slots = LibraryMatch.SampleSlots(baseline);
        Assert.Equal(Enumerable.Range(0, 7), slots.Order());
        var observed = slots.ToDictionary(slot => slot, slot => baseline.Presets[slot]);
        observed[2] = observed[2] with { SceneNames = ["Changed", .. observed[2].SceneNames.Skip(1)] };
        var result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", slots);
        Assert.Equal(6, result.Matched);
        Assert.False(result.MeetsThreshold(99));
    }

    [Fact]
    public void FullMatchCannotIgnoreAnUnavailablePreviouslyEmptySlot()
    {
        var baseline = Baseline(2);
        var result = LibraryMatch.Compare(baseline, baseline.Presets, false, "Stage", "12.00", [0, 1, 2]);
        Assert.Equal(1, result.Failed);
        Assert.False(result.MeetsThreshold(1));
    }

    [Theory]
    [InlineData("Different", "12.00")]
    [InlineData(null, "12.00")]
    [InlineData("Stage", "13.00")]
    [InlineData("Stage", null)]
    public void MetadataDifferencesPreventAutomaticAcceptance(string? name, string? firmware)
    {
        var baseline = Baseline(3);
        var result = LibraryMatch.Compare(baseline, baseline.Presets, false, name, firmware);
        Assert.Equal(100m, result.Percent);
        Assert.False(result.MeetsThreshold(99));
        Assert.NotNull(result.Caution);
    }

    [Fact]
    public void NoEvidenceCannotMatchAndClonedContentDoesNotProveDeviceIdentity()
    {
        var empty = Baseline(0);
        Assert.False(LibraryMatch.Compare(empty, empty.Presets, false, "Stage", "12.00").MeetsThreshold(1));
        var baseline = Baseline(2);
        var clone = IndexJson.Clone(baseline);
        Assert.True(LibraryMatch.Compare(baseline, clone.Presets, false, "Stage", "12.00").MeetsThreshold(100));
        clone.Presets[0] = clone.Presets[0] with { Variant = FractalDeviceVariant.FM3 };
        Assert.Equal(1, LibraryMatch.Compare(baseline, clone.Presets, false, "Stage", "12.00").Matched);
    }

    [Fact]
    public void OldProfilesDefaultTo99AndInvalidThresholdsAreRejected()
    {
        Assert.Equal(99, IndexJson.ReadProfile(JsonSerializer.SerializeToElement(new { SchemaVersion = 1 })).PresetMatchThresholdPercent);
        Assert.Equal(95, IndexJson.ReadProfile(IndexJson.ToElement(new IndexProfile { PresetMatchThresholdPercent = 95 })).PresetMatchThresholdPercent);
        Assert.Throws<InvalidDataException>(() => IndexJson.ToElement(new IndexProfile { PresetMatchThresholdPercent = 0 }));
        Assert.Throws<InvalidDataException>(() => IndexJson.ToElement(new IndexProfile { PresetMatchThresholdPercent = 101 }));
    }
}
