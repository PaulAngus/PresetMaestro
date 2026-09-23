using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public sealed class SettingsManagerTests
{
    [Fact]
    public void MissingSettingsFileReturnsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"device-missing-{Guid.NewGuid():N}", "settings.json");

        AppSettings settings = SettingsManager.Load(path);

        Assert.Equal(1, settings.MidiChannel);
        Assert.Equal(511, settings.MaxDisplayedPreset);
        Assert.Equal(34, settings.SceneCc);
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("1", settings.MidiNoteMap[36]);
    }

    [Fact]
    public void SettingsRoundTripPreservesNestedCachesAndCollections()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"device-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var expected = new AppSettings
            {
                MidiInputPort = "Input",
                MidiOutputPort = "Output",
                ThruInputPorts = ["Controller"],
                MidiChannel = 7,
                DisplayOffset = 1,
                MaxDisplayedPreset = 512,
                AutoSend = true,
                MidiNoteMap = new Dictionary<int, string> { [60] = "SEND" },
                PresetNameCache = new Dictionary<int, string> { [128] = "Preset" },
                SceneNameCaches = new Dictionary<string, Dictionary<int, SceneCacheEntry>>
                {
                    ["ports"] = new Dictionary<int, SceneCacheEntry>
                    {
                        [128] = new SceneCacheEntry
                        {
                            Names = ["One", "Two"],
                            RetrievedAt = DateTimeOffset.Parse("2026-09-19T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                            Source = "stored",
                        },
                    },
                },
            };

            SettingsManager.Save(expected, path);
            AppSettings actual = SettingsManager.Load(path);

            Assert.Equal(expected.MidiInputPort, actual.MidiInputPort);
            Assert.Equal(expected.MidiOutputPort, actual.MidiOutputPort);
            Assert.Equal(expected.ThruInputPorts, actual.ThruInputPorts);
            Assert.Equal(expected.MidiChannel, actual.MidiChannel);
            Assert.Equal(expected.DisplayOffset, actual.DisplayOffset);
            Assert.Equal(expected.MaxDisplayedPreset, actual.MaxDisplayedPreset);
            Assert.True(actual.AutoSend);
            Assert.Equal("SEND", actual.MidiNoteMap[60]);
            Assert.Equal("Preset", actual.PresetNameCache[128]);
            SceneCacheEntry scene = actual.SceneNameCaches["ports"][128];
            Assert.Equal(["One", "Two"], scene.Names);
            Assert.Equal(expected.SceneNameCaches["ports"][128].RetrievedAt, scene.RetrievedAt);
            Assert.Equal("stored", scene.Source);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void InvalidJsonIsNotSilentlyAcceptedByTestableLoader()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{ invalid json }");

            Assert.ThrowsAny<System.Text.Json.JsonException>(() => SettingsManager.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveRejectsNullSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"device-settings-{Guid.NewGuid():N}", "settings.json");

        Assert.Throws<ArgumentNullException>(() => SettingsManager.Save(null!, path));
    }
}
