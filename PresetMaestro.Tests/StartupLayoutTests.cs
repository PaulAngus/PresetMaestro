using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed class StartupLayoutTests
{
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void OpensAtMinimumWidthOnConfigAndAllowsResizingAndNavigation(string theme)
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-startup-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory); var settings = store.LoadSettings(); settings.Theme = theme;
        var window = new MainWindow(settings, [], new DeviceMidi(), profileStore: store);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1000, window.MinWidth);
            Assert.Equal(window.MinWidth, window.Width);
            Assert.Equal(window.MinWidth, window.Bounds.Width);
            var config = Find<Grid>("ConfigLayout");
            Assert.True(config.IsEffectivelyVisible);
            Assert.True(Find<Button>("Connect").IsEffectivelyVisible);
            Assert.True(Find<ComboBox>("ProfileSelector").IsEffectivelyVisible);
            Assert.NotEqual(Find<Button>("NavPresetSender").Background, Find<Button>("NavConfig").Background);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"startup-config-1000-{theme}");
            window.Width = 1440; Dispatcher.UIThread.RunJobs();
            Assert.Equal(1440, window.Bounds.Width);
            Assert.True(config.IsEffectivelyVisible);
            FavoriteEditorTests.CaptureSendConfirmation(window, $"startup-config-1440-{theme}");
            Find<Button>("NavPresetSender").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(config, window.GetVisualDescendants());
            Assert.True(Find<Border>("PresetDisplayCard").IsEffectivelyVisible);
        }
        finally { window.Close(); Directory.Delete(directory, true); }

        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    }
}
