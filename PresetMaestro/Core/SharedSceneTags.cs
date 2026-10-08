#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Core;

/// <summary>Index annotations own tags; favorites retain a portable display copy.</summary>
internal static class SharedSceneTags
{
    internal sealed record Target(Guid DeviceId, IndexedPreset Preset, string? Firmware, int Scene);

    public static Target? Resolve(IndexProfile profile, DeviceIndex? cache, int displayOffset, int preset, int scene)
    {
        if (scene is < 1 or > 8 || cache is null || profile.SelectedDeviceId != cache.Device.Id ||
            cache.Browsable is not { } scan || !scan.Presets.TryGetValue(preset - displayOffset, out var indexed) || indexed.NameOnlyEmpty)
        { return null; }
        return new(cache.Device.Id, indexed, scan.Firmware, scene - 1);
    }

    public static List<string> Read(IndexProfile profile, Target target) =>
        profile.Find(target.DeviceId, target.Preset, target.Firmware)?.SceneTags.GetValueOrDefault(target.Scene) ?? [];

    public static void Set(IndexProfile profile, Target target, IEnumerable<string> tags) =>
        profile.GetOrCreate(target.DeviceId, target.Preset, target.Firmware).SceneTags[target.Scene] = IndexProfile.NormalizeTags(tags);

    // Import each legacy favorite only once. Linked copies must never resurrect a
    // deleted tag or transfer old content's tags onto a replacement at the same slot.
    public static bool Reconcile(IndexProfile profile, DeviceIndex? cache, IList<Favorite> favorites, int displayOffset, bool importLegacy = true)
    {
        if (cache is null || profile.SelectedDeviceId != cache.Device.Id || cache.Browsable is null) { return false; }
        bool changed = false;
        foreach (var group in favorites.Where(f => !f.IsEmpty).GroupBy(f => (f.Preset, f.Scene)))
        {
            var target = Resolve(profile, cache, displayOffset, group.Key.Preset, group.Key.Scene);
            var annotation = target is null ? null : profile.Find(target.DeviceId, target.Preset, target.Firmware);
            if (target is not null && importLegacy)
            {
                var legacy = IndexProfile.NormalizeTags(group.Where(f => f.SceneTagSourceId is null).SelectMany(f => f.Tags));
                if (legacy.Count > 0)
                {
                    annotation ??= profile.GetOrCreate(target.DeviceId, target.Preset, target.Firmware);
                    var existing = annotation.SceneTags.GetValueOrDefault(target.Scene) ?? [];
                    var merged = IndexProfile.NormalizeTags(existing.Concat(legacy));
                    if (!existing.SequenceEqual(merged))
                    { annotation.SceneTags[target.Scene] = merged; changed = true; }
                }
            }
            foreach (var favorite in group)
            {
                // Favorites without indexed content continue to work independently.
                if (annotation is null && favorite.SceneTagSourceId is null) { continue; }
                var tags = annotation is null ? [] : Read(profile, target!);
                Guid? source = annotation?.Id ?? favorite.SceneTagSourceId;
                if (favorite.SceneTagSourceId != source || !favorite.Tags.SequenceEqual(tags))
                {
                    favorite.Tags = tags.ToList(); favorite.SceneTagSourceId = source; changed = true;
                }
            }
        }
        return changed;
    }
}
#endif
