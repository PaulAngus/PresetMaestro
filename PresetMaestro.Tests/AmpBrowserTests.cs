using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class AmpBrowserTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-amps-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    private static IndexedPreset Preset(int slot, params int[] ids) => new(FractalDeviceVariant.FM9, slot, "Amp test " + slot,
        Enumerable.Range(1, 8).Select(i => "Scene " + i).ToArray(), ids.Length == 0 ? [] :
        [new(1, ids.Select((id, channel) => new IndexedAmpChannel(channel, new(id, "Unknown Amp model #" + id, null, "fixture", false))).ToArray(),
            Enumerable.Range(0, 8).Select(i => new AmpSceneState(0, true)).ToArray())], "hash" + slot);

    private static DeviceIndex Cache(bool complete = true)
    {
        var presets = Enumerable.Range(0, complete ? 512 : 5).ToDictionary(i => i, i => Preset(i));
        presets[0] = Preset(0, 0, 282, 290, 141);
        presets[1] = Preset(1, 0, 0, 0, 0);
        presets[2] = Preset(2, 9999, 9999, 9999, 9999);
        var scan = new IndexScan { Firmware = "12.00", Status = complete ? "Complete" : "Cancelled", FinishedAt = DateTimeOffset.UtcNow, Presets = presets };
        return new() { Device = new(Guid.NewGuid(), "Stage FM9", FractalDeviceVariant.FM9, "12.00"), Committed = complete ? scan : null, LastAttempt = complete ? null : scan };
    }

    [Fact]
    public void FamilyUsageCountsEachPresetOnceAcrossVariantsChannelsAndBlocks()
    {
        var cache = Cache(false);
        var family = AmpBrowserCatalog.Build(cache).Families.Single(f => f.Family.Id == "fender-bassman-59");
        int[] ids = family.Variants.Select(v => v.ModelId).ToArray();
        var preset = Preset(20, ids[0], ids[1], ids[2], ids[0]);
        preset = preset with { Amps = [preset.Amps[0], preset.Amps[0] with { BlockNumber = 2 }] };
        cache.LastAttempt!.Presets = new() { [20] = preset, [21] = Preset(21, ids[1], ids[1], ids[1], ids[1]) };
        Assert.Equal(2, AmpBrowserCatalog.Build(cache).Families.Single(f => f.Family.Id == family.Family.Id).MatchingPresets);
    }

    [Fact]
    public void DirectoryIncludesUnusedModelsCountsDistinctPresetsAndKeepsUnknownIds()
    {
        var directory = AmpBrowserCatalog.Build(Cache());
        Assert.True(directory.IsComplete);
        Assert.Contains("336 device-confirmed amp names", directory.Coverage);
        Assert.Equal(337, directory.Families.Sum(f => f.Variants.Length)); // 336 names plus the fixture's unknown ID.
        var bassman = directory.Families.Single(f => f.Family.Id == "fender-bassman-59");
        Assert.Equal(3, bassman.Variants.Length);
        Assert.Equal(2, bassman.MatchingPresets);
        Assert.Equal("2 presets", bassman.UsageLabel);
        Assert.True(bassman.Matches("Bassguy")); Assert.True(bassman.Matches("Fender")); Assert.True(bassman.Matches("5F6-A"));
        Assert.Equal("0 presets", directory.Families.Single(f => f.Family.Id == "marshall-jcm800").UsageLabel);
        Assert.Contains(directory.Families, f => f.Family.Manufacturer == "Fractal originals");
        var unknown = directory.Families.Single(f => f.Variants.Any(v => v.ModelId == 9999));
        Assert.Equal("Unmapped", unknown.Family.Manufacturer); Assert.Equal("unmapped", unknown.Family.Confidence);
        Assert.Contains(directory.Families.SelectMany(f => f.Variants), v => v.ModelId == 271 && v.IdentityVerified);
        // Matching a saved channel doesn't imply any scene selects or engages it.
        var filter = new AmpContainsFilter(Cache().Device.Id, FractalDeviceVariant.FM9, "12.00", "Plexi", [141]);
        Assert.True(filter.Matches(Cache().Committed!.Presets[0]));
        Assert.False(Cache().Committed!.Presets[0].Uses(0, 141));
    }

    [Fact]
    public void ResearchedAttributionsPreserveHardwareRevisionsAndUncertainty()
    {
        var directory = AmpBrowserCatalog.Build(Cache());
        var families = directory.Families;
        BrowserAmpFamily For(int id) => families.Single(f => f.Variants.Any(v => v.ModelId == id));
        Assert.Equal("bludotone-ojai", For(124).Family.Id);
        Assert.Contains("Ojai", For(124).Variants.Single(v => v.ModelId == 124).SpecificModel);
        Assert.NotEqual(For(148).Family.Id, For(25).Family.Id); // 2-channel vs 3-channel Rectifier.
        Assert.NotEqual(For(14).Family.Id, For(261).Family.Id); // JCM800 vs JMP Plexi topology.
        Assert.NotEqual(For(81).Family.Id, For(213).Family.Id); // 100W vs 50W EVH.
        Assert.NotEqual(For(81).Family.Id, For(313).Family.Id); // Stealth reference.
        Assert.NotEqual(For(76).Family.Id, For(249).Family.Id); // Legacy VL100 vs Legacy 3.
        Assert.NotEqual(For(153).Family.Id, For(258).Family.Id); // Champ vs EC Vibro-Champ.
        Assert.Equal(For(39).Family.Id, For(298).Family.Id); // Official Block family attribution takes precedence.
        Assert.Equal("5150 Block Letter", For(298).Family.Name);
        Assert.Contains("Rhythm channel", For(298).Variants.Single(v => v.ModelId == 298).SpecificModel);
        Assert.Contains("Axe-Fx-II-Owners-Manual.pdf", For(298).Variants.Single(v => v.ModelId == 298).MappingEvidence);
        Assert.True(For(298).Variants.Single(v => v.ModelId == 298).IdentityVerified);
        Assert.Equal("B-15 Portaflex", For(263).Family.Name);
        var portaflex = For(263).Variants.Single();
        Assert.Equal("Ampeg B-15R Portaflex reissue (inferred)", portaflex.SpecificModel);
        Assert.True(For(263).Matches("B15"));
        Assert.True(For(263).Matches("B-15"));
        Assert.True(For(263).Matches("B-15R"));
        Assert.Contains("not explicitly named by Fractal", portaflex.MappingEvidence);
        Assert.Contains("Ares12.05.pdf", portaflex.MappingEvidence);
        Assert.True(portaflex.IdentityVerified); // Device confirms the name; B-15R reference remains inferred.
        Assert.Equal(For(166).Family.Id, For(271).Family.Id); // DC-30 preamp channels.
        Assert.Equal(For(303).Family.Id, For(307).Family.Id); // REVV channels, one reference amp.
        Assert.Equal("Fender", For(111).Family.Manufacturer); // Physical custom reference.
        Assert.Equal("Fractal originals", For(136).Family.Manufacturer); // Virtual Thordendal.
        Assert.All(families.SelectMany(f => f.Variants).Where(v => v.MappingEvidence.Contains("Manufacturer/model sources")),
            v => { Assert.Contains("ampdex", v.MappingEvidence); Assert.Contains("wiki.fractalaudio.com", v.MappingEvidence); });
        Assert.True(For(303).Variants.Single(v => v.ModelId == 303).IdentityVerified);
        Assert.Equal("mesa-markiv", For(23).Family.Id);
        Assert.Contains("Lead channel", For(23).Variants.Single(v => v.ModelId == 23).SpecificModel);
        Assert.Contains("probably Rev B", For(23).Variants.Single(v => v.ModelId == 23).MappingEvidence);
        Assert.Equal("matchless-chieftain", For(61).Family.Id);
        Assert.Equal(For(61).Family.Id, For(62).Family.Id);
        Assert.Contains("normal/unboosted", For(61).Variants.Single(v => v.ModelId == 61).SpecificModel);
        Assert.Contains("Chieftain - boosted", For(62).Variants.Single(v => v.ModelId == 62).SpecificModel);
        Assert.Equal("fuchs-ods", For(118).Family.Id);
        Assert.Contains("Mid-boost", For(118).Variants.Single(v => v.ModelId == 118).SpecificModel);
        Assert.Contains("Mid is not treated as an alias for Deep", For(118).Variants.Single(v => v.ModelId == 118).MappingEvidence);
        Assert.Equal("fender-vibroverb-custom", For(219).Family.Id);
        Assert.NotEqual(For(181).Family.Id, For(219).Family.Id);
        Assert.NotEqual(For(182).Family.Id, For(219).Family.Id);
        Assert.Contains("normal-channel preamp triode removed", For(219).Variants.Single().MappingEvidence);
        var princetone = For(130).Variants.Single();
        Assert.Contains("AA1164-family circuit (inferred)", princetone.SpecificModel);
        Assert.Contains("Wiki instead says AA964", princetone.MappingEvidence);
        Assert.Contains("#102", For(52).Variants.Single().MappingEvidence);
        Assert.Contains("HRM serial 0213", For(52).Variants.Single().MappingEvidence);
        Assert.Contains("High input", For(314).Variants.Single(v => v.ModelId == 314).SpecificModel);
        Assert.Contains("Low input", For(319).Variants.Single(v => v.ModelId == 319).SpecificModel);
        Assert.Contains("2555, 100W", For(103).Variants.Single().SpecificModel);
        Assert.Contains("1963 brownface Vibrolux", For(121).Variants.Single().SpecificModel);
        Assert.All(new[] { 23, 61, 62, 118, 219, 233, 287, 288, 335 }, id =>
            Assert.True(For(id).Variants.Single(v => v.ModelId == id).IdentityVerified));
        Assert.Equal("vox-ac30", For(233).Family.Id);
        Assert.Contains("non-Top-Boost", For(233).Variants.Single(v => v.ModelId == 233).SpecificModel);
        Assert.Equal("friedman-be-2010", For(287).Family.Id);
        Assert.Equal(For(287).Family.Id, For(288).Family.Id);
        Assert.NotEqual(For(37).Family.Id, For(287).Family.Id); // Later BE-100 stays separate from Marsha.
        Assert.NotEqual(For(0).Family.Id, For(302).Family.Id); // Original Bassman stays separate from reissue.
        Assert.Equal("fender-bassman-59-ri", For(302).Family.Id);
        Assert.Equal("Deluxe Tweed Bright", For(283).Variants.Single(v => v.ModelId == 283).Name);
        Assert.Equal(For(283).Family.Id, For(331).Family.Id);
        Assert.Equal(For(39).Family.Id, For(332).Family.Id);
        Assert.Equal(For(119).Family.Id, For(335).Family.Id);
        Assert.NotEqual(For(248).Family.Id, For(21).Family.Id); // IIC+ and custom IIC++ are distinct.
        Assert.DoesNotContain(families, f => f.Family.Manufacturer == "Unmapped" && f.Variants.Any(v => v.ModelId != 9999));
    }

    [Fact]
    public void AmpUsageRequiresSavedPresetsFromCompatibleFirmware()
    {
        var cache = Cache();
        Assert.True(AmpBrowserCatalog.Build(cache).HasUsageData);
        Assert.True(AmpBrowserCatalog.Build(cache).HasCompleteUsageData);
        Assert.True(AmpBrowserCatalog.Build(cache).CanListUnusedAmps);
        Assert.True(AmpBrowserCatalog.Build(Cache(false)).HasUsageData);
        Assert.False(AmpBrowserCatalog.Build(Cache(false)).HasCompleteUsageData);
        Assert.False(AmpBrowserCatalog.Build(Cache(false)).CanListUnusedAmps);
        cache.Committed = null;
        Assert.False(AmpBrowserCatalog.Build(cache).HasUsageData);
        Assert.NotEmpty(AmpBrowserCatalog.Build(cache).Families);
        cache = Cache();
        cache.Committed!.Firmware = "11.00";
        Assert.False(AmpBrowserCatalog.Build(cache).HasUsageData);
        cache.Committed.Firmware = "12.00";
        cache.Committed.Presets.Clear();
        Assert.False(AmpBrowserCatalog.Build(cache).HasUsageData);
    }

    [Fact]
    public void ObservedModelsAloneCannotSupplyAnUnusedAmpCatalogueForOtherDevices()
    {
        var cache = Cache();
        cache.Device = cache.Device with { Variant = FractalDeviceVariant.AxeFxIIIOriginal };
        foreach (int slot in cache.Committed!.Presets.Keys.ToArray())
        { cache.Committed.Presets[slot] = cache.Committed.Presets[slot] with { Variant = FractalDeviceVariant.AxeFxIIIOriginal }; }
        IndexJson.Validate(cache);
        var directory = AmpBrowserCatalog.Build(cache);
        Assert.True(directory.HasCompleteUsageData);
        Assert.False(directory.HasCatalogueRoster);
        Assert.False(directory.CanListUnusedAmps);
        Assert.All(directory.Families, f => Assert.True(f.MatchingPresets > 0));
    }

    [Fact]
    public void LegacyScanConfirmationSurvivesExportAndDoesNotAllowUsageAfterSoftwareChanges()
    {
        var cache = Cache();
        cache.Committed!.Firmware = null;
        Assert.False(AmpBrowserCatalog.Build(cache).HasUsageData);
        cache.Committed.FirmwareConfirmation = new("12.00", DateTimeOffset.UtcNow, true, 16, 16, 99);
        new IndexLibrary(_directory).Save(cache);
        var reopened = new IndexLibrary(_directory).Load(cache.Device.Id)!;
        var portable = IndexJson.Clone(reopened);
        IndexJson.Validate(portable);
        Assert.Null(portable.Committed!.Firmware);
        Assert.Equal("12.00", portable.Committed.EffectiveFirmware);
        Assert.True(AmpBrowserCatalog.Build(portable).HasUsageData);
        var filter = new AmpContainsFilter(cache.Device.Id, cache.Device.Variant, "12.00", "Plexi", [141]);
        Assert.True(filter.AppliesTo(portable));
        portable.Device = portable.Device with { Firmware = "13.00" };
        Assert.False(AmpBrowserCatalog.Build(portable).HasUsageData);
        Assert.False(filter.AppliesTo(portable));
    }

    [Theory]
    [InlineData("not a version", 16, 16, 99)]
    [InlineData("12.00", 15, 16, 99)]
    [InlineData("12.00", 17, 16, 99)]
    [InlineData("12.00", 0, 0, 99)]
    [InlineData("12.00", 16, 16, 101)]
    [InlineData("13.00", 16, 16, 99)]
    public void InvalidCompatibilityConfirmationCannotBeLoaded(string firmware, int matched, int compared, int threshold)
    {
        var cache = Cache();
        cache.Committed!.FirmwareConfirmation = new(firmware, DateTimeOffset.UtcNow, true, matched, compared, threshold);
        Assert.Throws<InvalidDataException>(() => IndexJson.Validate(cache));
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void UnusedFilterShowsUnrepresentedFamiliesAndCombinesWithSearchAndManufacturer(string theme)
    {
        var cache = Cache();
        Assert.False(cache.Committed!.Presets[0].Uses(0, 141)); // Saved Amp 1/D is not selected by any scene.
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.Theme = theme;
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1200, Height = 850 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            var usage = Find<ComboBox>(window, "AmpsPresetUsage");
            Assert.Equal(new[] { "All amps", "Used in my presets", "Not used in my presets" }, usage.Items.OfType<ComboBoxItem>().Select(i => (string)i.Content!));
            Assert.True(UsageOption(window, 2).IsEnabled);
            var list = Find<ListBox>(window, "AmpsDirectory");
            int allCount = list.ItemCount;
            usage.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            var unused = list.Items.OfType<ListBoxItem>().Select(i => (BrowserAmpFamily)i.Tag!).ToArray();
            Assert.Equal(allCount - 3, unused.Length); // Bassman, Plexi and unknown #9999 occur in saved channels.
            Assert.DoesNotContain(unused, f => f.Family.Id == "fender-bassman-59" || f.Variants.Any(v => v.ModelId is 141 or 9999));
            Assert.Contains(unused, f => f.Family.Id == "marshall-jcm800");
            Assert.Contains("Not used in my presets", Find<TextBlock>(window, "AmpsCount").Text);
            Capture(window, "amps-unused-" + theme.ToLowerInvariant());
            Find<ComboBox>(window, "AmpsManufacturer").SelectedItem = "Marshall"; Dispatcher.UIThread.RunJobs();
            Assert.All(list.Items.OfType<ListBoxItem>(), i => Assert.Equal("Marshall", ((BrowserAmpFamily)i.Tag!).Family.Manufacturer));
            int manufacturerCount = list.ItemCount;
            Find<TextBox>(window, "AmpsSearch").Text = "JCM800"; Dispatcher.UIThread.RunJobs();
            Assert.InRange(list.ItemCount, 1, manufacturerCount - 1);
            Assert.Contains(list.Items.OfType<ListBoxItem>(), i => ((BrowserAmpFamily)i.Tag!).Family.Id == "marshall-jcm800");
            SelectFamily(list, "marshall-jcm800");
            Click(Find<Button>(window, "AmpsFindPresets"));
            Assert.Empty(Find<ListBox>(window, "IndexPresetList").Items);
            Click(Find<Button>(window, "IndexBackToAmps"));
            Assert.Equal(2, Find<ComboBox>(window, "AmpsPresetUsage").SelectedIndex);
            Assert.Equal("JCM800", Find<TextBox>(window, "AmpsSearch").Text);
            Find<TextBox>(window, "AmpsSearch").Text = "No such amplifier"; Dispatcher.UIThread.RunJobs();
            Assert.Empty(list.Items);
            Assert.Contains("No unused amps match", Find<TextBlock>(window, "AmpsEmpty").Text);
            Click(Find<Button>(window, "AmpsClearSearch"));
            usage.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Single(list.Items); // The Plexi family used on a saved channel still appears.
            usage.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "AmpsManufacturer").SelectedItem = "All manufacturers"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(allCount, list.ItemCount);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PartialLibraryAllowsUsedAmpsButExplainsWhyUnusedAmpsNeedACompleteSync()
    {
        var cache = Cache(false);
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1000, Height = 800 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            Assert.True(UsageOption(window, 1).IsEnabled);
            Assert.False(UsageOption(window, 2).IsEnabled);
            var notice = Find<TextBlock>(window, "AmpsUnusedNotice");
            Assert.True(notice.IsEffectivelyVisible);
            Assert.Contains("complete a library sync in Preset Index", notice.Text);
            Find<ComboBox>(window, "AmpsPresetUsage").SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, Find<ListBox>(window, "AmpsDirectory").ItemCount);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithoutCompatibleAmpDataShowsCatalogueAndExplainsRequiredSync(bool staleFirmware)
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        var cache = Cache();
        if (staleFirmware) { cache.Committed!.Firmware = "11.00"; }
        else { cache.Committed = null; }
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1200, Height = 800 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            Assert.False(UsageOption(window, 1).IsEnabled);
            Assert.False(UsageOption(window, 2).IsEnabled);
            Assert.Equal(0, Find<ComboBox>(window, "AmpsPresetUsage").SelectedIndex);
            Assert.True(Find<ListBox>(window, "AmpsDirectory").ItemCount > 80);
            Assert.True(Find<StackPanel>(window, "AmpsFirmwareNotice").IsVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains(staleFirmware ? "refresh the saved amp data" : "Preset-name sync updates names only") == true);
            SelectFamily(Find<ListBox>(window, "AmpsDirectory"), "fender-bassman-59");
            Assert.False(Find<Button>(window, "AmpsFindPresets").IsEnabled);
            Capture(window, staleFirmware ? "amps-stale-usage" : "amps-no-usage");
            Click(Find<Button>(window, "AmpsOpenPresetIndex"));
            Assert.True(Find<ListBox>(window, "IndexPresetList").IsEffectivelyVisible);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void MissingFirmwareShowsReferenceCatalogWithoutReinterpretingSavedIds()
    {
        var cache = Cache();
        cache.Device = cache.Device with { Firmware = null };
        cache.Committed!.Firmware = null;
        var directory = AmpBrowserCatalog.Build(cache);
        Assert.True(directory.IsReferencePreview);
        Assert.Contains(directory.Families, f => f.Family.Manufacturer == "Fender");
        Assert.All(directory.Families, f =>
        {
            Assert.False(f.UsageComplete);
            Assert.Equal(0, f.MatchingPresets);
            Assert.Equal("usage unknown", f.UsageLabel);
        });
        Assert.All(directory.Families.SelectMany(f => f.Variants), v => Assert.False(v.IdentityVerified));
        Assert.Null(cache.Device.Firmware); Assert.Null(cache.Committed.Firmware);
        var preset = cache.Committed.Presets[0];
        Assert.Same(preset, AmpBrowserCatalog.ResolveForDisplay(preset, null));
        Assert.False(new AmpContainsFilter(cache.Device.Id, cache.Device.Variant, null, "Preview", [0]).AppliesTo(cache));
        cache.Device = cache.Device with { Variant = FractalDeviceVariant.FM3 };
        Assert.False(AmpBrowserCatalog.Build(cache).IsReferencePreview);
        Assert.All(AmpBrowserCatalog.Build(cache).Families, f => Assert.Equal("Unmapped", f.Family.Manufacturer));
    }

    [AvaloniaFact]
    public void MissingFirmwareExplainsDetectionAndDisablesPresetMatchingWithoutMidi()
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings();
        var cache = Cache(); cache.Device = cache.Device with { Firmware = null }; cache.Committed!.Firmware = null;
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1200, Height = 800 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            Assert.True(Find<StackPanel>(window, "AmpsFirmwareNotice").IsVisible);
            Assert.False(UsageOption(window, 1).IsEnabled);
            Assert.False(UsageOption(window, 2).IsEnabled);
            Assert.Contains("Reference catalog", Find<TextBlock>(window, "AmpsCount").Text);
            Capture(window, "amps-missing-firmware");
            SelectFamily(Find<ListBox>(window, "AmpsDirectory"), "fender-bassman-59");
            Assert.False(Find<Button>(window, "AmpsFindPresets").IsEnabled);
            Click(Find<Button>(window, "NavConfig"));
            Click(Find<Button>(window, "ManageLibraries"));
            Assert.Equal("Not detected", Find<TextBlock>(window, "IndexFirmware").Text);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), c => c.Name == "IndexFirmware");
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void PartialScanAndFirmwareChangeCannotClaimZeroUsageOrLeakModelIdentities()
    {
        var cache = Cache(false);
        var partial = AmpBrowserCatalog.Build(cache);
        Assert.Equal("usage unknown", partial.Families.Single(f => f.Family.Id == "marshall-jcm800").UsageLabel);
        Assert.Contains("usage unknown", partial.Families.Single(f => f.Family.Id == "fender-bassman-59").UsageLabel);
        cache.Device = cache.Device with { Firmware = "13.00" };
        var changed = AmpBrowserCatalog.Build(cache);
        Assert.Empty(changed.Families); Assert.Contains("differs", changed.Coverage);
        foreach (var variant in new[] { FractalDeviceVariant.FM3, FractalDeviceVariant.AxeFxIIIOriginal, FractalDeviceVariant.AxeFxIIIMarkII })
        {
            cache = Cache(); cache.Device = cache.Device with { Variant = variant };
            cache.Committed!.Presets = cache.Committed.Presets.ToDictionary(p => p.Key, p => p.Value with { Variant = variant });
            var directory = AmpBrowserCatalog.Build(cache);
            Assert.All(directory.Families, f => Assert.Equal("Unmapped", f.Family.Manufacturer));
            Assert.DoesNotContain(directory.Families.SelectMany(f => f.Variants), v => v.Name == "59 Bassguy Bright");
        }
    }

    [Fact]
    public void ReferencesValidateDeduplicateAndTravelWithProfileExports()
    {
        var references = new AmpReferenceStore(Path.Combine(_directory, "amp-references.json"));
        var reference = new AmpReference("fender-bassman-59", AmpBrowserCatalog.Wiki + "#Bassguy");
        references.Merge([reference, reference]); Assert.Single(references.Load());
        foreach (string url in new[] { "file:///C:/file", "javascript:alert(1)", "https://wiki.fractalaudio.com.evil.test/wiki/index.php", "https://user@wiki.fractalaudio.com/wiki/index.php", "http://wiki.fractalaudio.com/wiki/index.php" })
        { Assert.Throws<ArgumentException>(() => AmpReferenceStore.NormalizeUrl(url)); }
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); var cache = Cache();
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        string zip = Path.Combine(_directory, "amps.zip"); store.Export("Default", zip);
        string imported = store.Import(zip);
        var profile = IndexJson.ReadProfile(store.LoadProfile(imported).FractalIndex);
        Assert.Equal(reference, Assert.Single(profile.PortableAmpReferences));
        Assert.NotEqual(cache.Device.Id, profile.SelectedDeviceId);
    }

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    public void BrowseFamilyVariantContainsBackAndZeroMatchesWithoutMidi(int width, string theme)
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.Theme = theme;
        new AmpReferenceStore(Path.Combine(_directory, "amp-references.json")).Merge([
            new("carolann-tucana", AmpBrowserCatalog.Wiki + "#CAROL-ANN_TUCANA_CLEAN_(Carol-Ann_Tucana_3)")]);
        var cache = Cache(); new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = 800 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            var list = Find<ListBox>(window, "AmpsDirectory");
            Assert.True(list.ItemCount > 80);
            var headings = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "AmpManufacturerHeading").ToArray();
            var items = list.Items.OfType<ListBoxItem>().ToArray();
            Assert.Equal(items.Select(i => ((BrowserAmpFamily)i.Tag!).Family.Manufacturer).Distinct().Count(), headings.Length);
            Assert.All(headings, heading => Assert.Equal(16, heading.FontSize));
            Assert.All(list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "AmpFamilyName"), text => Assert.Equal(window.FontSize, text.FontSize));
            var bassmanRow = items.Single(i => ((BrowserAmpFamily)i.Tag!).Family.Id == "fender-bassman-59");
            var bassmanLabels = bassmanRow.GetVisualDescendants().OfType<TextBlock>().ToArray();
            var realFamily = bassmanLabels.Single(t => t.Name == "AmpFamilyName");
            var fractalModels = bassmanLabels.Single(t => t.Name == "AmpFamilyFractalModels");
            Assert.Equal("'59 Bassman", realFamily.Text);
            Assert.Contains("59 Bassguy Bright", fractalModels.Text);
            Assert.Contains("59 Bassguy Jumped", fractalModels.Text);
            Assert.Contains("59 Bassguy Normal", fractalModels.Text);
            Assert.True(realFamily.FontSize > fractalModels.FontSize);
            Assert.Equal(TextWrapping.Wrap, fractalModels.TextWrapping);
            foreach (var group in items.GroupBy(i => ((BrowserAmpFamily)i.Tag!).Family.Manufacturer))
            {
                Assert.Single(group.Select(i => i.Bounds.X).Distinct());
                var rows = group.ToArray();
                for (int i = 1; i < rows.Length; i++) { Assert.True(rows[i].Bounds.Y >= rows[i - 1].Bounds.Bottom); }
            }
            var panes = items.GroupBy(i => ((BrowserAmpFamily)i.Tag!).Family.Manufacturer)
                .Select(g => new { X = g.First().Bounds.X, Top = g.First().Bounds.Y, Bottom = g.Last().Bounds.Bottom }).ToArray();
            foreach (var column in panes.GroupBy(p => p.X))
            {
                var stacked = column.OrderBy(p => p.Top).ToArray();
                for (int i = 1; i < stacked.Length; i++) { Assert.Equal(14, stacked[i].Top - stacked[i - 1].Bottom, 1); }
            }
            Capture(window, $"amps-directory-{width}-{theme}");
            SelectFamily(list, "cameron-atomica");
            Capture(window, $"amps-inspector-{width}-{theme}");
            Click(Find<Button>(window, "AmpsBackToDirectory"));
            SelectFamily(list, "carolann-tucana");
            Assert.Equal("Wiki", Find<TextBlock>(window, "AmpsWikiTitle").Text);
            var wikiLinks = Find<StackPanel>(window, "AmpsWikiLinks").Children.OfType<HyperlinkButton>().Where(b => b.Name == "AmpsWikiLink").ToArray();
            Assert.Equal(new[]
            {
                AmpBrowserCatalog.Wiki + "#CAROL-ANN_TUCANA_CLEAN_(Carol-Ann_Tucana_3)",
                AmpBrowserCatalog.Wiki + "#CAROL-ANN_TUCANA_LEAD",
            }, wikiLinks.Select(b => Uri.UnescapeDataString(((AmpReference)b.Tag!).Url)));
            Assert.All(wikiLinks, link => Assert.True(link.IsEffectivelyVisible));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "AmpsEvidence" or "AmpsCatalogueStatus" or "AmpsReferences");
            Assert.False(Find<TextBlock>(window, "AmpsCoverage").IsEffectivelyVisible);
            Capture(window, $"amps-tucana-{width}-{theme}");
            Click(Find<Button>(window, "AmpsBackToDirectory"));
            var first = Find<Button>(window, "NavPresetSender"); var last = Find<Button>(window, "NavConfig");
            Assert.True(first.TranslatePoint(default, window)!.Value.X > 150);
            Assert.True(last.TranslatePoint(default, window)!.Value.X + last.Bounds.Width < Find<TextBlock>(window, "IndexAssignedDevice").TranslatePoint(default, window)!.Value.X);
            Find<TextBox>(window, "AmpsSearch").Text = "Bassguy"; Dispatcher.UIThread.RunJobs();
            SelectFamily(list, "fender-bassman-59");
            Assert.All(VariantChecks(window), check => Assert.True(check.IsChecked));
            foreach (var check in VariantChecks(window))
            {
                var variant = ((BrowserAmpFamily)bassmanRow.Tag!).Variants.Single(v => v.Id == (string)check.Tag!);
                var labels = check.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var realAmp = labels.Single(t => t.Name == "AmpsVariantRealAmp");
                var fractalModel = labels.Single(t => t.Name == "AmpsVariantFractalModel");
                Assert.Equal(variant.SpecificModel, realAmp.Text);
                Assert.Equal("Fractal: " + variant.Name, fractalModel.Text);
                Assert.True(realAmp.FontSize > fractalModel.FontSize);
                Assert.Equal(TextWrapping.Wrap, fractalModel.TextWrapping);
            }
            Assert.True(list.IsEffectivelyVisible);
            Assert.True(Find<StackPanel>(window, "AmpsDetail").TranslatePoint(default, window)!.Value.Y >= list.TranslatePoint(default, window)!.Value.Y + list.Bounds.Height);
            Capture(window, $"amps-family-{width}-{theme}");
            Click(Find<Button>(window, "NavPresetIndex"));
            Find<TextBox>(window, "IndexSearchInput").Text = "no matching preset";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Find<ListBox>(window, "IndexPresetList").Items);
            Click(Find<Button>(window, "NavAmps"));
            Click(Find<Button>(window, "AmpsFindPresets"));
            Assert.Equal(2, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Contains("Fender", Find<TextBlock>(window, "IndexPageTitle").Text!);
            Assert.False(Find<TextBox>(window, "IndexSearchInput").IsEffectivelyVisible);
            Assert.False(Find<Button>(window, "IndexClearSearch").IsEffectivelyVisible);
            Assert.False(Find<Button>(window, "IndexSync").IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name is "IndexAmpSceneUsage" or "IndexClearAmpFilter" or "IndexAmpFilterLabel");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Name == "IndexAmpResultSummary" && t.Text == "Amp 1 · 59 Bassguy Bright · channels A–D");
            Assert.True(Find<ListBox>(window, "IndexPresetList").IsKeyboardFocusWithin);
            Capture(window, $"amps-selected-results-{width}-{theme}");
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.Contains("Amp 1 / B", Find<TextBlock>(window, "IndexAmpMatches").Text!);
            Capture(window, $"amps-contains-{width}-{theme}");
            Click(Find<Button>(window, "IndexBackToAmps"));
            Assert.Equal("Bassguy", Find<TextBox>(window, "AmpsSearch").Text);
            Assert.True(Find<Button>(window, "AmpsFindPresets").IsFocused);
            Click(Find<Button>(window, "NavPresetIndex"));
            Assert.True(Find<TextBox>(window, "IndexSearchInput").IsEffectivelyVisible);
            Assert.Equal("no matching preset", Find<TextBox>(window, "IndexSearchInput").Text);
            Assert.Empty(Find<ListBox>(window, "IndexPresetList").Items);
            Click(Find<Button>(window, "IndexClearSearch"));
            Assert.Equal(512, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Click(Find<Button>(window, "NavAmps"));
            var variants = VariantChecks(window);
            foreach (var check in variants) { check.IsChecked = false; }
            Assert.False(Find<Button>(window, "AmpsFindPresets").IsEnabled);
            variants[1].IsChecked = true;
            Click(Find<Button>(window, "AmpsFindPresets"));
            Assert.Single(Find<ListBox>(window, "IndexPresetList").Items);
            Click(Find<Button>(window, "IndexBackToAmps"));
            Assert.Single(VariantChecks(window), c => c.IsChecked == true);
            Assert.True(VariantChecks(window)[1].IsChecked);
            Click(Find<Button>(window, "AmpsBackToDirectory"));
            Click(Find<Button>(window, "AmpsClearSearch"));
            SelectFamily(Find<ListBox>(window, "AmpsDirectory"), "marshall-jcm800");
            Click(Find<Button>(window, "AmpsFindPresets"));
            Assert.Empty(Find<ListBox>(window, "IndexPresetList").Items);
            Assert.Contains("No presets", Find<TextBlock>(window, "IndexEmptyState").Text!);
            Click(Find<Button>(window, "NavPresetIndex"));
            Assert.Equal(512, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BrowserKeepsScrollAndReferencesAndDoesNotAcceptPresetSendShortcuts()
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.KeyboardEntryEnabled = true;
        new AmpReferenceStore(Path.Combine(_directory, "amp-references.json")).Merge([
            new("fender-bassman-59", AmpBrowserCatalog.Wiki + "#Bassguy")]);
        var cache = Cache(); new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id }); store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = 1000, Height = 800 };
        window.Show();
        try
        {
            // A pre-existing command must not be sent by Enter while browsing amps.
            window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
            Click(Find<Button>(window, "NavAmps"));
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Empty(midi.Sent);
            var list = Find<ListBox>(window, "AmpsDirectory");
            list.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.True(list.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "AmpsDetail" && c.IsEffectivelyVisible);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, 450); Dispatcher.UIThread.RunJobs();
            double offset = scroll.Offset.Y;
            Assert.True(offset > 0);
            SelectFamily(list, "fender-bassman-59");
            Click(Find<Button>(window, "AmpsFindPresets"));
            Click(Find<Button>(window, "IndexBackToAmps"));
            Assert.Empty(window.OwnedWindows);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "AmpsAddWikiLink");
            Assert.Contains(Find<StackPanel>(window, "AmpsWikiLinks").Children.OfType<HyperlinkButton>(),
                link => link.Tag is AmpReference { Url: "https://wiki.fractalaudio.com/wiki/index.php?title=Amp_models#Bassguy" });
            Click(Find<Button>(window, "AmpsBackToDirectory"));
            Assert.Equal(offset, scroll.Offset.Y);
            Assert.Equal("fender-bassman-59", ((BrowserAmpFamily)((ListBoxItem)list.SelectedItem!).Tag!).Family.Id);
            Find<ComboBox>(window, "AmpsPresetUsage").SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, list.ItemCount); // Bassman, Plexi and the unmapped ID.
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ContainsFilterCannotCrossDevicesFirmwareOrVariantsAndDisplayMappingDoesNotChangeSavedFacts()
    {
        var cache = Cache(); var preset = cache.Committed!.Presets[0];
        var filter = new AmpContainsFilter(cache.Device.Id, cache.Device.Variant, cache.Device.Firmware, "Bassman", [0]);
        Assert.True(filter.AppliesTo(cache));
        cache.Device = cache.Device with { Firmware = "12.0" }; Assert.True(filter.AppliesTo(cache));
        cache.Device = cache.Device with { Firmware = "13.00" }; Assert.False(filter.AppliesTo(cache));
        Assert.False(filter.AppliesTo(Cache()));
        Assert.False(filter.Matches(preset with { Variant = FractalDeviceVariant.FM3 }));
        var resolved = AmpBrowserCatalog.ResolveForDisplay(preset, "12.00");
        Assert.Equal(preset.ContentSha256, resolved.ContentSha256);
        Assert.Equal("Unknown Amp model #0", preset.Amps[0].Channels[0].Model.DisplayName);
        Assert.Equal("59 Bassguy Bright", resolved.Amps[0].Channels[0].Model.DisplayName);
        Assert.Same(preset, AmpBrowserCatalog.ResolveForDisplay(preset, "13.00"));
    }

    [Fact]
    public void ChannelSummariesKeepBlocksModelsAndNoncontiguousChannelsDistinct()
    {
        var filter = new AmpContainsFilter(Guid.NewGuid(), FractalDeviceVariant.FM9, "12.00", "Selected amp", [0]);
        Assert.Equal(new[] { "Amp 1 · channels A–D" }, filter.MatchingChannelSummary(Preset(0, 0, 0, 0, 0)));
        Assert.Equal(new[] { "Amp 1 · channels A, C" }, filter.MatchingChannelSummary(Preset(0, 0, 141, 0, 141)));
        Assert.Equal(new[] { "Amp 1 · channel B" }, filter.MatchingChannelSummary(Preset(0, 141, 0, 141, 141)));
        var preset = Preset(0, 0, 141, 0, 141);
        preset = preset with { Amps = [preset.Amps[0], preset.Amps[0] with { BlockNumber = 2 }] };
        Assert.Equal(new[] { "Amp 1 · channels A, C", "Amp 2 · channels A, C" }, filter.MatchingChannelSummary(preset));
        filter = filter with { ModelIds = [0, 141] };
        Assert.Equal(new[]
        {
            "Amp 1 · Unknown Amp model #0 · channels A, C",
            "Amp 1 · Unknown Amp model #141 · channels B, D",
            "Amp 2 · Unknown Amp model #0 · channels A, C",
            "Amp 2 · Unknown Amp model #141 · channels B, D",
        }, filter.MatchingChannelSummary(preset));
    }

    private static ComboBoxItem UsageOption(Window window, int index) => Find<ComboBox>(window, "AmpsPresetUsage").Items.OfType<ComboBoxItem>().ElementAt(index);

    private static void SelectFamily(ListBox list, string id)
    {
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(i => ((BrowserAmpFamily)i.Tag!).Family.Id == id);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
    }
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static CheckBox[] VariantChecks(Window window) => window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name == "AmpsVariant").ToArray();
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
        if (output is null) { return; }
        Directory.CreateDirectory(output); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame(); image!.Save(Path.Combine(output, name + ".png"));
    }
}
