using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed partial class AmpBrowserTests
{
    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Light")]
    [InlineData(1440, "Dark")]
    public void CompactModelTableSearchTracksMouseAndKeyboardChoices(int width, string theme)
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.Theme = theme;
        var cache = Cache();
        var family = AmpBrowserCatalog.Build(cache).Families.Single(family => family.Family.Name == "2203 100W");
        var models = family.Variants;
        cache.Committed!.Presets[8] = Preset(8, Enumerable.Repeat(models[0].ModelId, 4).ToArray());
        cache.Committed.Presets[9] = Preset(9, Enumerable.Repeat(models[1].ModelId, 4).ToArray());
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = width == 1000 ? 640 : 850 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            var directory = Find<ListBox>(window, "AmpsDirectory");
            SelectFamily(directory, family.Family.Id);
            var action = Find<Button>(window, "AmpsFindPresets");
            var checks = VariantChecks(window);
            Assert.Equal(2, checks.Length);
            Assert.Equal("Show presets for 2 models", action.Content);
            var tableHeading = Find<Grid>(window, "AmpsModelTableHeaders");
            Assert.True(action.TranslatePoint(default, window)!.Value.Y + action.Bounds.Height <= tableHeading.TranslatePoint(default, window)!.Value.Y);
            Capture(window, $"amps-model-table-{width}-{theme}");

            // Pointer hit testing needs the current rendered scene even when
            // optional screenshot output is disabled.
            window.CaptureRenderedFrame()?.Dispose();
            var firstLabel = window.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "AmpsVariantModelLabel" && Equals(control.Tag, checks[0].Tag));
            var labelPosition = firstLabel.TranslatePoint(new Point(12, 12), window)!.Value;
            window.MouseDown(labelPosition, MouseButton.Left); window.MouseUp(labelPosition, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(checks[0].IsChecked);
            Assert.True(checks[0].IsFocused);
            Assert.Equal("Show presets for 1 model", action.Content);
            Capture(window, $"amps-model-table-one-choice-{width}-{theme}");
            Click(action);
            var results = Find<ListBox>(window, "IndexPresetList");
            Assert.Single(results.Items);
            Click(Find<Button>(window, "IndexBackToAmps"));
            checks = VariantChecks(window);
            Assert.False(checks[0].IsChecked); Assert.True(checks[1].IsChecked);
            checks[0].Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            action = Find<Button>(window, "AmpsFindPresets");
            Assert.True(checks[0].IsChecked);
            Assert.Equal("Show presets for 2 models", action.Content);
            Click(action);
            Assert.Equal(2, Find<ListBox>(window, "IndexPresetList").ItemCount);
            Click(Find<Button>(window, "IndexBackToAmps"));
            foreach (var check in VariantChecks(window)) { check.IsChecked = false; }
            action = Find<Button>(window, "AmpsFindPresets");
            Assert.Equal("Show presets for 0 models", action.Content);
            Assert.False(action.IsEnabled);
            var origin = (ListBoxItem)Find<ListBox>(window, "AmpsDirectory").SelectedItem!;
            VariantChecks(window)[0].Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "AmpsDetailFrame").IsEffectivelyVisible);
            Assert.True(origin.IsFocused);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }
}
