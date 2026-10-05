#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Core;

/// <summary>Profile/library persistence policy, independent of the Config and Index views.</summary>
internal sealed class ProfileLibraryCoordinator(AppSettings settings, IndexLibrary library, Action<AppSettings> save)
{
    public (IndexProfile Profile, DeviceIndex? Cache) Load(bool materialize)
    {
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        if (materialize)
        {
            foreach (var snapshot in profile.PortableSnapshots)
            { if (library.LoadSummary(snapshot.Device.Id) is null) { library.Save(snapshot); } }
        }
        var stored = profile.SelectedDeviceId is Guid id ? library.Load(id) : null;
        var cache = stored ?? profile.PortableSnapshots.FirstOrDefault(c => c.Device.Id == profile.SelectedDeviceId);
        // A historical reference is display context, not permission to create an empty library.
        if (cache is null && profile.AssignedDevice is { } missing) { cache = new() { Device = missing, Imported = true }; }
        if (stored is { PresetMapping: null } && materialize)
        {
            stored.PresetMapping = new(settings.MidiChannel, settings.DisplayOffset, settings.SceneCc);
            library.Save(stored);
        }
        return (profile, cache);
    }

    public IndexProfile Update(IndexProfile current, Action<IndexProfile> change)
    {
        var previous = settings.FractalIndex;
        try
        {
            var updated = IndexJson.Clone(current);
            change(updated);
            settings.FractalIndex = IndexJson.ToElement(updated);
            save(settings);
            return updated;
        }
        catch { settings.FractalIndex = previous; throw; }
    }

    public static DevicePresetMapping ResolveMapping(DeviceIndex? cache, IndexProfile current, AppSettings settings,
        IEnumerable<string> profiles, Func<string, ProfileSettings> readProfile)
    {
        if (cache is null) { return new(); }
        if (cache.PresetMapping is { } mapping) { return mapping; }
        var profile = cache.Device.Id == current.SelectedDeviceId ? ProfileSettings.From(settings) :
            profiles.Select(readProfile).FirstOrDefault(p => IndexJson.ReadProfile(p.FractalIndex).SelectedDeviceId == cache.Device.Id);
        return profile is null ? new() : new(profile.MidiChannel, profile.DisplayOffset, profile.SceneCc);
    }
}
#endif
