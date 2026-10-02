using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public sealed class FractalIndexWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-index-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    internal static IndexedPreset Preset(int slot, FractalDeviceVariant variant = FractalDeviceVariant.FM9) => new(variant, slot,
        slot == 125 ? "Plexis+ACs" : new[] { "Bassman Tweed", "Brit Lead", "Vintage Crunch", "American Clean", "Stadium Solo" }[slot % 5],
        ["Jumped & 30W TB", "6CA7 & 15WTB", "2204 & Citrus", "100W & D-30", "S1 + OD", "S2 + OD", "S3 + OD", "S4 + OD"],
        [new IndexedAmpBlock(1, [new(0, new(141, "Plexi 50W Jumped", null, "fixture", true)), new(1, new(277, "Plexi 50W 6CA7", null, "fixture", true)), new(2, new(261, "Plexi 2204", null, "fixture", true)), new(3, new(145, "Plexi 100W Jumped", null, "fixture", true))],
            Enumerable.Range(0, 8).Select(i => new AmpSceneState(i % 4, i == 7)).ToArray())], $"hash-{slot}");

    private static DeviceIndex Cache(int count = 512, FractalDeviceVariant variant = FractalDeviceVariant.FM9)
    {
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", variant, "12.00");
        return new DeviceIndex { Device = device, Committed = new IndexScan { Status = "Complete", FinishedAt = DateTimeOffset.UtcNow, Firmware = "12.00", Presets = Enumerable.Range(0, count).ToDictionary(i => i, i => Preset(i, variant)) } };
    }

    [AvaloniaTheory]
    [InlineData(99, false, false, true, false)]
    [InlineData(100, false, false, false, false)]
    [InlineData(100, true, false, true, false)]
    [InlineData(99, false, true, false, false)]
    [InlineData(100, true, false, false, true)]
    public async Task LibraryCheckAndSyncRespectThresholdAndPreserveBaselineUntilConfirmed(int threshold, bool approve, bool renamed, bool publishes, bool disconnect)
    {
        var source = new NameSource(slot => slot < 100 ? "Populated" : "<EMPTY>");
        var reader = new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter());
        var cache = Cache();
        for (int slot = 0; slot < 512; slot++)
        { cache.Committed!.Presets[slot] = await reader.ReadAsync(FractalDeviceDefinition.For(cache.Device.Variant), new Version(12, 0), slot, default); }
        cache.Committed!.ConnectedDeviceName = "Stage";
        Guid baselineId = cache.Committed.Id;
        source.FullReads.Clear();
        var store = new ProfileStore(_directory);
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex")); library.Save(cache);
        var settings = store.LoadSettings(); settings.Theme = "Light";
        settings.MidiInputPort = "FM9"; settings.MidiOutputPort = "FM9";
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id, PresetMatchThresholdPercent = threshold });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        midi.Send = request =>
        {
            if (request[5] == 0) { midi.Reply(FractalDeviceInformationTests.Capture("identity-fm9")); }
            if (request[5] == 1) { midi.Reply(FractalDeviceInformationTests.SyntheticName(renamed ? "Other" : "Stage")); }
        };
        int confirmations = 0;
        MainWindow? window = null;
        window = new MainWindow(settings, [], midi, profileStore: store, confirm: (title, message, _, _) =>
        {
            Assert.Equal("Confirm library update", title);
            Assert.Contains("99 of 100 populated", message);
            Assert.Equal(baselineId, library.Load(cache.Device.Id)!.Committed!.Id);
            confirmations++;
            if (disconnect) { typeof(MainWindow).GetMethod("Disconnect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null); }
            return Task.FromResult(approve);
        })
        { Width = 1440, Height = 850, ThruInputRetryDelay = TimeSpan.Zero };
        typeof(MainWindow).GetField("_fractalIndexReader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, reader);
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); Dispatcher.UIThread.RunJobs();
            await window.ConnectAsync(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(12, source.FullReads.Count);
            Assert.Contains("12 of 12 sampled", Find<TextBlock>(window, "IndexMatchStatus").Text!);
            Assert.Equal(baselineId, library.Load(cache.Device.Id)!.Committed!.Id);
            source.ChangedSlot = 99;
            await window.SyncIndexAsync(); Dispatcher.UIThread.RunJobs();
            var saved = library.Load(cache.Device.Id)!;
            Assert.Equal(publishes, baselineId != saved.Committed!.Id);
            Assert.Equal(threshold == 100 || renamed ? 1 : 0, confirmations);
            Assert.Equal("Complete", saved.LastAttempt!.Status);
            Assert.Contains(disconnect ? "connect the device" : "99 of 100 populated", Find<TextBlock>(window, "IndexMatchStatus").Text!);
            if (publishes) { Assert.Equal("Stage", saved.Committed.ConnectedDeviceName); }
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null && threshold == 99 && !renamed)
            {
                Directory.CreateDirectory(output);
                using var screenshot = window.CaptureRenderedFrame(); screenshot!.Save(Path.Combine(output, "preset-library-match.png"));
                Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
                Find<NumericUpDown>(window, "IndexMatchThreshold").BringIntoView(); Dispatcher.UIThread.RunJobs();
                using var config = window.CaptureRenderedFrame(); config!.Save(Path.Combine(output, "preset-library-match-config.png"));
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task FailedSampleReadsCannotBecomeMatchesAndStopAfterThreeFailures()
    {
        var cache = Cache();
        int attempts = 0;
        var reader = new PresetIndexReader(new Source((_, _) => { attempts++; throw new TimeoutException(); }), AmpModelCatalogRegistry.CreateStarter());
        var result = await LibraryMatch.CheckSampleAsync(reader, cache, cache.Committed!, null, default);
        Assert.Equal(3, attempts);
        Assert.Equal(12, result.Failed);
        Assert.False(result.MeetsThreshold(1));
    }

    [Fact]
    public async Task StagedFirstScanDoesNotEstablishBaselineUntilHostAcceptsIt()
    {
        var cache = new DeviceIndex { Device = new IndexDevice(Guid.NewGuid(), "FM9", FractalDeviceVariant.FM9) };
        var library = new IndexLibrary(_directory);
        var source = new NameSource(slot => slot == 0 ? "Populated" : "<EMPTY>");
        await new IndexScanner(new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()), library)
            .ScanAsync(cache, false, null, default, publish: false, connectedDeviceName: "Stage");
        var saved = library.Load(cache.Device.Id)!;
        Assert.Null(saved.Committed);
        Assert.Equal("Complete", saved.LastAttempt!.Status);
        Assert.Equal("Stage", saved.LastAttempt.ConnectedDeviceName);
        Assert.NotNull(saved.LastAttempt.FinishedAt);
    }

    [Fact]
    public async Task EmptyNameCheckSkipsOnlyExplicitMarkersAndFallsBackForUnknownNames()
    {
        var source = new NameSource(slot => slot switch { 0 => " <EMPTY> ", 1 => "", 2 => null, 3 => "<empty>", 4 => throw new TimeoutException(), _ => "Populated" });
        var reader = new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter());
        var device = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var empty = await reader.ReadAsync(device, null, 0, CancellationToken.None);
        Assert.True(empty.NameOnlyEmpty);
        Assert.Empty(empty.ContentSha256);
        Assert.Empty(source.FullReads);
        for (int slot = 1; slot <= 5; slot++)
        {
            Assert.False((await reader.ReadAsync(device, null, slot, CancellationToken.None)).NameOnlyEmpty);
        }
        Assert.Equal([1, 2, 3, 4, 5], source.FullReads);
        Assert.Throws<InvalidOperationException>(() => new IndexProfile().GetOrCreate(Guid.NewGuid(), empty, null));
    }

    [Fact]
    public async Task ResumeRechecksPreviouslyEmptySlotsAndRetainsCompletedPresetReads()
    {
        var library = new IndexLibrary(_directory);
        var cache = new DeviceIndex { Device = new IndexDevice(Guid.NewGuid(), "FM9", FractalDeviceVariant.FM9) };
        using var cancellation = new CancellationTokenSource();
        var source = new NameSource(slot => slot == 0 ? "<EMPTY>" : "Populated");
        var scanner = new IndexScanner(new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()), library);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cache, false, p =>
        {
            if (p.Read == 2) { cancellation.Cancel(); }
        }, cancellation.Token));
        var checkpoint = library.Load(cache.Device.Id)!;
        Assert.True(checkpoint.LastAttempt!.Presets[0].NameOnlyEmpty);
        var replacement = new NameSource(slot => slot < 2 ? "Now populated" : "<EMPTY>");
        var progress = new List<IndexScanProgress>();
        await new IndexScanner(new PresetIndexReader(replacement, AmpModelCatalogRegistry.CreateStarter()), library)
            .ScanAsync(checkpoint, true, progress.Add, CancellationToken.None);
        Assert.Equal([0], replacement.FullReads);
        Assert.False(checkpoint.Committed!.Presets[0].NameOnlyEmpty);
        Assert.Equal(510, progress[^1].EmptySkipped);
        Assert.Equal(512, progress[^1].Read);
        Assert.Equal(512, library.Load(cache.Device.Id)!.Committed!.Presets.Count);
    }

    [Fact]
    public async Task CancellationCheckpointsAndResumePublishesOnlyACompleteGeneration()
    {
        var library = new IndexLibrary(_directory);
        var cache = new DeviceIndex { Device = new IndexDevice(Guid.NewGuid(), "FM3", FractalDeviceVariant.FM3) };
        using var cancellation = new CancellationTokenSource();
        var source = new Source((slot, _) =>
        {
            if (slot == 2) { cancellation.Cancel(); }
            return Image(slot);
        });
        var scanner = new IndexScanner(new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()), library);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(cache, false, null, cancellation.Token));
        var checkpoint = library.Load(cache.Device.Id)!;
        Assert.Null(checkpoint.Committed);
        Assert.Equal("Cancelled", checkpoint.LastAttempt!.Status);
        Assert.Equal(3, checkpoint.LastAttempt.Presets.Count);
        var readSlots = new List<int>();
        var resumeSource = new Source((slot, _) => { readSlots.Add(slot); return Image(slot); });
        await new IndexScanner(new PresetIndexReader(resumeSource, AmpModelCatalogRegistry.CreateStarter()), library)
            .ScanAsync(checkpoint, true, null, CancellationToken.None);
        var complete = library.Load(cache.Device.Id)!;
        Assert.Equal(512, complete.Committed!.Presets.Count);
        Assert.Equal("Complete", complete.Committed.Status);
        Assert.DoesNotContain(0, readSlots);
        Assert.DoesNotContain(1, readSlots);
        Assert.Equal(509, readSlots.Count);
    }

    [Fact]
    public async Task FailedScanKeepsPreviousCompleteIndexAndStopsAfterThreeFailures()
    {
        var cache = Cache();
        var originalId = cache.Committed!.Id;
        var library = new IndexLibrary(_directory);
        var source = new Source((_, _) => throw new IOException("No device"));
        await Assert.ThrowsAsync<IOException>(() => new IndexScanner(new PresetIndexReader(source, AmpModelCatalogRegistry.CreateStarter()), library)
            .ScanAsync(cache, false, null, CancellationToken.None));
        var saved = library.Load(cache.Device.Id)!;
        Assert.Equal(originalId, saved.Browsable!.Id);
        Assert.Equal(3, saved.LastAttempt!.Errors.Count);
        Assert.Equal("Failed", saved.LastAttempt.Status);
    }

    [Fact]
    public void ChangedMovedAndCopiedPresetsNeverInheritTagsWithoutReview()
    {
        var cache = Cache();
        var profile = new IndexProfile { Devices = [cache.Device] };
        var original = cache.Committed!.Presets[2];
        var annotation = profile.GetOrCreate(cache.Device.Id, original, "12.00");
        annotation.Tags = ["Live"];
        annotation.SceneTags[0] = ["Clean"];
        Assert.Same(annotation, profile.Find(cache.Device.Id, original, "12.00"));
        var copy = original with { Slot = 99 };
        Assert.Null(profile.Find(cache.Device.Id, copy, "12.00"));
        var replaced = original with { ContentSha256 = "replacement" };
        cache.Committed.Presets[2] = replaced;
        cache.Committed.Presets[99] = copy;
        Assert.Null(profile.Find(cache.Device.Id, replaced, "12.00"));
        Assert.Single(profile.Pending(cache));
        profile.Reattach(annotation.Id, copy, "12.00", new Dictionary<int, int> { [0] = 3 });
        Assert.Equal("Live", profile.Find(cache.Device.Id, copy, "12.00")!.Tags.Single());
        Assert.Equal("Clean", profile.Find(cache.Device.Id, copy, "12.00")!.SceneTags[3].Single());
        profile.UndoReview();
        Assert.Null(profile.Find(cache.Device.Id, copy, "12.00"));
        Assert.Single(profile.Pending(cache));
    }

    [Fact]
    public void UndoReassignmentPreservesUnrelatedEditsAndRestoresMergedTags()
    {
        var cache = Cache();
        var profile = new IndexProfile { Devices = [cache.Device] };
        var original = profile.GetOrCreate(cache.Device.Id, cache.Committed!.Presets[2], "12.00");
        original.Tags = ["Live"];
        var destination = profile.GetOrCreate(cache.Device.Id, cache.Committed.Presets[3], "12.00");
        destination.Tags = ["Clean"];
        profile.Reattach(original.Id, cache.Committed.Presets[3], "12.00", new Dictionary<int, int>());
        var other = profile.GetOrCreate(cache.Device.Id, cache.Committed.Presets[4], "12.00");
        other.SceneTags[1] = ["Later edit"];
        profile.UndoReview();
        Assert.Equal(["Live"], profile.Find(cache.Device.Id, cache.Committed.Presets[2], "12.00")!.Tags);
        Assert.Equal(["Clean"], profile.Find(cache.Device.Id, cache.Committed.Presets[3], "12.00")!.Tags);
        Assert.Equal(["Later edit"], profile.Find(cache.Device.Id, cache.Committed.Presets[4], "12.00")!.SceneTags[1]);
        Assert.Empty(profile.ReviewHistory);
    }

    [Fact]
    public void UndoReassignmentCannotOverwriteLaterEditsToAffectedTags()
    {
        var cache = Cache();
        var profile = new IndexProfile { Devices = [cache.Device] };
        var original = profile.GetOrCreate(cache.Device.Id, cache.Committed!.Presets[2], "12.00");
        original.Tags = ["Live"];
        profile.Reattach(original.Id, cache.Committed.Presets[3], "12.00", new Dictionary<int, int>());
        var destination = profile.Find(cache.Device.Id, cache.Committed.Presets[3], "12.00")!;
        destination.Tags.Add("Later edit");
        Assert.Throws<InvalidOperationException>(() => profile.UndoReview());
        Assert.Equal(["Live", "Later edit"], destination.Tags);
        Assert.Single(profile.ReviewHistory);
    }

    [Fact]
    public void TagSearchCombinesPresetAndSceneAssignmentsWithoutSendingMidi()
    {
        var preset = Preset(125);
        var tags = new PresetAnnotation { Tags = ["Live"], SceneTags = new() { [2] = ["Lead"] } };
        Assert.True(IndexSearch.Matches(preset, tags, ["Plexi"], ["Live", "lead"], true, 1));
        Assert.False(IndexSearch.Matches(preset, tags, [], ["Live", "Clean"], true, 1));
        Assert.True(IndexSearch.Matches(preset, tags, ["126"], ["Live", "Clean"], false, 1));
        Assert.False(IndexSearch.Matches(preset, null, [], ["Live"], false, 1));
    }

    [Fact]
    public void ProfileLifecycleAndExportRetainTagsAndOfflineSnapshotsWithIndependentIdentities()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var cache = Cache();
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        var profile = new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id, PresetMatchThresholdPercent = 97 };
        profile.GetOrCreate(cache.Device.Id, cache.Committed!.Presets[125], "12.00").SceneTags[2] = ["Lead"];
        profile.Reattach(profile.Annotations.Single().Id, cache.Committed.Presets[126], "12.00", new Dictionary<int, int> { [2] = 4 });
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        store.Create("Copy", store.LoadProfile("Default"));
        var copied = IndexJson.ReadProfile(store.LoadProfile("Copy").FractalIndex);
        Assert.Equal(97, copied.PresetMatchThresholdPercent);
        Assert.NotEqual(profile.ProfileId, copied.ProfileId);
        Assert.Equal(cache.Device.Id, copied.Devices.Single().Id);
        store.Rename("Copy", "Renamed");
        Assert.Equal(copied.ProfileId, IndexJson.ReadProfile(store.LoadProfile("Renamed").FractalIndex).ProfileId);
        string zip = Path.Combine(_directory, "export.zip");
        store.Export("Renamed", zip);
        using (var archive = ZipFile.OpenRead(zip)) { Assert.Equal(2, archive.Entries.Count); }
        string imported = store.Import(zip);
        var portable = IndexJson.ReadProfile(store.LoadProfile(imported).FractalIndex);
        Assert.Equal(97, portable.PresetMatchThresholdPercent);
        var snapshot = Assert.Single(portable.PortableSnapshots);
        Assert.NotEqual(cache.Device.Id, snapshot.Device.Id);
        Assert.True(snapshot.Imported);
        Assert.Equal(512, snapshot.Committed!.Presets.Count);
        Assert.Equal("Lead", portable.Find(snapshot.Device.Id, snapshot.Committed.Presets[126], "12.00")!.SceneTags[4].Single());
        portable.UndoReview();
        Assert.Equal("Lead", portable.Find(snapshot.Device.Id, snapshot.Committed.Presets[125], "12.00")!.SceneTags[2].Single());
        store.Delete("Renamed");
        Assert.NotNull(new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Load(cache.Device.Id));
        Assert.Contains(Directory.GetFiles(Path.Combine(_directory, "DeletedProfiles"), "*-settings.json", SearchOption.AllDirectories),
            file => IndexJson.ReadProfile(JsonSerializer.Deserialize<ProfileSettings>(File.ReadAllText(file))!.FractalIndex).ProfileId == copied.ProfileId);
    }

    [AvaloniaFact]
    public void IndexUsesFavoritesEmptyEdgeRuleAndKeepsUnnamedAndInternalGaps()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var cache = Cache();
        foreach (int slot in Enumerable.Range(0, 512)) { cache.Committed!.Presets[slot] = Preset(slot) with { Name = "<EMPTY>" }; }
        cache.Committed!.Presets[2] = Preset(2);
        cache.Committed.Presets[5] = Preset(5) with { Name = "" };
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id });
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store);
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); Dispatcher.UIThread.RunJobs();
            var list = Find<ListBox>(window, "IndexPresetList");
            Assert.Equal([2, 3, 4, 5], list.Items.OfType<ListBoxItem>().Select(i => ((IndexedPreset)i.Tag!).Slot));
            Find<TextBox>(window, "IndexSearchInput").Text = "<EMPTY>";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, list.ItemCount);
            Assert.Contains("can take", Find<TextBlock>(window, "IndexSyncNotice").Text!);
            Assert.Equal(14, Find<ProgressBar>(window, "IndexProgress").Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void IndexScreenScalesTo1024PresetsAndKeepsSceneRowsCompact()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = "Light"; settings.DisplayOffset = 1;
        var cache = Cache(1024, FractalDeviceVariant.AxeFxIIIMarkII);
        var example = cache.Committed!.Presets[125];
        var secondAmp = new IndexedAmpBlock(2, [new(0, new(326, "Class-A 30W Brilliant", null, "fixture", true)), new(1, new(6, "Class-A 15W TB", null, "fixture", true)), new(2, new(163, "Citrus A30 Dirty", null, "fixture", true)), new(3, new(271, "Matchbox D-30 EF86", null, "fixture", true))], example.Amps[0].Scenes);
        cache.Committed.Presets[125] = example with { Amps = [.. example.Amps, secondAmp] };
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        var profile = new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id };
        foreach (int slot in Enumerable.Range(0, 1024).Where(i => i % 3 == 0))
        {
            var annotation = profile.GetOrCreate(cache.Device.Id, cache.Committed!.Presets[slot], "12.00");
            annotation.Tags = ["Live"]; annotation.SceneTags[2] = ["Lead"];
        }
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1440, Height = 850 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            Dispatcher.UIThread.RunJobs();
            var list = Find<ListBox>(window, "IndexPresetList");
            Assert.Equal(1024, list.ItemCount);
            list.SelectedIndex = 125;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(8, Find<Grid>(window, "IndexScenes").Children.Count);
            Assert.All(Find<Grid>(window, "IndexScenes").Children, row => Assert.Equal(32, row.Bounds.Height));
            var sceneSection = Find<StackPanel>(window, "IndexSceneSection");
            var ampSection = Find<StackPanel>(window, "IndexAmpSection");
            Assert.Equal(sceneSection.Bounds.Y, ampSection.Bounds.Y);
            Assert.True(ampSection.Bounds.X >= sceneSection.Bounds.Right);
            Assert.Equal(152, Find<Grid>(window, "IndexAmpChannels").Bounds.Height);
            Assert.Equal("+ scene tag", Find<Button>(window, "IndexScene3Tags").Content);
            var search = Find<TextBox>(window, "IndexSearchInput");
            search.Text = "Live";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(342, list.ItemCount);
            Click(Find<Button>(window, "IndexClearSearch"));
            Assert.Equal(1024, list.ItemCount);
            Assert.Empty(midi.Sent);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                list.SelectedIndex = 125; Dispatcher.UIThread.RunJobs();
                using var amps = window.CaptureRenderedFrame(); amps!.Save(Path.Combine(output, "preset-index-together.png"));
                list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
                using var singleAmp = window.CaptureRenderedFrame(); singleAmp!.Save(Path.Combine(output, "preset-index-together-single-amp.png"));
            }
            list.SelectedIndex = 125;
            window.Width = 1000; window.Height = 680; Dispatcher.UIThread.RunJobs();
            sceneSection = Find<StackPanel>(window, "IndexSceneSection");
            ampSection = Find<StackPanel>(window, "IndexAmpSection");
            Assert.Equal(sceneSection.Bounds.Y, ampSection.Bounds.Y);
            Assert.True(ampSection.Bounds.X >= sceneSection.Bounds.Right);
            Assert.True(Find<ListBox>(window, "IndexPresetList").Bounds.Height >= 100);
            Assert.Equal(256, Find<Grid>(window, "IndexScenes").Bounds.Height);
            Assert.All(Find<Grid>(window, "IndexScenes").Children, row => Assert.Equal(32, row.Bounds.Height));
            if (output is not null)
            {
                using var narrow = window.CaptureRenderedFrame(); narrow!.Save(Path.Combine(output, "preset-index-narrow.png"));
            }
            window.Width = 1440; Dispatcher.UIThread.RunJobs();
            Assert.Equal(128, Find<Grid>(window, "IndexScenes").Bounds.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ProfileHasOneAssignedLibraryAndHeaderSwitchRestoresAnotherProfilesAssignment()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings();
        var first = Cache();
        var second = Cache(); second.Device = second.Device with { Name = "Backup FM9" };
        foreach (int slot in Enumerable.Range(4, 508)) { first.Committed!.Presets[slot] = first.Committed.Presets[slot] with { Name = "<EMPTY>" }; }
        foreach (int slot in Enumerable.Range(3, 509)) { second.Committed!.Presets[slot] = second.Committed.Presets[slot] with { Name = "<EMPTY>" }; }
        var otherModel = Cache(512, FractalDeviceVariant.FM3);
        var library = new IndexLibrary(Path.Combine(_directory, "FractalIndex"));
        foreach (var cache in new[] { first, second, otherModel }) { library.Save(cache); }
        var profile = new IndexProfile { Devices = [first.Device, second.Device], SelectedDeviceId = first.Device.Id };
        profile.GetOrCreate(first.Device.Id, first.Committed!.Presets[0], "12.00").Tags = ["Live"];
        settings.FractalIndex = IndexJson.ToElement(profile); store.SaveSettings(settings);
        store.Create("Second", new ProfileSettings { FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [first.Device], SelectedDeviceId = first.Device.Id }) });
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store, confirm: (_, _, _, _) => Task.FromResult(true));
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(), c => c.Name == "IndexDeviceSelector");
            Assert.Equal(4, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
            var choices = Find<ComboBox>(window, "IndexAvailableDevices");
            var threshold = Find<NumericUpDown>(window, "IndexMatchThreshold");
            Assert.Equal(99m, threshold.Value);
            threshold.Value = 95;
            Assert.Equal(95, IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).PresetMatchThresholdPercent);
            choices.SelectedItem = choices.Items.OfType<IndexDevice>().Single(d => d.Id == second.Device.Id);
            Dispatcher.UIThread.RunJobs();
            var assigned = IndexJson.ReadProfile(settings.FractalIndex);
            Assert.Equal(second.Device.Id, assigned.AssignedDevice!.Id);
            Assert.Equal("Live", assigned.Find(first.Device.Id, first.Committed.Presets[0], "12.00")!.Tags.Single());
            Assert.Null(assigned.Find(second.Device.Id, second.Committed!.Presets[0], "12.00"));
            choices.SelectedItem = choices.Items.OfType<IndexDevice>().Single(d => d.Id == otherModel.Device.Id);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(second.Device.Id, IndexJson.ReadProfile(settings.FractalIndex).AssignedDevice!.Id);
            Assert.Contains("configured for FM9", Find<TextBlock>(window, "IndexDeviceStatus").Text!);
            Click(Find<Button>(window, "NavPresetIndex")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Equal("Library: Backup FM9", Find<TextBlock>(window, "IndexAssignedDevice").Text);

            var selector = Find<Button>(window, "HeaderProfileSelector");
            var menu = Assert.IsType<MenuFlyout>(selector.Flyout);
            menu.ShowAt(selector); Dispatcher.UIThread.RunJobs();
            menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Second")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", settings.ActiveProfile);
            Assert.Equal(first.Device.Id, IndexJson.ReadProfile(settings.FractalIndex).AssignedDevice!.Id);
            Assert.Equal(99, IndexJson.ReadProfile(settings.FractalIndex).PresetMatchThresholdPercent);
            Assert.Equal(4, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Empty(IndexJson.ReadProfile(settings.FractalIndex).Annotations);
            Assert.Equal(second.Device.Id, IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex).AssignedDevice!.Id);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void LegacyLibraryReferencesRetainTagsAndRequireAnUnambiguousAssignment()
    {
        var first = Cache(1); var second = Cache(1);
        var profile = new IndexProfile { Devices = [first.Device, second.Device], SelectedDeviceId = second.Device.Id };
        profile.GetOrCreate(first.Device.Id, first.Committed!.Presets[0], "12.00").Tags = ["Keep"];
        var loaded = IndexJson.ReadProfile(IndexJson.ToElement(profile));
        Assert.Equal(second.Device.Id, loaded.AssignedDevice!.Id);
        loaded.AssignDevice(first.Device);
        var roundTrip = IndexJson.ReadProfile(IndexJson.ToElement(loaded));
        Assert.Equal(first.Device.Id, roundTrip.AssignedDevice!.Id);
        Assert.Equal("Keep", roundTrip.Annotations.Single().Tags.Single());
        Assert.Equal(2, roundTrip.Devices.Count);
        profile.SelectedDeviceId = null;
        Assert.Null(IndexJson.ReadProfile(IndexJson.ToElement(profile)).AssignedDevice);
        profile.Devices = [first.Device];
        Assert.Equal(first.Device.Id, IndexJson.ReadProfile(IndexJson.ToElement(profile)).AssignedDevice!.Id);
    }

    [AvaloniaFact]
    public void SceneTagEditorPersistsAndProfileSwitchChangesOnlyAnnotations()
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = "Light";
        var cache = Cache();
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id });
        store.SaveSettings(settings);
        store.Create("Second", new ProfileSettings { FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }) });
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store, confirm: (_, _, _, _) => Task.FromResult(true));
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex")); Dispatcher.UIThread.RunJobs();
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 125; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexScene3Tags")); Dispatcher.UIThread.RunJobs();
            var editor = Assert.Single(window.OwnedWindows);
            Find<AutoCompleteBox>(editor, "IndexTagEditorInput").Text = "Solo tone";
            Click(Find<Button>(editor, "IndexSaveTags")); Dispatcher.UIThread.RunJobs();
            var saved = IndexJson.ReadProfile(store.LoadProfile("Default").FractalIndex);
            Assert.Equal("Solo tone", saved.Find(cache.Device.Id, cache.Committed!.Presets[125], "12.00")!.SceneTags[2].Single());
            Find<TextBox>(window, "IndexSearchInput").Text = "Solo tone"; Dispatcher.UIThread.RunJobs();
            Assert.Single(Find<ListBox>(window, "IndexPresetList").Items);
            Click(Find<Button>(window, "NavConfig")); Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "ProfileSelector").SelectedItem = "Second"; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavPresetIndex")); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", settings.ActiveProfile);
            Assert.Empty(IndexJson.ReadProfile(settings.FractalIndex).Annotations);
            Click(Find<Button>(window, "IndexClearSearch")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(512, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Empty(midi.Sent);
            Assert.False(Find<Button>(window, "IndexResume").IsVisible);
            Assert.DoesNotContain("unfinished", Find<TextBlock>(window, "IndexStatus").Text!);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void UnsupportedOrMalformedIndexDataIsPreservedAndRejected()
    {
        var profile = new IndexProfile { SchemaVersion = 20 };
        Assert.Throws<InvalidDataException>(() => IndexJson.ReadProfile(JsonSerializer.SerializeToElement(profile)));
        var cache = Cache();
        cache.Committed!.Presets[0] = cache.Committed.Presets[0] with { SceneNames = [] };
        var library = new IndexLibrary(_directory);
        Assert.Throws<InvalidDataException>(() => library.Save(cache));
        Assert.False(Directory.Exists(_directory));
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static StoredPresetImage Image(int slot)
    {
        byte[] body = new byte[0x1c4];
        byte[] raw = new byte[16384]; BinaryPrimitives.WriteInt32LittleEndian(raw, slot);
        return new StoredPresetImage(new PresetScenes(slot, "Fixture " + slot, Enumerable.Repeat("Scene", 8).ToArray()), raw, body);
    }
    private sealed class Source(Func<int, FractalDeviceDefinition, StoredPresetImage> read) : IStoredPresetImageSource
    {
        public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => Task.FromResult(read(slot, device));
    }

    private sealed class NameSource(Func<int, string?> name) : IStoredPresetImageSource, IStoredPresetNameSource
    {
        public List<int> FullReads { get; } = [];
        public int? ChangedSlot { get; set; }
        public Task<string?> ReadStoredPresetNameAsync(int slot, FractalDeviceDefinition device, CancellationToken token) => Task.FromResult(name(slot));
        public Task<StoredPresetImage> ReadStoredImageAsync(int slot, FractalDeviceDefinition device, CancellationToken token)
        {
            FullReads.Add(slot);
            var image = Image(slot) with { Body = new byte[0x254] };
            if (ChangedSlot == slot) { image.RawImage[8] = 1; }
            return Task.FromResult(image);
        }
    }
}
