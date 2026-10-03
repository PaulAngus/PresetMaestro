using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    public void DirectoryIncludesUnusedModelsCountsDistinctPresetsAndKeepsUnknownIds()
    {
        var directory = AmpBrowserCatalog.Build(Cache());
        Assert.False(directory.IsComplete);
        Assert.Contains("incomplete", directory.Coverage);
        Assert.True(directory.Families.Sum(f => f.Variants.Length) >= 285);
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
        Assert.NotEqual(For(39).Family.Id, For(298).Family.Id); // Disputed Crunch reference.
        Assert.Equal("partially resolved; see evidence", For(298).Family.Confidence);
        Assert.Contains("unresolved", For(263).Family.Name);
        Assert.Equal(For(166).Family.Id, For(271).Family.Id); // DC-30 preamp channels.
        Assert.Equal(For(303).Family.Id, For(307).Family.Id); // REVV channels, one reference amp.
        Assert.Equal("Fender", For(111).Family.Manufacturer); // Physical custom reference.
        Assert.Equal("Fractal originals", For(136).Family.Manufacturer); // Virtual Thordendal.
        Assert.All(families.SelectMany(f => f.Variants).Where(v => v.MappingEvidence.Contains("Manufacturer/model sources")),
            v => { Assert.Contains("ampdex", v.MappingEvidence); Assert.Contains("wiki.fractalaudio.com", v.MappingEvidence); });
        Assert.False(For(303).Variants.Single(v => v.ModelId == 303).IdentityVerified);
        Assert.Equal("Unmapped", For(118).Family.Manufacturer); // Mid vs Deep rename not guessed.
    }

    [Fact]
    public void AmpUsageRequiresSavedPresetsFromCompatibleFirmware()
    {
        var cache = Cache();
        Assert.True(AmpBrowserCatalog.Build(cache).HasUsageData);
        Assert.True(AmpBrowserCatalog.Build(Cache(false)).HasUsageData);
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
            var used = Find<CheckBox>(window, "AmpsUsedOnly");
            Assert.False(used.IsEnabled); Assert.False(used.IsChecked);
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
            Assert.False(Find<CheckBox>(window, "AmpsUsedOnly").IsEnabled);
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
    [InlineData(1440, "Dark")]
    public void BrowseFamilyVariantContainsBackAndZeroMatchesWithoutMidi(int width, string theme)
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.Theme = theme;
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
            var first = Find<Button>(window, "NavPresetSender"); var last = Find<Button>(window, "NavConfig");
            Assert.True(first.TranslatePoint(default, window)!.Value.X > 150);
            Assert.True(last.TranslatePoint(default, window)!.Value.X + last.Bounds.Width < Find<TextBlock>(window, "IndexAssignedDevice").TranslatePoint(default, window)!.Value.X);
            Find<TextBox>(window, "AmpsSearch").Text = "Bassguy"; Dispatcher.UIThread.RunJobs();
            SelectFamily(list, "fender-bassman-59");
            Assert.All(VariantChecks(window), check => Assert.True(check.IsChecked));
            Assert.True(list.IsEffectivelyVisible);
            Assert.True(Find<StackPanel>(window, "AmpsDetail").TranslatePoint(default, window)!.Value.Y >= list.TranslatePoint(default, window)!.Value.Y + list.Bounds.Height);
            Capture(window, $"amps-family-{width}-{theme}");
            Click(Find<Button>(window, "AmpsFindPresets"));
            Assert.Equal(2, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Contains("Contains: Fender", Find<TextBlock>(window, "IndexAmpFilterLabel").Text!);
            Find<ListBox>(window, "IndexPresetList").SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Assert.Contains("Amp 1 / B", Find<TextBlock>(window, "IndexAmpMatches").Text!);
            Capture(window, $"amps-contains-{width}-{theme}");
            Find<ComboBox>(window, "IndexAmpSceneUsage").SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            Assert.Empty(Find<ListBox>(window, "IndexPresetList").Items);
            Find<ComboBox>(window, "IndexAmpSceneUsage").SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Click(Find<Button>(window, "IndexClearSearch"));
            Assert.Equal(2, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Click(Find<Button>(window, "IndexBackToAmps"));
            Assert.Equal("Bassguy", Find<TextBox>(window, "AmpsSearch").Text);
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
            Click(Find<Button>(window, "IndexClearAmpFilter"));
            Assert.Equal(512, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BrowserKeepsScrollAndReferencesAndDoesNotAcceptPresetSendShortcuts()
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.KeyboardEntryEnabled = true;
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
            var references = Find<Button>(window, "AmpsReferences"); Click(references);
            var menu = references.ContextMenu!;
            menu.Items.OfType<MenuItem>().Single(i => i.Name == "AmpsAddWikiLink").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close(); Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(window.OwnedWindows);
            Find<TextBox>(dialog, "AmpReferenceUrl").Text = "https://wiki.fractalaudio.com/wiki/index.php?title=Amp_models#Bassguy";
            Click(Find<Button>(dialog, "AmpReferenceSave"));
            Assert.Empty(window.OwnedWindows);
            Assert.Equal("References (2)", Find<Button>(window, "AmpsReferences").Content);
            Click(Find<Button>(window, "AmpsBackToDirectory"));
            Assert.Equal(offset, scroll.Offset.Y);
            Assert.Equal("fender-bassman-59", ((BrowserAmpFamily)((ListBoxItem)list.SelectedItem!).Tag!).Family.Id);
            Find<CheckBox>(window, "AmpsUsedOnly").IsChecked = true; Dispatcher.UIThread.RunJobs();
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
        Directory.CreateDirectory(output); using var image = window.CaptureRenderedFrame(); image!.Save(Path.Combine(output, name + ".png"));
    }
}
