using System.Text.Json;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class LibraryMatchTests
{
    [Fact]
    public void NameDifferencesListRenamesNewAndClearedSlotsButNotEmptySlots()
    {
        var baseline = Baseline(100);
        var names = Enumerable.Range(0, 512).ToDictionary(i => i, i => i < 100 ? baseline.Presets[i].Name : "<EMPTY>");
        Assert.Empty(LibraryMatch.NameDifferences(baseline, names));
        names[99] = "Renamed";
        Assert.Equal(new[] { 99 }, LibraryMatch.NameDifferences(baseline, names).Select(d => d.Slot));
        names[0] = "<EMPTY>";
        names[511] = "New preset";
        Assert.Equal(new[] { 0, 99, 511 }, LibraryMatch.NameDifferences(baseline, names).Select(d => d.Slot));
        names[510] = ""; // A blank name is not proof of an empty preset.
        Assert.Equal(new[] { 0, 99, 510, 511 }, LibraryMatch.NameDifferences(baseline, names).Select(d => d.Slot));
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
        baseline.FirmwareConfirmation = new("12.00", DateTimeOffset.UtcNow, true, 16, 16);

        var result = LibraryMatch.Compare(baseline, baseline.Presets, true, "Stage", firmware,
            baseline.Presets.Keys.ToArray());

        Assert.Equal(16, result.Matched);
        Assert.Equal(accepted, result.IsConsistent);
        Assert.Equal(!accepted, result.Caution is not null);
    }

    [Fact]
    public void AnySingleDifferenceIsReportedAndEmptySlotsDoNotCount()
    {
        var baseline = Baseline(100);
        var observed = new Dictionary<int, IndexedPreset>(baseline.Presets);
        observed[100] = observed[0] with { Slot = 100, Name = "<EMPTY>", ContentSha256 = "", NameOnlyEmpty = true, Amps = [] };
        var result = LibraryMatch.Compare(baseline, observed, false, "Stage", "12.00");
        Assert.Equal((100, 100), (result.Matched, result.Compared));
        Assert.True(result.IsConsistent);
        Assert.Contains("100 of 100 populated presets match · consistent", result.Describe());
        Assert.DoesNotContain("%", result.Describe());
        Assert.Contains("1 other preset name differs · library match unconfirmed", result.Describe(1));
        observed[99] = observed[99] with { ContentSha256 = "changed" };
        result = LibraryMatch.Compare(baseline, observed, false, "Stage", "12.00");
        Assert.Equal((99, 100), (result.Matched, result.Compared));
        Assert.False(result.IsConsistent);
        Assert.Equal(99, Assert.Single(result.Differences).Slot);
        observed[100] = FractalIndexWorkflowTests.Preset(100);
        result = LibraryMatch.Compare(baseline, observed, false, "Stage", "12.00");
        Assert.Equal(101, result.Compared);
        Assert.False(result.IsConsistent);
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
        Assert.Contains("16 of 16 sampled", result.Describe());
        Assert.True(result.IsConsistent);
        observed.Remove(slots[0]);
        result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", slots);
        Assert.Equal(1, result.Failed);
        Assert.False(result.IsConsistent);
        Assert.Contains("unconfirmed", result.Describe());
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
        Assert.False(result.IsConsistent);
    }

    [Fact]
    public void FullMatchCannotIgnoreAnUnavailablePreviouslyEmptySlot()
    {
        var baseline = Baseline(2);
        var result = LibraryMatch.Compare(baseline, baseline.Presets, false, "Stage", "12.00", [0, 1, 2]);
        Assert.Equal(1, result.Failed);
        Assert.False(result.IsConsistent);
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
        Assert.Equal(result.Compared, result.Matched);
        Assert.False(result.IsConsistent);
        Assert.NotNull(result.Caution);
    }

    [Fact]
    public void NoEvidenceCannotMatchAndClonedContentDoesNotProveDeviceIdentity()
    {
        var empty = Baseline(0);
        Assert.False(LibraryMatch.Compare(empty, empty.Presets, false, "Stage", "12.00").IsConsistent);
        var baseline = Baseline(2);
        var clone = IndexJson.Clone(baseline);
        Assert.True(LibraryMatch.Compare(baseline, clone.Presets, false, "Stage", "12.00").IsConsistent);
        clone.Presets[0] = clone.Presets[0] with { Variant = FractalDeviceVariant.FM3 };
        Assert.Equal(1, LibraryMatch.Compare(baseline, clone.Presets, false, "Stage", "12.00").Matched);
    }

    [Theory]
    [InlineData(95)]
    [InlineData(0)]
    [InlineData(101)]
    public void ProfilesSavedWithARetiredMatchThresholdStillLoadAndDropIt(int threshold)
    {
        var device = new IndexDevice(Guid.NewGuid(), "Stage", FractalDeviceVariant.FM9);
        var json = JsonSerializer.SerializeToElement(new
        {
            SchemaVersion = 1,
            ProfileId = Guid.NewGuid(),
            Devices = new[] { device },
            SelectedDeviceId = device.Id,
            PresetMatchThresholdPercent = threshold,
        });
        var profile = IndexJson.ReadProfile(json);
        Assert.Equal(device.Id, profile.SelectedDeviceId);
        Assert.DoesNotContain("PresetMatchThresholdPercent", IndexJson.ToElement(profile).GetRawText());
    }

    [Fact]
    public void ScansConfirmedWithARetiredThresholdStillLoad()
    {
        var baseline = Baseline(512);
        baseline.Status = "Complete";
        baseline.Firmware = null;
        var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Saved", FractalDeviceVariant.FM9), Committed = baseline };
        var json = JsonSerializer.Serialize(cache, IndexJson.Options).Replace("\"FirmwareConfirmation\": null",
            "\"FirmwareConfirmation\": { \"Firmware\": \"12.00\", \"CheckedAt\": \"2026-10-01T00:00:00+00:00\", \"IsSample\": false, \"Matched\": 507, \"Compared\": 512, \"ThresholdPercent\": 99 }");
        var loaded = JsonSerializer.Deserialize<DeviceIndex>(json, IndexJson.Options)!;
        IndexJson.Validate(loaded);
        Assert.Equal(new ScanFirmwareConfirmation("12.00", loaded.Committed!.FirmwareConfirmation!.CheckedAt, false, 507, 512),
            loaded.Committed.FirmwareConfirmation);
    }

    [Fact]
    public void DifferencesKeepSpecificSettingsFailuresAndMetadataWithoutReportingBypass()
    {
        var baseline = Baseline(3);
        var observed = new Dictionary<int, IndexedPreset>(baseline.Presets);
        var preset = observed[1];
        var amp = preset.Amps[0];
        observed[1] = preset with
        {
            Name = "Renamed",
            ContentSha256 = "changed",
            SceneNames = ["New scene", .. preset.SceneNames.Skip(1)],
            Amps = [amp with { Scenes = [new(2, !amp.Scenes[0].Bypassed), .. amp.Scenes.Skip(1)] }],
        };
        observed.Remove(2);
        var result = LibraryMatch.Compare(baseline, observed, true, "Other", "13.00", [0, 1, 2],
            new Dictionary<int, string> { [2] = "The device query timed out." });
        Assert.Equal(new[] { 1, 2 }, result.Differences.Select(d => d.Slot));
        Assert.Contains(result.Differences[0].Changes, c => c.Setting == "Preset name" && c.Saved == preset.Name && c.Connected == "Renamed");
        Assert.Contains(result.Differences[0].Changes, c => c.Setting == "Scene 1 name" && c.Connected == "New scene");
        Assert.Contains(result.Differences[0].Changes, c => c.Setting == "Scene 1, Amp 1 channel" && c.Connected == "Channel C");
        Assert.DoesNotContain(result.Differences[0].Changes, c => c.Setting.Contains("bypass", StringComparison.OrdinalIgnoreCase) || c.Connected.Contains("bypass", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("The device query timed out.", result.Differences[1].ReadError);
        Assert.Equal(new[] { "Device name", "Firmware" }, result.MetadataDifferences.Select(d => d.Setting));
        Assert.Equal(new[] { 0, 1, 2 }, result.RequestedSlots);
        Assert.True(result.IncludesLegacyFingerprints);
    }

    [Fact]
    public void NewComparisonIgnoresBypassImageChangesButLegacyDifferencesRemainUnconfirmed()
    {
        var baseline = Baseline(1);
        const string fingerprint = "gen3-no-bypass-v1:same-settings";
        baseline.Presets[0] = baseline.Presets[0] with { BypassIgnoredSha256 = fingerprint };
        var preset = baseline.Presets[0];
        var observed = new Dictionary<int, IndexedPreset>
        {
            [0] = preset with
            {
                ContentSha256 = "different-raw-image",
                Amps = [preset.Amps[0] with { Scenes = preset.Amps[0].Scenes.Select(s => s with { Bypassed = !s.Bypassed }).ToArray() }],
            },
        };
        var result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", [0]);
        Assert.True(result.IsConsistent);
        Assert.Empty(result.Differences);
        Assert.False(result.IncludesLegacyFingerprints);
        observed[0] = observed[0] with { BypassIgnoredSha256 = "gen3-no-bypass-v1:changed-parameter" };
        Assert.False(LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", [0]).IsConsistent);
        baseline.Presets[0] = preset with { BypassIgnoredSha256 = null };
        result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", [0]);
        Assert.False(result.IsConsistent);
        Assert.True(result.IncludesLegacyFingerprints);
        Assert.Empty(result.Differences.Single().Changes);
    }

    [Fact]
    public void CheckReportKeepsEvidenceSeparateFromTheBaseline()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-check-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var baseline = Baseline(512);
            baseline.Status = "Complete";
            var cache = new DeviceIndex { Device = new(Guid.NewGuid(), "Saved", FractalDeviceVariant.FM9), Committed = baseline };
            var library = new IndexLibrary(directory); library.Save(cache);
            var observed = new Dictionary<int, IndexedPreset>(baseline.Presets);
            observed[1] = observed[1] with { ContentSha256 = "changed" };
            var result = LibraryMatch.Compare(baseline, observed, true, "Stage", "12.00", [0, 1]);
            new LibraryCheckReportStore(directory).Save(new(cache.Device.Id, baseline.Id, baseline.StartedAt,
                DateTimeOffset.UtcNow, "Stage", "12.00", result, []));
            string report = File.ReadAllText(Path.Combine(directory, "Checks", cache.Device.Id.ToString("N") + ".json"));
            Assert.Contains("changed", report);
            Assert.Contains("RequestedSlots", report);
            Assert.Equal("hash-1", library.Load(cache.Device.Id)!.Committed!.Presets[1].ContentSha256);
            Assert.Single(library.ListDevices());
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}
