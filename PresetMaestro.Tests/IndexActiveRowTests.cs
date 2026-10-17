using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PresetMaestro.Tests;

public sealed partial class DeviceNavigationTests
{
    [AvaloniaTheory]
    [InlineData("Light", 1440, 1)]
    [InlineData("Dark", 1440, 0)]
    [InlineData("Light", 1000, 0)]
    [InlineData("Dark", 1000, 1)]
    public void IndexActiveMarkersFollowConfirmedDeviceStateWithoutChangingSelection(string theme, int width, int offset)
    {
        var (window, midi) = Window(theme: theme, width: width, displayOffset: offset, height: width == 1000 ? 640 : 850);
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var presets = Find<ListBox>(window, "IndexPresetList");
            Assert.Empty(Active(presets));
            Confirm(1, 3);
            var activePreset = (ListBoxItem)presets.Items[1]!;
            Assert.Same(activePreset, Assert.Single(Active(presets)).GetVisualAncestors().OfType<ListBoxItem>().First());
            presets.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "IndexCloseInspector"));
            Assert.Same(activePreset, Assert.Single(Active(presets)).GetVisualAncestors().OfType<ListBoxItem>().First());
            Assert.Contains("Active", AutomationProperties.GetHelpText(activePreset));
            Snapshot("presets");

            IndexView(window, 1);
            var scenes = Find<ListBox>(window, "IndexSceneList");
            Assert.Same(scenes.ContainerFromIndex(10), Assert.Single(Active(scenes)).GetVisualAncestors().OfType<ListBoxItem>().First());
            scenes.SelectedIndex = 11; Dispatcher.UIThread.RunJobs();
            Snapshot("scenes");
            var selected = scenes.SelectedItem;
            Confirm(1, 4);
            Assert.Same(selected, scenes.SelectedItem);
            Assert.Same(scenes.ContainerFromIndex(11), Assert.Single(Active(scenes)).GetVisualAncestors().OfType<ListBoxItem>().First());
            Confirm(2, 1);
            Assert.Same(scenes.ContainerFromIndex(16), Assert.Single(Active(scenes)).GetVisualAncestors().OfType<ListBoxItem>().First());
            Assert.Equal("", AutomationProperties.GetHelpText(scenes.ContainerFromIndex(11)!));

            // Rebuilding results must restore the current marker, and filtering it out must hide it.
            var search = Find<TextBox>(window, "IndexSearchInput");
            search.Text = "No matching scene"; Dispatcher.UIThread.RunJobs();
            Assert.Empty(Active(scenes));
            search.Text = ""; Dispatcher.UIThread.RunJobs();
            Assert.Single(Active(scenes));
            typeof(MainWindow).GetMethod("StopSceneTracking", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Empty(Active(scenes));
            IndexView(window, 0);
            Assert.Empty(Active(presets));
            Assert.Equal(2, presets.SelectedIndex);
            Assert.Empty(midi.Writes);
        }
        finally { window.Close(); }

        void Confirm(int? slot, int? scene)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_detectedPresetSlot", flags)!.SetValue(window, slot);
            typeof(MainWindow).GetField("_detectedScene", flags)!.SetValue(window, scene);
            typeof(MainWindow).GetMethod("UpdateIndexActiveRows", flags)!.Invoke(window, null);
            Dispatcher.UIThread.RunJobs();
        }
        static Border[] Active(Control list) => list.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Name == "IndexActiveAccent" && b.IsVisible).ToArray();
        void Snapshot(string view)
        {
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
            using var bitmap = window.CaptureRenderedFrame();
            bitmap!.Save(Path.Combine(output, $"active-{view}-{theme}-{width}.png"));
        }
    }
}
