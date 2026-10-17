using Avalonia.Headless.XUnit;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class SceneTagContinuityTests
{
    private static (IndexProfile Profile, DeviceIndex Cache, List<Favorite> Favorites) Fixture()
    {
        var device = new IndexDevice(Guid.NewGuid(), "FM9", FractalDeviceVariant.FM9, "12.00");
        var preset = FractalIndexWorkflowTests.Preset(9) with
        {
            Name = "Plexi 100W",
            SceneNames = ["High", "Jump", "1970", "1959SLP Treble", "High Dry", "Jump Dry", "1970 Dry", "1959SLP Dry"],
            BypassIgnoredSha256 = "gen3-no-bypass-v1:old-settings",
        };
        var cache = new DeviceIndex
        {
            Device = device,
            Committed = new IndexScan
            {
                Status = "Complete",
                FinishedAt = DateTimeOffset.UtcNow,
                Firmware = "12.00",
                Presets = new() { [9] = preset },
            },
        };
        var profile = new IndexProfile { Devices = [device], SelectedDeviceId = device.Id };
        var annotation = profile.GetOrCreate(device.Id, preset, "12.00");
        annotation.Tags = ["Live"]; annotation.SceneTags[2] = ["Crunch"];
        var favorites = new List<Favorite>
        {
            new() { Id = 1, Slot = 1, Name = "Solo", Preset = 10, Scene = 3 },
            new() { Id = 2, Slot = 2, Name = "Encore", Preset = 10, Scene = 3 },
        };
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        return (profile, cache, favorites);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OrdinaryPresetEditsKeepLegacyAndCurrentSharedTagsAndIds(bool legacy, bool defaultSceneNames)
    {
        var (profile, cache, favorites) = Fixture();
        var annotation = profile.Annotations.Single();
        if (legacy) { annotation.BypassIgnoredSha256 = null; }
        if (defaultSceneNames)
        {
            var names = Enumerable.Range(1, 8).Select(i => $"Scene {i}").ToArray();
            annotation.SceneNames = names;
            cache.Committed!.Presets[9] = cache.Committed.Presets[9] with { SceneNames = names };
        }
        cache.Committed!.Presets[9] = cache.Committed.Presets[9] with
        { ContentSha256 = "new-image", BypassIgnoredSha256 = "gen3-no-bypass-v1:new-settings" };
        // Reproduce the projection already having been cleared by an older build.
        foreach (var favorite in favorites) { favorite.Tags.Clear(); }
        Assert.True(SharedSceneTags.Reconcile(profile, cache, favorites, 1));
        Assert.Empty(profile.Pending(cache));
        Assert.Same(annotation, profile.Annotations.Single());
        Assert.Equal("new-image", annotation.ContentSha256);
        Assert.Equal(["Live"], annotation.Tags);
        Assert.All(favorites, f => { Assert.Equal(["Crunch"], f.Tags); Assert.Equal(annotation.Id, f.SceneTagSourceId); });
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, 1));

        // Edits and deletions continue to flow both ways after recovery and reload.
        profile = IndexJson.Clone(profile); favorites = IndexJson.Clone(favorites);
        var target = SharedSceneTags.Resolve(profile, cache, 1, 10, 3)!;
        SharedSceneTags.Set(profile, target, ["Lead"]);
        SharedSceneTags.Reconcile(profile, cache, favorites, 1, importLegacy: false);
        Assert.All(favorites, f => Assert.Equal(["Lead"], f.Tags));
        SharedSceneTags.Set(profile, target, []);
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.All(favorites, f => Assert.Empty(f.Tags));
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("13.00")]
    public void FirmwareMetadataDoesNotDetachIdenticalContent(string? firmware)
    {
        var (profile, cache, favorites) = Fixture();
        cache.Committed!.Firmware = firmware;
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.Empty(profile.Pending(cache));
        Assert.Equal(firmware, profile.Annotations.Single().Firmware);
        Assert.All(favorites, f => Assert.Equal(["Crunch"], f.Tags));
    }

    [Fact]
    public void BypassOnlyChangesKeepTagsEvenWithGenericSceneNames()
    {
        var (profile, cache, favorites) = Fixture();
        profile.Annotations.Single().SceneNames = Enumerable.Repeat("Scene", 8).ToArray();
        cache.Committed!.Presets[9] = cache.Committed.Presets[9] with
        { ContentSha256 = "bypass-changed", SceneNames = Enumerable.Repeat("Scene", 8).ToArray() };
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.Empty(profile.Pending(cache));
        Assert.All(favorites, f => Assert.Equal(["Crunch"], f.Tags));
    }

    [Theory]
    [InlineData("scene-rename")]
    [InlineData("preset-rename")]
    [InlineData("other-block-parameters")]
    [InlineData("add-block")]
    [InlineData("remove-blocks")]
    [InlineData("replace-block")]
    [InlineData("reorder-scenes")]
    [InlineData("legacy-missing-scene-names")]
    public void RenamesAndAnySoundEditsRetainTagsAtTheirPresetAndSceneNumbers(string change)
    {
        var (profile, cache, favorites) = Fixture();
        var annotation = profile.Annotations.Single();
        var current = cache.Committed!.Presets[9] with
        { ContentSha256 = "edited-image", BypassIgnoredSha256 = "gen3-no-bypass-v1:edited-settings" };
        switch (change)
        {
            case "scene-rename":
                var names = current.SceneNames.ToArray(); names[2] = "Solo";
                current = current with { SceneNames = names }; break;
            case "preset-rename": current = current with { Name = "My edited Plexi" }; break;
            // Non-indexed effect parameters are represented by the changed full
            // image/comparison fingerprints; they do not appear in IndexedPreset.
            case "other-block-parameters": break;
            case "add-block": current = current with { Amps = [.. current.Amps, current.Amps[0] with { BlockNumber = 2 }] }; break;
            case "remove-blocks": current = current with { Amps = [] }; break;
            case "replace-block":
                var amp = current.Amps[0];
                var channels = amp.Channels.Select(c => c with { Model = c.Model with { Id = 999, DisplayName = "Different amp" } }).ToArray();
                current = current with { Amps = [amp with { Channels = channels }] }; break;
            case "reorder-scenes": current = current with { SceneNames = current.SceneNames.Reverse().ToArray() }; break;
            case "legacy-missing-scene-names": annotation.SceneNames = []; break;
        }
        cache.Committed.Presets[9] = current;
        Assert.True(SharedSceneTags.Reconcile(profile, cache, favorites, 1));
        Assert.Empty(profile.Pending(cache));
        Assert.Same(annotation, profile.Annotations.Single());
        Assert.Equal(current.Name, annotation.Name);
        Assert.Equal(current.SceneNames, annotation.SceneNames);
        Assert.Equal(["Live"], annotation.Tags);
        Assert.Equal(["Crunch"], annotation.SceneTags[2]);
        Assert.All(favorites, f => { Assert.Equal(["Crunch"], f.Tags); Assert.Equal(annotation.Id, f.SceneTagSourceId); });
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, 1));

        profile = IndexJson.Clone(profile); favorites = IndexJson.Clone(favorites);
        var target = SharedSceneTags.Resolve(profile, cache, 1, 10, 3)!;
        SharedSceneTags.Set(profile, target, ["Updated description"]);
        SharedSceneTags.Reconcile(profile, cache, favorites, 1, importLegacy: false);
        Assert.All(favorites, f => Assert.Equal(["Updated description"], f.Tags));
        SharedSceneTags.Set(profile, target, []);
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.All(favorites, f => Assert.Empty(f.Tags));
        Assert.False(SharedSceneTags.Reconcile(profile, cache, favorites, 1));
    }

    [Theory]
    [InlineData("cleared")]
    [InlineData("moved")]
    [InlineData("other-device")]
    public void ClearedSlotsMovesAndOtherDevicesDoNotInheritTags(string change)
    {
        var (profile, cache, favorites) = Fixture();
        var current = cache.Committed!.Presets[9] with
        { ContentSha256 = "new-image", BypassIgnoredSha256 = "gen3-no-bypass-v1:new-settings" };
        switch (change)
        {
            case "cleared": current = current with { Name = "<EMPTY>", NameOnlyEmpty = true, ContentSha256 = "" }; break;
            case "moved":
                cache.Committed.Presets.Clear(); current = current with { Slot = 10 }; break;
            case "other-device":
                cache.Device = cache.Device with { Id = Guid.NewGuid() }; profile.AssignDevice(cache.Device); break;
        }
        cache.Committed.Presets[current.Slot] = current;
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.All(favorites, f => Assert.Empty(f.Tags));
        Assert.Equal(["Crunch"], profile.Annotations.Single().SceneTags[2]);
        if (change != "other-device") { Assert.Single(profile.Pending(cache)); }
    }

    [Fact]
    public void CompetingHistoricalBindingsNeedReviewInsteadOfPickingTheFirst()
    {
        var (profile, cache, favorites) = Fixture();
        var duplicate = IndexJson.Clone(profile.Annotations.Single());
        duplicate.Id = Guid.NewGuid(); duplicate.ContentSha256 = "older-image";
        duplicate.BypassIgnoredSha256 = null; duplicate.SceneTags[2] = ["Conflicting tag"];
        profile.Annotations.Add(duplicate);
        cache.Committed!.Presets[9] = cache.Committed.Presets[9] with
        { ContentSha256 = "new-image", BypassIgnoredSha256 = "gen3-no-bypass-v1:new-settings" };
        SharedSceneTags.Reconcile(profile, cache, favorites, 1);
        Assert.Equal(2, profile.Pending(cache).Count());
        Assert.All(favorites, f => Assert.Empty(f.Tags));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupRepairsAndPersistsExistingBindingsWithoutTagReattachmentOrMidi(bool renameScene)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-tag-continuity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (profile, cache, favorites) = Fixture();
            profile.Annotations.Single().BypassIgnoredSha256 = null;
            cache.Committed!.Presets[9] = cache.Committed.Presets[9] with
            { ContentSha256 = "new-image", BypassIgnoredSha256 = "gen3-no-bypass-v1:new-settings" };
            if (renameScene)
            {
                var names = cache.Committed.Presets[9].SceneNames.ToArray(); names[2] = "Solo";
                cache.Committed.Presets[9] = cache.Committed.Presets[9] with { SceneNames = names };
            }
            foreach (var favorite in favorites) { favorite.Tags.Clear(); }
            var store = new ProfileStore(directory); var settings = store.LoadSettings();
            settings.DisplayOffset = 1; settings.FractalIndex = IndexJson.ToElement(profile);
            store.SaveSettingsAndFavorites(settings, favorites);
            foreach (int slot in Enumerable.Range(0, 512).Where(slot => slot != 9))
            { cache.Committed.Presets[slot] = FractalIndexWorkflowTests.Preset(slot); }
            new IndexLibrary(Path.Combine(directory, "FractalIndex")).Save(cache);
            var midi = new DeviceMidi();
            var window = new MainWindow(settings, [], midi, profileStore: store);
            try
            {
                var saved = IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex);
                Assert.Empty(saved.Pending(cache));
                Assert.Equal(profile.Annotations.Single().Id, saved.Annotations.Single().Id);
                Assert.Equal(cache.Committed.Presets[9].SceneNames, saved.Annotations.Single().SceneNames);
                Assert.All(store.LoadFavorites("Default"), f => Assert.Equal(["Crunch"], f.Tags));
                Assert.Empty(midi.Sent);
            }
            finally { window.Close(); }
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}
