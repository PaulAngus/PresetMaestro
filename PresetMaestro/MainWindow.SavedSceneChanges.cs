using Avalonia.Input;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetNameSync.Core;
#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;
#endif

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly SavedSceneChangeTracker _savedSceneChanges = new();
    private long _activeNamesRetryAt, _savedNamesRetryAt;
    private bool _savedSceneDecisionWaitingForFocus;

    private PresetScenes? SavedNamesFor(int slot)
    {
#if FRACTAL_INDEX
        if (_indexCache?.Device.Variant == FractalDeviceVariant.FM9 &&
            _indexCache.Committed?.Presets.TryGetValue(slot, out var preset) == true)
        { return new(slot, preset.Name, preset.SceneNames); }
#endif
        return SceneCache.TryGetValue(slot, out var cached) && cached.Source == "stored" &&
            _settings.PresetNameCache.TryGetValue(slot, out string? name) ? new(slot, name, cached.Names) : null;
    }

    private async Task CheckSavedSceneChangesAsync(Fm9ActivePreset live, CancellationToken token)
    {
        var saved = SavedNamesFor(live.Slot);
        if (saved is not null && (!SceneCache.TryGetValue(live.Slot, out var cached) || cached.Source != "stored"))
        { SaveStoredNames(saved, _sceneCacheKey); }
        _savedSceneChanges.Observe(live, saved);
        if (_savedSceneChanges.NextSlot is not int slot || Environment.TickCount64 < _savedNamesRetryAt ||
            OwnedWindows.Any() || _favEditingId is not null || _savedSceneDecisionWaitingForFocus && !IsActive) { return; }
        _savedSceneDecisionWaitingForFocus = false;
        int profile = _profileGeneration;
        long connection = _connectionGeneration;
        string key = _sceneCacheKey;
        bool Current() => !token.IsCancellationRequested && !_sceneClosing && profile == _profileGeneration &&
            connection == _connectionGeneration && key == _sceneCacheKey && _midi.InputOpen && _midi.OutputOpen &&
            _presetNamesCts is null && !_changingProfile;
        InputElement? returnFocus = null;
        try
        {
#if FRACTAL_INDEX
            if (_indexCache is not null && _profileStore is not null)
            {
                if (IndexResyncUnavailable(false) is not null || _indexCache.Committed is not { } baseline ||
                    !AmpBrowserCatalog.SameFirmware(baseline.EffectiveFirmware, _detectedDevice?.Firmware) ||
                    !baseline.Presets.TryGetValue(slot, out var before)) { return; }
                var expected = IndexJson.Clone(_indexCache);
                var definition = FractalDeviceDefinition.For(expected.Device);
                Version? firmware = Version.TryParse(_detectedDevice?.Firmware, out var parsed) ? parsed : null;
                var after = await _fractalIndexReader.ReadAsync(definition, firmware, slot, token);
                if (!Current() || _indexCache?.Device.Id != expected.Device.Id) { return; }
                var updated = IndexJson.Clone(_indexProfile);
                var annotation = updated.Find(expected.Device.Id, before, baseline.Firmware);
                var plan = SceneTagMoves.Compare(before, after, annotation);
                if (plan.HasTaggedMoves && !plan.Exact)
                {
                    if (!IsActive) { _savedSceneDecisionWaitingForFocus = true; return; }
                    // Reuse the explicit resync decision, without opening a second modal or stealing editor focus.
                    returnFocus = FocusManager?.GetFocusedElement() as InputElement;
                    bool? follow = await ConfirmSceneTagMovesAsync(after, plan, token);
                    if (!Current() || _indexCache?.Device.Id != expected.Device.Id) { return; }
                    if (follow is null) { _savedSceneChanges.Defer(slot); return; }
                    var checkedAgain = await _fractalIndexReader.ReadAsync(definition, firmware, slot, token);
                    if (!Current() || checkedAgain.ContentSha256 != after.ContentSha256) { return; }
                    if (follow == true) { plan.Apply(annotation!); }
                }
                else if (plan.HasTaggedMoves) { plan.Apply(annotation!); }

                var names = new PresetScenes(slot, after.Name, after.SceneNames);
                if (before.ContentSha256 != after.ContentSha256 || before.Name != after.Name ||
                    !before.SceneNames.SequenceEqual(after.SceneNames) || plan.HasTaggedMoves)
                {
                    var candidate = IndexJson.Clone(expected);
                    candidate.Committed!.Presets[slot] = after;
                    var settings = IndexJson.Clone(_settings);
                    var favorites = IndexJson.Clone(_favorites);
                    SharedSceneTags.Reconcile(updated, candidate, favorites, settings.DisplayOffset, importLegacy: false);
                    settings.FractalIndex = IndexJson.ToElement(updated);
                    PutSavedNames(settings, names, key);
                    if (!Current()) { return; }
                    _profileStore.SaveIndexRefresh(settings, favorites, candidate, expected);
                    _settings.FractalIndex = settings.FractalIndex;
                    _settings.PresetNameCache = settings.PresetNameCache;
                    _settings.SceneNameCaches = settings.SceneNameCaches;
                    _favorites.Clear(); _favorites.AddRange(favorites);
                    _indexProfile = updated; _indexCache = candidate; _indexViewContext = null;
                    RefreshIndexContext();
                    UpdateFavoritePresetDisplay(); PopulateFavoriteScenes(); UpdateDisplay();
                    AppendLog($"SCENES: saved preset {slot + _settings.DisplayOffset:000} refreshed" +
                        (plan.HasTaggedMoves ? "; scene tags reconciled." : "."));
                }
                else { SaveStoredNames(names, key); }
            }
            else
#endif
            {
                var names = await _presetNameClient.StoredScenesAsync(slot, token);
                if (!Current()) { return; }
                SaveStoredNames(names, key);
            }
            _savedSceneChanges.Complete(slot);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!Current()) { return; }
            _savedNamesRetryAt = Environment.TickCount64 + 30_000;
            AppendLog("SCENES: could not refresh saved names and tags; previous data kept. " + ex.Message);
        }
        finally
        {
            if (Current() && returnFocus is not null)
            {
                if (returnFocus.IsEffectivelyEnabled && returnFocus.GetVisualRoot() == this) { returnFocus.Focus(); }
#if FRACTAL_INDEX
                else if (_indexList?.IsEffectivelyVisible == true) { _indexList.Focus(); }
#endif
            }
        }
    }

    private static void PutSavedNames(AppSettings settings, PresetScenes names, string key)
    {
        settings.PresetNameCache[names.Slot] = names.PresetName;
        if (!settings.SceneNameCaches.TryGetValue(key, out var cache)) { settings.SceneNameCaches[key] = cache = []; }
        cache[names.Slot] = new SceneCacheEntry { Names = names.Names.ToArray(), Source = "stored", RetrievedAt = DateTimeOffset.UtcNow };
    }

    private void SaveStoredNames(PresetScenes names, string key)
    {
        if (_settings.PresetNameCache.GetValueOrDefault(names.Slot) == names.PresetName &&
            _settings.SceneNameCaches.TryGetValue(key, out var existing) && existing.TryGetValue(names.Slot, out var entry) &&
            entry.Source == "stored" && entry.Names.SequenceEqual(names.Names)) { return; }
        var oldPresets = _settings.PresetNameCache;
        var oldScenes = _settings.SceneNameCaches;
        _settings.PresetNameCache = new(oldPresets);
        _settings.SceneNameCaches = new(oldScenes);
        _settings.SceneNameCaches[key] = oldScenes.TryGetValue(key, out var oldCache) ? new(oldCache) : [];
        try { PutSavedNames(_settings, names, key); _saveSettings(_settings); }
        catch { _settings.PresetNameCache = oldPresets; _settings.SceneNameCaches = oldScenes; throw; }
        UpdateFavoritePresetDisplay(); PopulateFavoriteScenes(); UpdateDisplay();
    }
}
