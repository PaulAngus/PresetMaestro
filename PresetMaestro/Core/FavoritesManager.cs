using System.Text.Json;

namespace PresetMaestro.Core;

public static class FavoritesManager
{
    private static readonly string FavoritesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PresetMaestro", "favorites.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<Favorite> Load()
    {
        try
        {
            return Load(FavoritesPath);
        }
        catch { }
        return [];
    }

    public static void Save(List<Favorite> favorites)
    {
        try
        {
            Save(favorites, FavoritesPath);
        }
        catch { }
    }

    internal static List<Favorite> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var favorites = JsonSerializer.Deserialize<List<Favorite>>(
                    File.ReadAllText(path), JsonOpts)
                ?? [];
        if (favorites.Any(favorite => favorite is null))
        {
            throw new JsonException("Invalid favorite entry.");
        }
        favorites.Sort((left, right) => left.Slot != right.Slot ? left.Slot.CompareTo(right.Slot) : left.Id.CompareTo(right.Id));
        return favorites;
    }

    internal static void Save(List<Favorite> favorites, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LegacyFavoritesStorage.Write(path, JsonSerializer.Serialize(favorites, JsonOpts));
    }

    public static int NextId(IEnumerable<Favorite> favorites) =>
        favorites.Any() ? favorites.Max(f => f.Id) + 1 : 1;

    public static int NextFreeSlot(IEnumerable<Favorite> favorites)
    {
        var used = favorites.Select(f => f.Slot).ToHashSet();
        int slot = 1;
        while (used.Contains(slot))
        {
            slot++;
        }

        return slot;
    }

    public static void RenumberSlots(IEnumerable<Favorite> favorites)
    {
        int slot = 1;
        foreach (var favorite in favorites)
        {
            favorite.Slot = slot++;
        }
    }

    public static bool ClearSlot(IList<Favorite> favorites, Favorite target)
    {
        var favorite = favorites.FirstOrDefault(f => ReferenceEquals(f, target) || f.Id == target.Id);
        if (favorite is null)
        {
            return false;
        }

        favorite.Name = string.Empty;
        favorite.Tags = [];
        favorite.Preset = 0;
        favorite.Scene = 0;
        return true;
    }

    public static bool RemoveSlot(IList<Favorite> favorites, Favorite target, bool confirmed = true)
    {
        if (!confirmed)
        {
            return false;
        }

        int index = favorites.ToList().FindIndex(f => ReferenceEquals(f, target) || f.Id == target.Id);
        if (index < 0)
        {
            return false;
        }

        favorites.RemoveAt(index);
        RenumberSlots(favorites);
        return true;
    }
}
