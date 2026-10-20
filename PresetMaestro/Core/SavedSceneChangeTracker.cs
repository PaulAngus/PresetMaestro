using PresetNameSync.Core;

namespace PresetMaestro.Core;

/// <summary>Live observations only schedule stored reads; they never supply committed names or tags.</summary>
internal sealed class SavedSceneChangeTracker
{
    private readonly HashSet<int> _edited = [];
    private readonly HashSet<int> _pending = [];
    private readonly Dictionary<int, Fm9ActivePreset> _deferred = [];
    private Fm9ActivePreset? _active;

    public void Observe(Fm9ActivePreset current, PresetScenes? saved)
    {
        if (_active is { } previous && previous.Slot != current.Slot && _edited.Contains(previous.Slot))
        { _pending.Add(previous.Slot); }
        _active = current;
        if (_deferred.TryGetValue(current.Slot, out var deferred))
        {
            if (Same(deferred, current)) { return; }
            _deferred.Remove(current.Slot);
        }
        if (current.Edited) { _edited.Add(current.Slot); }
        else if (_edited.Contains(current.Slot) || saved is null || current.PresetName != saved.PresetName ||
            !current.SceneNames.SequenceEqual(saved.Names)) { _pending.Add(current.Slot); }
    }

    public int? NextSlot => _pending.OrderBy(slot => slot).Cast<int?>()
        .FirstOrDefault(slot => _active is null || slot != _active.Slot || !_active.Edited);

    public void Complete(int slot)
    {
        _pending.Remove(slot);
        _edited.Remove(slot);
    }

    public void Defer(int slot)
    {
        Complete(slot);
        if (_active?.Slot == slot) { _deferred[slot] = _active; }
    }

    public void Reset()
    {
        _active = null; _edited.Clear(); _pending.Clear(); _deferred.Clear();
    }

    private static bool Same(Fm9ActivePreset first, Fm9ActivePreset second) => first.Slot == second.Slot &&
        first.Edited == second.Edited && first.PresetName == second.PresetName && first.SceneNames.SequenceEqual(second.SceneNames);
}
