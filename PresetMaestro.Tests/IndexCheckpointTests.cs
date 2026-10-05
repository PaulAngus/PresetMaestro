using System.Text.Json;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class IndexCheckpointTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-journal-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }
    private static DeviceIndex Cache() => new()
    {
        Device = new(Guid.NewGuid(), "Test", FractalDeviceVariant.FM9, "12.00"),
        LastAttempt = new() { Firmware = "12.00" },
    };
    private static IndexedPreset Preset(int slot) => new(FractalDeviceVariant.FM9, slot, "Preset " + slot,
        Enumerable.Repeat("Scene", 8).ToArray(), [], "hash" + slot);

    [Fact]
    public void InvalidCheckpointCannotBeAppended()
    {
        var library = new IndexLibrary(_directory); var cache = Cache(); library.Save(cache);
        Assert.Throws<InvalidDataException>(() => library.SaveCheckpoint(cache, 0));
        cache.LastAttempt!.Presets[0] = Preset(1);
        Assert.Throws<InvalidDataException>(() => library.SaveCheckpoint(cache, 0));
        cache.LastAttempt.Presets[0] = Preset(0); cache.LastAttempt.Errors[0] = "Error";
        Assert.Throws<InvalidDataException>(() => library.SaveCheckpoint(cache, 0));
        cache.LastAttempt.Presets[512] = Preset(512);
        Assert.Throws<InvalidDataException>(() => library.SaveCheckpoint(cache, 512));
        Assert.False(File.Exists(Path.Combine(_directory, cache.Device.Id.ToString("N") + ".scan.jsonl")));
    }

    [Fact]
    public void NewReaderRecoversCompletedRecordsAndIgnoresOnlyTornFinalRecord()
    {
        var library = new IndexLibrary(_directory); var cache = Cache(); library.Save(cache);
        string original = File.ReadAllText(Path.Combine(_directory, cache.Device.Id.ToString("N") + ".json"));
        cache.LastAttempt!.Presets[0] = Preset(0); library.SaveCheckpoint(cache, 0);
        cache.LastAttempt.Errors[1] = "Timeout"; library.SaveCheckpoint(cache, 1);
        cache.LastAttempt.Presets[1] = Preset(1); cache.LastAttempt.Errors.Remove(1); library.SaveCheckpoint(cache, 1);
        string journal = Path.Combine(_directory, cache.Device.Id.ToString("N") + ".scan.jsonl");
        File.AppendAllText(journal, "{\"ScanId\":");
        var restored = new IndexLibrary(_directory).Load(cache.Device.Id)!;
        Assert.Equal(2, restored.LastAttempt!.Presets.Count); Assert.Empty(restored.LastAttempt.Errors);
        Assert.Equal(cache.LastAttempt.Id, restored.LastAttempt.Id);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_directory, cache.Device.Id.ToString("N") + ".json")));
        // Resuming compacts first, including removal of the torn record.
        library.Save(restored);
        Assert.False(File.Exists(journal));
        restored.LastAttempt.Presets[2] = Preset(2); library.SaveCheckpoint(restored, 2);
        Assert.Equal(3, library.Load(cache.Device.Id)!.LastAttempt!.Presets.Count);
    }

    [Fact]
    public void CompleteInvalidRecordIsReportedAndOldGenerationCannotContaminateNewScan()
    {
        var library = new IndexLibrary(_directory); var cache = Cache(); library.Save(cache);
        cache.LastAttempt!.Presets[0] = Preset(0); library.SaveCheckpoint(cache, 0);
        string path = Path.Combine(_directory, cache.Device.Id.ToString("N") + ".scan.jsonl");
        string oldJournal = File.ReadAllText(path);
        File.AppendAllText(path, "{}\n");
        // A valid current-generation record with an impossible slot is corruption.
        File.WriteAllText(path, JsonSerializer.Serialize(new { ScanId = cache.LastAttempt.Id, Slot = 512, Preset = Preset(512) }) + "\n");
        Assert.Throws<InvalidDataException>(() => library.Load(cache.Device.Id));
        cache.LastAttempt = new() { Firmware = "12.00" }; library.Save(cache);
        File.WriteAllText(path, oldJournal);
        Assert.Empty(library.Load(cache.Device.Id)!.LastAttempt!.Presets);
        cache.LastAttempt.Status = "Cancelled"; library.Save(cache);
        File.WriteAllText(path, "not valid json\n");
        Assert.Equal("Cancelled", library.Load(cache.Device.Id)!.LastAttempt!.Status);
    }

    [Fact]
    public void MetadataCacheTracksExternalReplacementCorruptionAndDeletionWithoutCachingMutablePresets()
    {
        var library = new IndexLibrary(_directory); var writer = new IndexLibrary(_directory); var cache = Cache();
        library.Save(cache);
        var first = library.LoadSummary(cache.Device.Id);
        Assert.Same(first, library.LoadSummary(cache.Device.Id));
        cache.Device = cache.Device with { Name = "Renamed externally" }; cache.PresetMapping = new(2, 1, 45); writer.Save(cache);
        Assert.Equal(cache.Device, Assert.Single(library.ListDevices()));
        Assert.Equal(cache.PresetMapping, library.LoadSummary(cache.Device.Id)!.PresetMapping);
        var loaded = library.Load(cache.Device.Id)!; loaded.Device = loaded.Device with { Name = "Unsaved" };
        Assert.Equal("Renamed externally", library.LoadSummary(cache.Device.Id)!.Device.Name);
        File.WriteAllText(Path.Combine(_directory, cache.Device.Id.ToString("N") + ".json"), "corrupt");
        Assert.Throws<JsonException>(() => library.LoadSummary(cache.Device.Id));
        writer.Delete(cache.Device.Id);
        Assert.Null(library.LoadSummary(cache.Device.Id)); Assert.Empty(library.ListDevices());
    }
}
