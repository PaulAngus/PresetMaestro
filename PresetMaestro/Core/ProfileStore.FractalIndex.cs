#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro.Core;

public sealed partial class ProfileStore
{
    private IndexLibrary? _deviceLibrary;
    private IndexLibrary DeviceLibrary => _deviceLibrary ??= new(Path.Combine(DirectoryPath, "FractalIndex"));
    internal System.Text.Json.JsonElement? PublishConnectionLibrary(DeviceIndex cache, string activeName,
        System.Text.Json.JsonElement? activeIndex, DeviceIndex expected)
    {
        IndexJson.Validate(cache);
        var definition = FractalDeviceDefinition.For(cache.Device);
        if (cache.Committed is not { Status: "Complete", FinishedAt: not null } scan || scan.Errors.Count != 0 ||
            scan.Presets.Count != definition.PresetSlots || !Enumerable.Range(0, definition.PresetSlots).All(scan.Presets.ContainsKey))
        { throw new InvalidOperationException("A complete device sync is required before replacing saved device data."); }
        var library = new IndexLibrary(Path.Combine(DirectoryPath, "FractalIndex"));
        var previous = library.Load(cache.Device.Id);
        if (System.Text.Json.JsonSerializer.Serialize(previous, IndexJson.Options) != System.Text.Json.JsonSerializer.Serialize(expected, IndexJson.Options))
        { throw new IOException("The Default device changed during setup. Reconnect and choose its replacement again."); }
        var changes = new List<(string Path, string Original, ProfileSettings Updated)>();
        System.Text.Json.JsonElement? updatedActive = activeIndex;
        foreach (string name in ListProfiles().Append(activeName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = SettingsPath(name);
            string original = File.ReadAllText(path);
            var settings = ReadProfileSettings(original);
            bool active = string.Equals(name, activeName, StringComparison.OrdinalIgnoreCase);
            var profile = IndexJson.ReadProfile(active ? activeIndex : settings.FractalIndex);
            if (!active && profile.Devices.All(d => d.Id != cache.Device.Id)) { continue; }
            for (int i = 0; i < profile.Devices.Count; i++)
            { if (profile.Devices[i].Id == cache.Device.Id) { profile.Devices[i] = cache.Device; } }
            if (active) { profile.AssignDevice(cache.Device); }
            profile.PortableSnapshots.RemoveAll(s => s.Device.Id == cache.Device.Id);
            if (profile.SelectedDeviceId == cache.Device.Id) { settings.DeviceModel = cache.Device.Variant.ToDeviceModel(); }
            settings.FractalIndex = IndexJson.ToElement(profile);
            if (active) { updatedActive = settings.FractalIndex; }
            changes.Add((path, original, settings));
        }
        var written = new List<(string Path, string Original)>();
        try
        {
            foreach (var change in changes)
            {
                WriteJson(change.Path, change.Updated);
                written.Add((change.Path, change.Original));
            }
            library.Save(cache);
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<Exception>();
            foreach (var change in written)
            {
                try { WriteJson(change.Path, System.Text.Json.Nodes.JsonNode.Parse(change.Original)); }
                catch (Exception recovery) { recoveryErrors.Add(recovery); }
            }
            if (recoveryErrors.Count > 0) { throw new AggregateException("Device replacement failed and some profile files could not be restored.", new[] { error }.Concat(recoveryErrors)); }
            throw;
        }
        return updatedActive;
    }

    partial void ApplyLibraryMapping(ProfileSettings settings)
    {
        if (settings.FractalIndex is null) { return; }
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        if (profile.SelectedDeviceId is not Guid id) { return; }
        var summary = DeviceLibrary.LoadSummary(id);
        var mapping = summary is null ? profile.PortableSnapshots.FirstOrDefault(c => c.Device.Id == id)?.PresetMapping : summary.PresetMapping;
        if (mapping is null) { return; }
        settings.MidiChannel = mapping.MidiChannel;
        settings.DisplayOffset = mapping.DisplayOffset;
        settings.SceneCc = mapping.SceneCc;
        var device = summary?.Device ?? profile.PortableSnapshots.FirstOrDefault(c => c.Device.Id == id)?.Device;
        settings.MaxDisplayedPreset = (device is { NeedsCapacityDetection: false }
            ? FractalDeviceDefinition.For(device).PresetSlots : DevicePresets.Capacity(settings.DeviceModel)) - 1 + mapping.DisplayOffset;
    }

    public System.Text.Json.JsonElement? DeleteIndexLibrary(Guid id, string activeName, System.Text.Json.JsonElement? activeIndex)
    {
        var library = new IndexLibrary(Path.Combine(DirectoryPath, "FractalIndex"));
        var cache = library.Load(id) ?? throw new InvalidOperationException("This device no longer exists.");
        var changes = new List<(string Path, string Original, ProfileSettings Updated)>();
        System.Text.Json.JsonElement? updatedActive = activeIndex;
        // Read and validate every profile before changing any files. Portable snapshots and
        // historical references must be removed too, or a deleted library could reappear.
        foreach (string name in ListProfiles().Append(activeName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = SettingsPath(name);
            string original = File.ReadAllText(path);
            var settings = ReadProfileSettings(original);
            var profile = IndexJson.ReadProfile(name == activeName ? activeIndex : settings.FractalIndex);
            if (profile.SelectedDeviceId == id)
            { throw new InvalidOperationException($"Assign another device to profile '{name}' before deleting this device."); }
            if (profile.Devices.All(d => d.Id != id) && profile.PortableSnapshots.All(s => s.Device.Id != id)) { continue; }
            profile.Devices.RemoveAll(d => d.Id == id);
            profile.Annotations.RemoveAll(a => a.DeviceId == id);
            profile.ReviewHistory.RemoveAll(h => h.Before.Concat(h.After).Any(a => a.DeviceId == id));
            profile.PortableSnapshots.RemoveAll(s => s.Device.Id == id);
            settings.FractalIndex = IndexJson.ToElement(profile);
            if (name == activeName) { updatedActive = settings.FractalIndex; }
            changes.Add((path, original, settings));
        }
        var written = new List<(string Path, string Original)>();
        try
        {
            foreach (var change in changes)
            {
                WriteJson(change.Path, change.Updated);
                written.Add((change.Path, change.Original));
            }
            library.Delete(id);
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<Exception>();
            // A locked cache may still exist. Restore only if deletion removed it,
            // and attempt every profile rollback even when one recovery write fails.
            try { if (library.Load(id) is null) { library.Save(cache); } }
            catch (Exception recovery) { recoveryErrors.Add(recovery); }
            foreach (var change in written)
            {
                try { WriteJson(change.Path, System.Text.Json.Nodes.JsonNode.Parse(change.Original)); }
                catch (Exception recovery) { recoveryErrors.Add(recovery); }
            }
            if (recoveryErrors.Count > 0)
            { throw new AggregateException("Device deletion failed and some files could not be restored.", new[] { error }.Concat(recoveryErrors)); }
            throw;
        }
        return updatedActive;
    }

    static partial void PrepareIndexCopy(ProfileSettings settings)
    {
        if (settings.FractalIndex is null) { return; }
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        profile.ProfileId = Guid.NewGuid();
        settings.FractalIndex = IndexJson.ToElement(profile);
    }

    partial void PrepareIndexExport(ProfileSettings settings)
    {
        if (settings.FractalIndex is null) { return; }
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        var library = new IndexLibrary(Path.Combine(DirectoryPath, "FractalIndex"));
        profile.PortableSnapshots = profile.Devices.Select(device => library.Load(device.Id)
            ?? profile.PortableSnapshots.FirstOrDefault(cache => cache.Device.Id == device.Id))
            .OfType<DeviceIndex>().Select(IndexJson.Clone).ToList();
        var relevant = profile.PortableSnapshots.SelectMany(cache => AmpBrowserCatalog.Build(cache).Families)
            .SelectMany(f => f.Variants.Select(v => v.Id).Append(f.Family.Id)).ToHashSet();
        profile.PortableAmpReferences = AmpReferenceStore.Validate(profile.PortableAmpReferences.Concat(
            new AmpReferenceStore(Path.Combine(DirectoryPath, "amp-references.json")).Load().Where(r => relevant.Contains(r.CatalogId))));
        settings.FractalIndex = IndexJson.ToElement(profile);
    }

    static partial void ValidateIndexImport(ProfileSettings settings)
    {
        if (settings.FractalIndex is not null) { IndexJson.ReadProfile(settings.FractalIndex); }
    }

    partial void PrepareIndexImport(ProfileSettings settings, string profileName, Guid? libraryId, bool keepImportedLibrary)
    {
        if (settings.FractalIndex is null) { return; }
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        profile.ProfileId = Guid.NewGuid();
        if (libraryId is not null && (keepImportedLibrary || profile.AssignedDevice is null))
        { throw new ArgumentException("Choose either an existing device or the imported device."); }
        var reservedNames = DeviceLibrary.ListDevices().Select(d => d.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappedDevices = new Dictionary<Guid, IndexDevice>();
        var reused = new HashSet<Guid>();
        foreach (var device in profile.Devices)
        {
            bool assigned = device.Id == profile.SelectedDeviceId;
            Guid? target = assigned && libraryId is not null ? libraryId : assigned && keepImportedLibrary ? null : device.Id;
            var local = target is Guid id ? DeviceLibrary.LoadSummary(id)?.Device : null;
            if (assigned && libraryId is not null && local is null)
            { throw new InvalidOperationException("The selected device is no longer available. Choose a device again."); }
            if (local is not null && local.Variant != device.Variant)
            { throw new InvalidOperationException("The selected device uses a different model. Choose a device with the same model."); }
            if (local is not null)
            {
                mappedDevices.Add(device.Id, local);
                reused.Add(device.Id);
                continue;
            }
            string name = device.Name.Trim();
            if (reservedNames.Contains(name))
            {
                string context = $" · imported for {profileName[..Math.Min(profileName.Length, 36)]}";
                string basis = name[..Math.Min(name.Length, 80 - context.Length)] + context;
                name = basis[..Math.Min(basis.Length, 80)];
                for (int copy = 2; reservedNames.Contains(name); copy++)
                {
                    string suffix = $" ({copy})";
                    name = basis[..Math.Min(basis.Length, 80 - suffix.Length)] + suffix;
                }
            }
            reservedNames.Add(name);
            mappedDevices.Add(device.Id, device with { Id = Guid.NewGuid(), Name = name });
        }
        // Combining an assigned library with one of this export's historical
        // libraries may converge on the same local identity. Retain all tag history.
        var ids = mappedDevices.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
        profile.Devices = mappedDevices.Values.DistinctBy(d => d.Id).ToList();
        profile.SelectedDeviceId = profile.SelectedDeviceId is Guid selected && ids.TryGetValue(selected, out var mapped) ? mapped : null;
        foreach (var annotation in profile.Annotations.Concat(profile.ReviewHistory.SelectMany(h => h.Before.Concat(h.After))))
        {
            if (!ids.TryGetValue(annotation.DeviceId, out var deviceId)) { throw new InvalidDataException("Annotation references an unknown device."); }
            annotation.DeviceId = deviceId;
        }
        // A shared local library is authoritative. An old export cannot replace it
        // now, or resurrect its old snapshot if that library is subsequently deleted.
        profile.PortableSnapshots.RemoveAll(snapshot => reused.Contains(snapshot.Device.Id));
        foreach (var snapshot in profile.PortableSnapshots)
        {
            if (!mappedDevices.TryGetValue(snapshot.Device.Id, out var device)) { throw new InvalidDataException("Snapshot references an unknown device."); }
            snapshot.Device = device;
            snapshot.Imported = true;
        }
        settings.FractalIndex = IndexJson.ToElement(profile);
        ApplyLibraryMapping(settings);
    }
}
#endif
