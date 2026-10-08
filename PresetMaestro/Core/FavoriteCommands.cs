namespace PresetMaestro.Core;

/// <summary>Favorite edits and persistence form one recoverable operation.</summary>
internal sealed class FavoriteCommands(List<Favorite> favorites, Action<List<Favorite>> save)
{
    public void Save(int id, string name, int preset, int scene, IEnumerable<string> tags) => Change(() =>
    {
        var favorite = id == 0 ? new Favorite { Id = FavoritesManager.NextId(favorites), Slot = favorites.Count + 1 }
            : favorites.Single(f => f.Id == id);
        favorite.Name = name; favorite.Preset = preset; favorite.Scene = scene; favorite.Tags = tags.ToList();
        favorite.SceneTagSourceId = null;
        if (id == 0) { favorites.Add(favorite); }
        FavoritesManager.RenumberSlots(favorites);
    });
    public void Clear(Favorite favorite) => Change(() => FavoritesManager.ClearSlot(favorites, favorite));
    public void Remove(Favorite favorite) => Change(() => FavoritesManager.RemoveSlot(favorites, favorite));
    public void Reorder(Favorite source, Favorite target, bool after)
    {
        int sourceIndex = favorites.IndexOf(source), targetIndex = favorites.FindIndex(f => f.Id == target.Id);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) { return; }
        Change(() =>
        {
            favorites.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex) { targetIndex--; }
            if (after) { targetIndex++; }
            favorites.Insert(Math.Clamp(targetIndex, 0, favorites.Count), source);
            FavoritesManager.RenumberSlots(favorites);
        });
    }
    private void Change(Action change) => FavoritesManager.MutateAndSave(favorites, change, save);
}
