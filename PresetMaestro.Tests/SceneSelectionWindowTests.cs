using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace PresetMaestro.Tests;

public class SceneSelectionWindowTests
{
    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void ShowsAllScenesWithTheirNumbersAndNames()
    {
        var window = new SceneSelectionWindow(["Clean", "Crunch", "Lead"], 2);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var scenes = Find<ListBox>(window, "SceneList");
            Assert.Equal(8, scenes.Items.Count);
            Assert.Equal(4, scenes.GetVisualDescendants().OfType<UniformGrid>().Single().Columns);
            Assert.Equal("Clean", Find<TextBlock>(window, "SceneName1").Text);
            Assert.Equal("Lead", Find<TextBlock>(window, "SceneName3").Text);
            Assert.Equal("Scene 8", Find<TextBlock>(window, "SceneName8").Text);
            Assert.Equal("Selected: Scene 2 — Crunch", Find<TextBlock>(window, "SelectedSceneSummary").Text);
            Assert.Equal(2, (int)((ListBoxItem)scenes.SelectedItem!).Tag!);
            if (Environment.GetEnvironmentVariable("device_SCENE_PICKER_SNAPSHOT") is { Length: > 0 } snapshot)
            {
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(snapshot);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EnterReturnsSelectedScene()
    {
        var owner = new Window();
        owner.Show();
        var window = new SceneSelectionWindow(null, 6);
        try
        {
            var result = window.ShowDialog<int?>(owner);
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(6, await result);
        }
        finally { window.Close(); owner.Close(); }
    }
}
