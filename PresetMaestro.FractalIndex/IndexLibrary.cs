using System.Text.Json;

namespace PresetMaestro.FractalIndex;

public sealed record IndexDevice(Guid Id, string Name, FractalDeviceVariant Variant, string? Firmware = null)
{
    public override string ToString() => Name;
}

public sealed record IndexLibrarySummary(IndexDevice Device, DevicePresetMapping? PresetMapping);

public sealed record DevicePresetMapping(int MidiChannel = 1, int DisplayOffset = 0, int SceneCc = 34)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => MidiChannel is >= 0 and <= 16 && DisplayOffset is >= 0 and <= 1 && SceneCc is >= 0 and <= 127;
}

public sealed class IndexScan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Firmware { get; set; }
    public ScanFirmwareConfirmation? FirmwareConfirmation { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EffectiveFirmware => Firmware ?? FirmwareConfirmation?.Firmware;
    public string? ConnectedDeviceName { get; set; }
    public string Status { get; set; } = "Scanning";
    public Dictionary<int, IndexedPreset> Presets { get; set; } = [];
    public Dictionary<int, string> Errors { get; set; } = [];
}

// Older files may also contain a ThresholdPercent value; it is ignored when read.
public sealed record ScanFirmwareConfirmation(string Firmware, DateTimeOffset CheckedAt, bool IsSample,
    int Matched, int Compared);

public sealed class DeviceIndex
{
    public int SchemaVersion { get; set; } = 1;
    public required IndexDevice Device { get; set; }
    // Null identifies older libraries whose mapping still belongs to a profile.
    public DevicePresetMapping? PresetMapping { get; set; }
    public bool Imported { get; set; }
    public IndexScan? Committed { get; set; }
    public IndexScan? LastAttempt { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IndexScan? Browsable => Committed ?? LastAttempt;
}

public sealed class PresetAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    public int Slot { get; set; }
    public string Name { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public string? Firmware { get; set; }
    public List<string> Tags { get; set; } = [];
    public Dictionary<int, List<string>> SceneTags { get; set; } = [];
    public string[] SceneNames { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasTags => Tags.Count > 0 || SceneTags.Values.Any(tags => tags.Count > 0);
    public bool Matches(Guid deviceId, IndexedPreset preset, string? firmware) =>
        !preset.NameOnlyEmpty && DeviceId == deviceId && Slot == preset.Slot && ContentSha256 == preset.ContentSha256 && Firmware == firmware;
}

public sealed class IndexTagReassignment
{
    public List<PresetAnnotation> Before { get; set; } = [];
    public List<PresetAnnotation> After { get; set; } = [];
}

public sealed class IndexProfile
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ProfileId { get; set; } = Guid.NewGuid();
    // Retain previously assigned library references so their tags and exports remain recoverable.
    // SelectedDeviceId is the single profile assignment; these are not browsing choices.
    public List<IndexDevice> Devices { get; set; } = [];
    public Guid? SelectedDeviceId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public IndexDevice? AssignedDevice => Devices.FirstOrDefault(d => d.Id == SelectedDeviceId);

    public void AssignDevice(IndexDevice device)
    {
        Devices.RemoveAll(d => d.Id == device.Id);
        Devices.Add(device);
        SelectedDeviceId = device.Id;
    }
    public List<PresetAnnotation> Annotations { get; set; } = [];
    // An explicit reconciliation can be undone, including scene-number reassignment.
    public List<IndexTagReassignment> ReviewHistory { get; set; } = [];
    public List<DeviceIndex> PortableSnapshots { get; set; } = [];
    public List<AmpReference> PortableAmpReferences { get; set; } = [];

    public PresetAnnotation? Find(Guid deviceId, IndexedPreset preset, string? firmware) =>
        Annotations.FirstOrDefault(a => a.Matches(deviceId, preset, firmware));

    public IEnumerable<PresetAnnotation> Pending(DeviceIndex cache) => Annotations.Where(a => a.HasTags &&
        a.DeviceId == cache.Device.Id && (cache.Browsable is not { } scan ||
        !scan.Presets.TryGetValue(a.Slot, out var preset) || !a.Matches(cache.Device.Id, preset, scan.Firmware)));

    public PresetAnnotation GetOrCreate(Guid deviceId, IndexedPreset preset, string? firmware)
    {
        if (preset.NameOnlyEmpty) { throw new InvalidOperationException("This slot was reported empty; there is no saved preset content to tag."); }
        var existing = Find(deviceId, preset, firmware);
        if (existing is not null) { return existing; }
        var annotation = new PresetAnnotation { DeviceId = deviceId, Slot = preset.Slot, Name = preset.Name, ContentSha256 = preset.ContentSha256, Firmware = firmware, SceneNames = preset.SceneNames.ToArray() };
        Annotations.Add(annotation);
        return annotation;
    }

    public void Reattach(Guid annotationId, IndexedPreset target, string? firmware, IReadOnlyDictionary<int, int> sceneMapping)
    {
        if (target.NameOnlyEmpty) { throw new InvalidOperationException("Choose a populated preset for these tags."); }
        var source = Annotations.Single(a => a.Id == annotationId);
        if (sceneMapping.Any(pair => pair.Key is < 0 or > 7 || pair.Value is < 0 or > 7))
        { throw new ArgumentException("Scene numbers must be between 1 and 8.", nameof(sceneMapping)); }
        if (source.SceneTags.Any(pair => pair.Value.Count > 0 && !sceneMapping.ContainsKey(pair.Key)))
        { throw new ArgumentException("Choose a destination for every tagged scene.", nameof(sceneMapping)); }
        var priorDestination = Find(source.DeviceId, target, firmware);
        var before = new List<PresetAnnotation> { IndexJson.Clone(source) };
        if (priorDestination is not null && priorDestination.Id != source.Id) { before.Add(IndexJson.Clone(priorDestination)); }
        Annotations.Remove(source);
        bool alreadyTagged = Find(source.DeviceId, target, firmware) is not null;
        var destination = GetOrCreate(source.DeviceId, target, firmware);
        if (!alreadyTagged) { destination.Id = source.Id; }
        destination.Tags = NormalizeTags(destination.Tags.Concat(source.Tags));
        foreach (var (scene, tags) in source.SceneTags)
        {
            if (tags.Count == 0) { continue; }
            int mapped = sceneMapping[scene];
            destination.SceneTags[mapped] = NormalizeTags(destination.SceneTags.GetValueOrDefault(mapped, []).Concat(tags));
        }
        ReviewHistory.Add(new() { Before = before, After = [IndexJson.Clone(destination)] });
        if (ReviewHistory.Count > 20) { ReviewHistory.RemoveAt(0); }
    }

    public void UndoReview()
    {
        if (ReviewHistory.Count == 0) { return; }
        var change = ReviewHistory[^1];
        var ids = change.Before.Concat(change.After).Select(a => a.Id).ToHashSet();
        var current = Annotations.Where(a => ids.Contains(a.Id)).ToArray();
        if (current.Length != change.After.Count || change.After.Any(expected =>
            current.SingleOrDefault(a => a.Id == expected.Id) is not { } actual ||
            JsonSerializer.Serialize(actual, IndexJson.Options) != JsonSerializer.Serialize(expected, IndexJson.Options)))
        { throw new InvalidOperationException("These tags were edited after the reassignment. Undo is unavailable so those edits are preserved."); }
        Annotations.RemoveAll(a => ids.Contains(a.Id));
        Annotations.AddRange(IndexJson.Clone(change.Before));
        ReviewHistory.RemoveAt(ReviewHistory.Count - 1);
    }

    public static List<string> NormalizeTags(IEnumerable<string> tags) => tags.Select(t => t.Trim())
        .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void Validate()
    {
        if (PortableAmpReferences is null) { throw new InvalidDataException("Invalid portable amp references."); }
        PortableAmpReferences = AmpReferenceStore.Validate(PortableAmpReferences);
        if (SchemaVersion != 1 || ProfileId == Guid.Empty || Devices is null || Annotations is null ||
            ReviewHistory is null || PortableSnapshots is null || Devices.Any(d => d is null) ||
            ReviewHistory.Any(h => h is null || h.Before is null || h.After is null || h.Before.Count is < 1 or > 2 || h.After.Count != 1) ||
            Devices.Select(d => d.Id).Distinct().Count() != Devices.Count ||
            SelectedDeviceId is Guid selected && Devices.All(d => d.Id != selected) ||
            Devices.Any(d => d.Id == Guid.Empty || string.IsNullOrWhiteSpace(d.Name) || !Enum.IsDefined(d.Variant) ||
                d.Firmware is not null && !Version.TryParse(d.Firmware, out _)) ||
            Annotations.Concat(ReviewHistory.SelectMany(h => h.Before.Concat(h.After))).Any(a => a is null || a.Tags is null || a.SceneTags is null ||
                a.SceneNames is null || a.ContentSha256 is null || a.Tags.Any(t => t is null) || Devices.All(d => d.Id != a.DeviceId) ||
                a.SceneTags.Any(p => p.Key is < 0 or > 7 || p.Value is null || p.Value.Any(t => t is null))))
        { throw new InvalidDataException("The profile's Preset Index data is invalid or uses a newer format."); }
        foreach (var snapshot in PortableSnapshots)
        {
            IndexJson.Validate(snapshot);
            if (Devices.All(d => d.Id != snapshot.Device.Id || d.Variant != snapshot.Device.Variant))
            { throw new InvalidDataException("An exported snapshot does not match this profile's device libraries."); }
        }
    }
}

public static class IndexJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 48 };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
    public static IndexProfile ReadProfile(JsonElement? json)
    {
        var profile = json?.Deserialize<IndexProfile>(Options) ?? new IndexProfile();
        profile.Validate();
        // Earlier files could hold one library without a selection. Multiple unselected
        // references stay unassigned rather than guessing which hardware the user intends.
        if (profile.SelectedDeviceId is null && profile.Devices.Count == 1) { profile.SelectedDeviceId = profile.Devices[0].Id; }
        return profile;
    }
    public static JsonElement ToElement(IndexProfile profile) { profile.Validate(); return JsonSerializer.SerializeToElement(profile, Options); }

    public static void Validate(DeviceIndex cache)
    {
        if (cache is null || cache.SchemaVersion != 1 || cache.Device is null || cache.Device.Id == Guid.Empty ||
            !Enum.IsDefined(cache.Device.Variant) || string.IsNullOrWhiteSpace(cache.Device.Name) ||
            cache.Device.Firmware is not null && !Version.TryParse(cache.Device.Firmware, out _) ||
            cache.PresetMapping is { IsValid: false })
        { throw new InvalidDataException("Unsupported device index."); }
        var definition = FractalDeviceDefinition.For(cache.Device.Variant);
        foreach (var scan in new[] { cache.Committed, cache.LastAttempt }.OfType<IndexScan>())
        {
            if (scan.FirmwareConfirmation is { } confirmation &&
                (!Version.TryParse(confirmation.Firmware, out _) || confirmation.CheckedAt == default ||
                confirmation.Compared < 1 || confirmation.Compared > definition.PresetSlots ||
                confirmation.Matched < 0 || confirmation.Matched > confirmation.Compared ||
                scan.Firmware is not null && !AmpBrowserCatalog.SameFirmware(scan.Firmware, confirmation.Firmware)))
            { throw new InvalidDataException("Invalid saved scan firmware confirmation."); }
            if (scan.Presets is null || scan.Errors is null || scan.Presets.Count > definition.PresetSlots ||
                scan.Status is not ("Complete" or "Scanning" or "Partial" or "Cancelled" or "Failed") ||
                scan.Presets.Any(p => p.Key < 0 || p.Key >= definition.PresetSlots || p.Value is null || p.Value.Slot != p.Key ||
                    p.Value.Variant != cache.Device.Variant || p.Value.Name is null || p.Value.ContentSha256 is null ||
                    p.Value.NameOnlyEmpty && (p.Value.Name.Trim() != "<EMPTY>" || p.Value.ContentSha256.Length != 0 || p.Value.Amps is null || p.Value.Amps.Length != 0) ||
                    p.Value.SceneNames is null || p.Value.SceneNames.Length != 8 || p.Value.SceneNames.Any(n => n is null) ||
                    p.Value.Amps is null || p.Value.Amps.Length > definition.MaxAmpBlocks || p.Value.Amps.Any(a => a is null ||
                        a.BlockNumber < 1 || a.BlockNumber > definition.MaxAmpBlocks ||
                        a.Channels is null || a.Channels.Length != 4 || a.Channels.Where((c, i) => c is null || c.Channel != i || c.Model is null || c.Model.DisplayName is null).Any() ||
                        a.Scenes is null || a.Scenes.Length != 8 || a.Scenes.Any(s => s is null || s.Channel is < 0 or > 3))))
            { throw new InvalidDataException("Invalid saved preset index data."); }
        }
        if (cache.Committed is { } complete && (complete.Presets.Count != definition.PresetSlots || complete.Errors.Count != 0 || complete.Status != "Complete"))
        { throw new InvalidDataException("A complete index must contain every preset slot."); }
    }
}

/// <summary>Shared per-device JSON. Profile tags are saved separately by the host.</summary>
public sealed class IndexLibrary(string directory)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (DateTime Written, DateTime Created, long Length, IndexLibrarySummary Summary)> _summaries = new();
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private string FilePath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".json");
    public DeviceIndex? Load(Guid id)
    {
        string path = FilePath(id);
        if (!File.Exists(path)) { return null; }
        if (new FileInfo(path).Length > 32 * 1024 * 1024) { throw new InvalidDataException("Device index is too large."); }
        var cache = JsonSerializer.Deserialize<DeviceIndex>(File.ReadAllText(path), IndexJson.Options)
            ?? throw new InvalidDataException("Device index is empty.");
        IndexJson.Validate(cache);
        if (cache.Device.Id != id) { throw new InvalidDataException("Device index identity mismatch."); }
        RestoreCheckpoints(cache);
        return cache;
    }
    public IndexLibrarySummary? LoadSummary(Guid id)
    {
        var file = new FileInfo(FilePath(id));
        if (!file.Exists) { _summaries.TryRemove(id, out _); return null; }
        if (_summaries.TryGetValue(id, out var entry) && entry.Written == file.LastWriteTimeUtc &&
            entry.Created == file.CreationTimeUtc && entry.Length == file.Length) { return entry.Summary; }
        var cache = Load(id);
        if (cache is null) { _summaries.TryRemove(id, out _); return null; }
        var summary = new IndexLibrarySummary(cache.Device, cache.PresetMapping);
        // Record the stamp from before the read. A concurrent replacement will
        // invalidate the next lookup rather than caching old data with a new stamp.
        _summaries[id] = (file.LastWriteTimeUtc, file.CreationTimeUtc, file.Length, summary);
        return summary;
    }
    public IReadOnlyList<IndexDevice> ListDevices()
    {
        if (!Directory.Exists(DirectoryPath)) { return []; }
        var ids = Directory.EnumerateFiles(DirectoryPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension).Where(name => Guid.TryParseExact(name, "N", out _))
            .Select(name => Guid.ParseExact(name!, "N")).ToHashSet();
        foreach (var id in _summaries.Keys) { if (!ids.Contains(id)) { _summaries.TryRemove(id, out _); } }
        return ids.Select(LoadSummary).OfType<IndexLibrarySummary>().Select(summary => summary.Device).OrderBy(d => d.Name).ToArray();
    }
    public DeviceIndex SaveDevice(IndexDevice device, DevicePresetMapping? mapping = null)
    {
        string name = device.Name.Trim();
        if (name.Length is 0 or > 80 || name.Any(char.IsControl))
        { throw new ArgumentException("Enter a library name of 1–80 characters without control characters."); }
        var cache = Load(device.Id);
        if ((cache is null || !string.Equals(cache.Device.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) &&
            ListDevices().Any(d => d.Id != device.Id && string.Equals(d.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        { throw new ArgumentException($"A library named '{name}' already exists. Select it from the list or choose a different name."); }
        if (cache is not null && cache.Device.Variant != device.Variant)
        { throw new InvalidOperationException("Create a new library for a different device model."); }
        device = device with { Name = name };
        cache ??= new DeviceIndex { Device = device };
        cache.Device = device;
        if (mapping is not null) { cache.PresetMapping = mapping; }
        Save(cache);
        return cache;
    }
    public void Save(DeviceIndex cache)
    {
        IndexJson.Validate(cache);
        Directory.CreateDirectory(DirectoryPath);
        string path = FilePath(cache.Device.Id), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache, IndexJson.Options));
            if (File.Exists(path)) { File.Copy(path, path + ".bak", true); }
            File.Move(temporary, path, true);
            _summaries.TryRemove(cache.Device.Id, out _);
            // A crash between replacement and deletion is harmless: the journal
            // belongs to a scan ID and only applies to an unfinished Scanning scan.
            File.Delete(JournalPath(cache.Device.Id));
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
    public void Delete(Guid id)
    {
        // Exact GUID-derived files only; the host removes profile references first.
        File.Delete(FilePath(id));
        File.Delete(FilePath(id) + ".bak");
        File.Delete(JournalPath(id));
        _summaries.TryRemove(id, out _);
    }

    private string JournalPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".scan.jsonl");
    private sealed record Checkpoint(Guid ScanId, int Slot, IndexedPreset? Preset, string? Error);
    private static readonly JsonSerializerOptions JournalOptions = new(IndexJson.Options) { WriteIndented = false };

    public void SaveCheckpoint(DeviceIndex cache, int slot)
    {
        var scan = cache.LastAttempt ?? throw new InvalidOperationException("No scan is active.");
        if (scan.Status != "Scanning") { throw new InvalidOperationException("Only a running scan can append a checkpoint."); }
        var checkpoint = new Checkpoint(scan.Id, slot, scan.Presets.GetValueOrDefault(slot), scan.Errors.GetValueOrDefault(slot));
        if (slot < 0 || slot >= FractalDeviceDefinition.For(cache.Device.Variant).PresetSlots ||
            checkpoint.Preset is { } preset && preset.Slot != slot ||
            (checkpoint.Preset is null) == (checkpoint.Error is null))
        { throw new InvalidDataException("A checkpoint must contain exactly one valid slot result."); }
        using var stream = new FileStream(JournalPath(cache.Device.Id), FileMode.Append, FileAccess.Write, FileShare.Read);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(checkpoint, JournalOptions) + "\n");
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void RestoreCheckpoints(DeviceIndex cache)
    {
        if (cache.LastAttempt is not { Status: "Scanning" } scan) { return; }
        string path = JournalPath(cache.Device.Id);
        if (!File.Exists(path)) { return; }
        if (new FileInfo(path).Length > 32 * 1024 * 1024) { throw new InvalidDataException("Scan journal is too large."); }
        string text = File.ReadAllText(path);
        int start = 0, end;
        // A torn final record has no newline. Completed records remain recoverable.
        while ((end = text.IndexOf('\n', start)) >= 0)
        {
            var checkpoint = JsonSerializer.Deserialize<Checkpoint>(text.AsSpan(start, end - start), JournalOptions)
                ?? throw new InvalidDataException("Invalid scan checkpoint.");
            start = end + 1;
            if (checkpoint.ScanId != scan.Id) { continue; }
            if (checkpoint.Slot < 0 || checkpoint.Slot >= FractalDeviceDefinition.For(cache.Device.Variant).PresetSlots ||
                checkpoint.Preset is { } preset && preset.Slot != checkpoint.Slot ||
                (checkpoint.Preset is null) == (checkpoint.Error is null))
            { throw new InvalidDataException("Invalid scan checkpoint."); }
            scan.Presets.Remove(checkpoint.Slot); scan.Errors.Remove(checkpoint.Slot);
            if (checkpoint.Preset is not null) { scan.Presets[checkpoint.Slot] = checkpoint.Preset; }
            else { scan.Errors[checkpoint.Slot] = checkpoint.Error!; }
        }
        IndexJson.Validate(cache);
    }
}

public sealed record IndexScanProgress(int Read, int Total, int Failed, int Slot, int EmptySkipped = 0);

public sealed class IndexScanner(PresetIndexReader reader, IndexLibrary library)
{
    public async Task ScanAsync(DeviceIndex cache, bool resume, Action<IndexScanProgress>? progress, CancellationToken token,
        bool publish = true, string? connectedDeviceName = null)
    {
        var device = FractalDeviceDefinition.For(cache.Device.Variant);
        Version? firmware = string.IsNullOrWhiteSpace(cache.Device.Firmware) ? null : Version.Parse(cache.Device.Firmware);
        var scan = resume && cache.LastAttempt is { Status: not "Complete" } previous && previous.Firmware == cache.Device.Firmware
            ? previous : new IndexScan { Firmware = cache.Device.Firmware };
        scan.Status = "Scanning";
        scan.ConnectedDeviceName = connectedDeviceName;
        scan.FinishedAt = null;
        cache.LastAttempt = scan;
        library.Save(cache);
        int consecutiveFailures = 0;
        try
        {
            for (int slot = 0; slot < device.PresetSlots; slot++)
            {
                token.ThrowIfCancellationRequested();
                // Recheck name-only empties on resume; a new preset may now occupy that slot.
                if (resume && scan.Presets.TryGetValue(slot, out var prior) && !prior.NameOnlyEmpty) { continue; }
                scan.Presets.Remove(slot);
                try
                {
                    scan.Presets[slot] = await reader.ReadAsync(device, firmware, slot, token).ConfigureAwait(false);
                    scan.Errors.Remove(slot);
                    consecutiveFailures = 0;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException)
                {
                    scan.Errors[slot] = ex.Message;
                    consecutiveFailures++;
                }
                library.SaveCheckpoint(cache, slot);
                progress?.Invoke(new(scan.Presets.Count, device.PresetSlots, scan.Errors.Count, slot, scan.Presets.Values.Count(p => p.NameOnlyEmpty)));
                if (consecutiveFailures >= 3) { throw new IOException("Index sync stopped after three consecutive failed reads."); }
            }
            token.ThrowIfCancellationRequested();
            scan.Status = scan.Presets.Count == device.PresetSlots && scan.Errors.Count == 0 ? "Complete" : "Partial";
            if (scan.Status == "Complete" && publish) { cache.Committed = IndexJson.Clone(scan); cache.Imported = false; }
        }
        catch (OperationCanceledException) { scan.Status = "Cancelled"; throw; }
        catch { scan.Status = "Failed"; throw; }
        finally
        {
            scan.FinishedAt = DateTimeOffset.UtcNow;
            if (scan.Status == "Complete" && publish) { cache.Committed!.FinishedAt = scan.FinishedAt; }
            library.Save(cache);
        }
    }
}

public static class IndexSearch
{
    public static bool Matches(IndexedPreset preset, PresetAnnotation? tags, IEnumerable<string> textTerms,
        IEnumerable<string> tagTerms, bool allTags, int displayOffset)
    {
        string[] attached = [.. tags?.Tags ?? [], .. tags?.SceneTags.Values.SelectMany(t => t) ?? []];
        var filterTags = tagTerms.ToArray();
        bool tagMatch = filterTags.Length == 0 || (allTags ? filterTags.All(HasTag) : filterTags.Any(HasTag));
        bool HasTag(string tag) => attached.Contains(tag, StringComparer.OrdinalIgnoreCase);
        string searchable = string.Join('\n', new[] { preset.Name, (preset.Slot + displayOffset).ToString("D3") }
            .Concat(preset.SceneNames).Concat(attached).Concat(preset.Amps.SelectMany(a => a.Channels)
                .Select(c => $"{c.Model.DisplayName} {c.Model.RealAmpFamily} {c.Model.Id}")));
        return tagMatch && textTerms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
