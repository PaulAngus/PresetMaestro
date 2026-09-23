using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    private static MidiMappingWindow OpenMapping(MainWindow window)
    {
        Click(Find<Button>(window, "OpenMidiMapping"));
        Dispatcher.UIThread.RunJobs();
        return Field<MidiMappingWindow>(window, "_midiMappingWindow");
    }

    private static void SetSceneCcThroughMapping(MainWindow window, int sceneCc)
    {
        var dialog = OpenMapping(window);
        Find<TextBox>(dialog, "SceneCc").Text = sceneCc.ToString();
        Click(Find<Button>(dialog, "SaveMapping"));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void MappingMatchesCompactLayoutAndFixedActions(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var window = new MidiMappingWindow("Default", 34, AppSettings.DefaultMidiNoteMap(), (_, _) => { });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tiles = window.GetVisualDescendants().OfType<Button>().Where(button => button.Name?.StartsWith("MapAction", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(15, tiles.Length);
            Assert.Equal(450, window.Width);
            Assert.Equal(156, Find<StackPanel>(window, "MappingCommands").Bounds.Width);
            Assert.InRange(Find<StackPanel>(window, "MappingNoteEditor").Bounds.Height, 30, 32);
            Assert.Equal("34", Find<TextBox>(window, "SceneCc").Text);
            Assert.Equal("36", Find<TextBox>(window, "MappingNoteNumber").Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "Default profile");
            Assert.All(tiles, tile =>
            {
                var point = tile.TranslatePoint(default, window)!.Value;
                Assert.True(point.X >= 0 && point.X + tile.Bounds.Width <= window.Bounds.Width);
                Assert.True(point.Y + tile.Bounds.Height <= window.Bounds.Height);
            });
            if (Environment.GetEnvironmentVariable("PRESETMAESTRO_MAPPING_SCREENSHOT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(directory, $"midi-mapping-{theme.ToLowerInvariant()}.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MappingSavesCurrentEditorAndPriorTileEditsToCorrectStorageScopes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-mapping-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(directory);
        var settings = store.LoadSettings();
        store.Create("Other", new ProfileSettings { SceneCc = 55 });
        var midi = new FakeMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<NumericUpDown>(), control => control.Name == "SceneCc");
            var dialog = OpenMapping(window);
            Find<TextBox>(dialog, "SceneCc").Text = "62";
            Find<TextBox>(dialog, "MappingNoteNumber").Text = "60";
            Click(Find<Button>(dialog, "MapActionSEND"));
            Assert.Equal("SEND · MIDI note", Find<TextBlock>(dialog, "MappingSelectedAction").Text);
            Find<TextBox>(dialog, "MappingNoteNumber").Text = "61";
            Assert.Equal(34, settings.SceneCc);
            Assert.Equal("1", settings.MidiNoteMap[36]);
            Click(Find<Button>(dialog, "SaveMapping"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsVisible);
            Assert.Equal(62, settings.SceneCc);
            Assert.Equal("1", settings.MidiNoteMap[60]);
            Assert.Equal("SEND", settings.MidiNoteMap[61]);
            Assert.DoesNotContain(36, settings.MidiNoteMap.Keys);
            Assert.DoesNotContain(46, settings.MidiNoteMap.Keys);
            Assert.Equal(15, settings.MidiNoteMap.Count);
            Assert.Equal(62, store.LoadProfile("Default").SceneCc);
            Assert.Equal(55, store.LoadProfile("Other").SceneCc);
            var machine = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "settings.json")))!.AsObject();
            var profile = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "Default-settings.json")))!.AsObject();
            Assert.False(machine.ContainsKey("SceneCc"));
            Assert.False(profile.ContainsKey("MidiNoteMap"));
            Assert.Equal("SEND", new ProfileStore(directory).LoadSettings().MidiNoteMap[61]);
            Assert.Equal(0, midi.TotalSendCount);
            // Rebuilding Config or closing the main window must not restore the old CC.
            var reopened = OpenMapping(window);
            Assert.Equal("62", Find<TextBox>(reopened, "SceneCc").Text);
            Click(Find<Button>(reopened, "CancelMapping"));
            window.Close();
            Assert.Equal(62, store.LoadProfile("Default").SceneCc);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("37", "34", "already assigned")]
    [InlineData("128", "34", "0 to 127")]
    [InlineData("-1", "34", "0 to 127")]
    [InlineData("36.5", "34", "whole MIDI")]
    [InlineData("", "34", "whole MIDI")]
    [InlineData("36", "128", "Scene CC#")]
    [InlineData("36", "", "Scene CC#")]
    public void MappingRejectsInvalidOrDuplicateValues(string note, string scene, string expected)
    {
        int saved = 0;
        var window = new MidiMappingWindow("Default", 34, AppSettings.DefaultMidiNoteMap(), (_, _) => saved++);
        try
        {
            window.Show();
            Find<TextBox>(window, "MappingNoteNumber").Text = note;
            Find<TextBox>(window, "SceneCc").Text = scene;
            Click(Find<Button>(window, "SaveMapping"));
            Assert.Equal(0, saved);
            Assert.True(window.IsVisible);
            Assert.Contains(expected, Find<TextBlock>(window, "MappingMessage").Text + Find<TextBlock>(window, "SceneCcError").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Cancel")]
    [InlineData("Escape")]
    [InlineData("Close")]
    public void MappingDismissalDiscardsBothSettings(string dismissal)
    {
        var source = AppSettings.DefaultMidiNoteMap();
        int saved = 0;
        var window = new MidiMappingWindow("Default", 34, source, (_, _) => saved++);
        try
        {
            window.Show();
            Find<TextBox>(window, "MappingNoteNumber").Text = "60";
            Click(Find<Button>(window, "MapActionSEND"));
            Find<TextBox>(window, "SceneCc").Text = "80";
            if (dismissal == "Cancel") { Click(Find<Button>(window, "CancelMapping")); }
            else if (dismissal == "Escape") { window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); }
            else { window.Close(); }
            Assert.False(window.IsVisible);
            Assert.Equal(0, saved);
            Assert.Equal("1", source[36]);
            Assert.DoesNotContain(60, source.Keys);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MappingSaveFailureKeepsEditorOpenAndRestoresLiveSettings()
    {
        var settings = new AppSettings();
        bool fail = true;
        var window = new MainWindow(settings, [], new FakeMidi(), saveSettings: _ => { if (fail) { throw new IOException("Disk unavailable"); } }, saveFavorites: _ => { });
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            var dialog = OpenMapping(window);
            Find<TextBox>(dialog, "MappingNoteNumber").Text = "60";
            Find<TextBox>(dialog, "SceneCc").Text = "70";
            Click(Find<Button>(dialog, "SaveMapping"));
            Assert.True(dialog.IsVisible);
            Assert.Equal(34, settings.SceneCc);
            Assert.Equal("1", settings.MidiNoteMap[36]);
            Assert.Contains("Disk unavailable", Find<TextBlock>(dialog, "MappingMessage").Text);
            fail = false;
            Click(Find<Button>(dialog, "SaveMapping"));
            Assert.False(dialog.IsVisible);
            Assert.Equal(70, settings.SceneCc);
            Assert.Equal("1", settings.MidiNoteMap[60]);
        }
        finally { fail = false; window.Close(); }
    }
}
