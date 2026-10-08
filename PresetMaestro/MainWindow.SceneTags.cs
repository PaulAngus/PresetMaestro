#if FRACTAL_INDEX
using Avalonia.Automation;
using Avalonia.Controls;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly Dictionary<(int Preset, int Scene), (List<string> Tags, string Input)> _favoriteSceneTagDrafts = [];
    private (int Preset, int Scene)? _favoriteTagDraftTarget;

    private void PersistSharedSceneTags(IndexProfile updated)
    {
        var previous = _settings.FractalIndex;
        try
        {
            _settings.FractalIndex = IndexJson.ToElement(updated);
            _profileStore!.SaveSettingsAndFavorites(_settings, _favorites);
            _indexProfile = updated;
        }
        catch { _settings.FractalIndex = previous; throw; }
    }

    private void SynchronizeSceneTags()
    {
        if (_profileStore is null) { return; }
        var updated = IndexJson.Clone(_indexProfile);
        bool changed = false;
        FavoritesManager.MutateAndSave(_favorites,
            () => changed = SharedSceneTags.Reconcile(updated, _indexCache, _favorites, _settings.DisplayOffset),
            _ => { if (changed) { PersistSharedSceneTags(updated); } });
    }

    partial void LoadFavoriteSceneTags()
    {
        _favoriteSceneTagDrafts.Clear();
        _favoriteTagDraftTarget = null;
        FavoriteTagTargetChanged();
    }

    partial void FavoriteTagTargetChanged()
    {
        if (_favEditingId is null || _profileStore is null || _indexError is not null ||
            _indexCache?.Browsable is null) { return; }
        var selection = _favPresetSpinner.Value is decimal preset && _favSceneSpinner.Value is decimal scene
            ? ((int Preset, int Scene)?)((int)preset, (int)scene) : null;
        if (_favoriteTagDraftTarget == selection) { return; }
        if (_favoriteTagDraftTarget is { } previous)
        { _favoriteSceneTagDrafts[previous] = (_favEditingTags.ToList(), _favTagInput.Text ?? ""); }
        _favoriteTagDraftTarget = selection;
        if (selection is not { } selected) { return; }
        var target = SharedSceneTags.Resolve(_indexProfile, _indexCache, _settings.DisplayOffset, selected.Preset, selected.Scene);
        AutomationProperties.SetName(_favTagInput, target is null ? "Add favorite tag" : "Add shared scene tag");
        ToolTip.SetTip(_favTagsEditor, target is null ? "Tags for this favorite" : "Tags are shared with this scene in Preset Index and matching favorites.");
        List<string> tags;
        string input = "";
        if (_favoriteSceneTagDrafts.TryGetValue(selected, out var draft)) { tags = draft.Tags; input = draft.Input; }
        else if (target is not null)
        { tags = SharedSceneTags.Read(_indexProfile, target); }
        else if (_favoriteSceneTagDrafts.Count == 0) { return; }
        else { tags = []; }
        _favEditingTags.Clear(); _favEditingTags.AddRange(tags);
        _favTagInput.Text = input;
        RefreshFavoriteTagEditor();
    }
}
#endif
