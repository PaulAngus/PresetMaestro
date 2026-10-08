using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
    public void SingleVariantFamilyCanRenderToggleAndDismissWithoutCrashing(int width, string theme)
    {
        var store = new ProfileStore(_directory); var settings = store.LoadSettings(); settings.Theme = theme;
        var cache = Cache(); new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(cache);
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [cache.Device], SelectedDeviceId = cache.Device.Id });
        store.SaveSettings(settings);
        var midi = new DeviceMidi(); var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = width == 1000 ? 640 : 850 };
        window.Show();
        try
        {
            Click(Find<Button>(window, "NavAmps"));
            var directory = Find<ListBox>(window, "AmpsDirectory");
            var familyRow = directory.Items.OfType<ListBoxItem>().First(row => ((BrowserAmpFamily)row.Tag!).Variants.Length == 1);
            var family = (BrowserAmpFamily)familyRow.Tag!;
            SelectFamily(directory, family.Family.Id);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var check = Assert.Single(VariantChecks(window));
            Assert.True(check.Bounds.Width > 0);
            Assert.True(Find<Border>(window, "AmpsDetailFrame").IsEffectivelyVisible);
            check.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs(); Assert.False(check.IsChecked);
            Assert.False(Find<Button>(window, "AmpsFindPresets").IsEnabled);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.True(check.IsChecked); Assert.True(Find<Button>(window, "AmpsFindPresets").IsEnabled);
            Capture(window, $"amps-single-variant-{width}-{theme}");
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "AmpsDetailFrame").IsEffectivelyVisible);
            Assert.True(familyRow.IsFocused); Assert.Same(familyRow, directory.SelectedItem);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }
    }
}
