#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Core;

public sealed partial class ProfileStore
{
    public System.Text.Json.JsonElement? DeleteIndexLibrary(Guid id, string activeName, System.Text.Json.JsonElement? activeIndex)
    {
        var library = new IndexLibrary(Path.Combine(DirectoryPath, "FractalIndex"));
        var cache = library.Load(id) ?? throw new InvalidOperationException("This library no longer exists.");
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
            { throw new InvalidOperationException($"Assign another library to profile '{name}' before deleting this library."); }
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
            { throw new AggregateException("Library deletion failed and some files could not be restored.", new[] { error }.Concat(recoveryErrors)); }
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
            ?? profile.PortableSnapshots.FirstOrDefault(cache => cache.Device.Id == device.Id)
            ?? new DeviceIndex { Device = device }).Select(IndexJson.Clone).ToList();
        var relevant = profile.PortableSnapshots.SelectMany(cache => AmpBrowserCatalog.Build(cache).Families)
            .SelectMany(f => f.Variants.Select(v => v.Id).Append(f.Family.Id)).ToHashSet();
        profile.PortableAmpReferences = AmpReferenceStore.Validate(profile.PortableAmpReferences.Concat(
            new AmpReferenceStore(Path.Combine(DirectoryPath, "amp-references.json")).Load().Where(r => relevant.Contains(r.CatalogId))));
        settings.FractalIndex = IndexJson.ToElement(profile);
    }

    static partial void PrepareIndexImport(ProfileSettings settings)
    {
        if (settings.FractalIndex is null) { return; }
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        var ids = profile.Devices.ToDictionary(device => device.Id, _ => Guid.NewGuid());
        profile.Devices = profile.Devices.Select(d => d with { Id = ids[d.Id] }).ToList();
        profile.SelectedDeviceId = profile.SelectedDeviceId is Guid selected && ids.TryGetValue(selected, out var mapped) ? mapped : null;
        foreach (var annotation in profile.Annotations.Concat(profile.ReviewHistory.SelectMany(h => h.Before.Concat(h.After))))
        {
            if (!ids.TryGetValue(annotation.DeviceId, out var deviceId)) { throw new InvalidDataException("Annotation references an unknown device."); }
            annotation.DeviceId = deviceId;
        }
        foreach (var snapshot in profile.PortableSnapshots)
        {
            if (!ids.TryGetValue(snapshot.Device.Id, out var deviceId)) { throw new InvalidDataException("Snapshot references an unknown device."); }
            snapshot.Device = snapshot.Device with { Id = deviceId };
            snapshot.Imported = true;
        }
        settings.FractalIndex = IndexJson.ToElement(profile);
    }
}
#endif
