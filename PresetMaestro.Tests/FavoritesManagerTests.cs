using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public class FavoritesManagerTests
{
    private static List<Favorite> Favorites() =>
    [
        new() { Id = 10, Slot = 1, Name = "Clean", Category = "Set A", Tags = ["bright"], Preset = 101, Scene = 2 },
        new() { Id = 20, Slot = 2, Name = "Lead", Category = "Set B", Tags = ["lead", "gain"], Preset = 202, Scene = 5 },
        new() { Id = 30, Slot = 3, Name = "Ambient", Category = "Set B", Tags = ["wet"], Preset = 303, Scene = 7 },
    ];

    [Fact]
    public void RenumberSlots_AssignsContiguousSlotsInListOrder()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 3, Slot = 9 },
            new() { Id = 1, Slot = 2 },
            new() { Id = 2, Slot = 5 },
        };

        FavoritesManager.RenumberSlots(favorites);

        Assert.Equal(new[] { 3, 1, 2 }, favorites.Select(f => f.Id));
        Assert.Equal(new[] { 1, 2, 3 }, favorites.Select(f => f.Slot));
    }

    [Fact]
    public void ClearMiddleSlot_PreservesEverySlotNumberAndClearsAllFavoriteData()
    {
        var favorites = Favorites();
        var target = favorites[1];

        Assert.True(FavoritesManager.ClearSlot(favorites, target));

        Assert.Equal(new[] { 1, 2, 3 }, favorites.Select(f => f.Slot));
        Assert.Equal(20, target.Id);
        Assert.True(target.IsEmpty);
        Assert.Equal(string.Empty, target.Name);
        Assert.Equal(string.Empty, target.Category);
        Assert.Empty(target.Tags);
        Assert.Equal(0, target.Preset);
        Assert.Equal(0, target.Scene);
        Assert.Equal("Ambient", favorites[2].Name);
    }

    [Fact]
    public void RemoveMiddleSlot_ShiftsEveryFollowingFavoriteUp()
    {
        var favorites = Favorites();

        Assert.True(FavoritesManager.RemoveSlot(favorites, favorites[1]));

        Assert.Equal(new[] { 10, 30 }, favorites.Select(f => f.Id));
        Assert.Equal(new[] { 1, 2 }, favorites.Select(f => f.Slot));
    }

    [Fact]
    public void RemoveFinalSlot_DoesNotAffectEarlierSlots()
    {
        var favorites = Favorites();

        Assert.True(FavoritesManager.RemoveSlot(favorites, favorites[2]));

        Assert.Equal(new[] { 10, 20 }, favorites.Select(f => f.Id));
        Assert.Equal(new[] { 1, 2 }, favorites.Select(f => f.Slot));
    }

    [Fact]
    public void CancelRemoveConfirmation_MakesNoChanges()
    {
        var favorites = Favorites();
        var before = favorites.Select(f => (f.Id, f.Slot, f.Name)).ToArray();

        Assert.False(MainWindow.ApplyFavoriteSlotRemoval(favorites, favorites[1], confirmed: false));

        Assert.Equal(before, favorites.Select(f => (f.Id, f.Slot, f.Name)));
    }

    [Fact]
    public void EditPanelAndContextMenuTargets_InvokeTheRequestedBehaviors()
    {
        var editorFavorites = Favorites();
        var contextFavorites = Favorites();

        Assert.True(MainWindow.ApplyFavoriteSlotClear(editorFavorites, editorFavorites[1]));
        Assert.True(editorFavorites[1].IsEmpty);

        Assert.True(MainWindow.ApplyFavoriteSlotRemoval(contextFavorites, contextFavorites[1], confirmed: true));
        Assert.Equal(new[] { 10, 30 }, contextFavorites.Select(f => f.Id));
        Assert.Equal(new[] { 1, 2 }, contextFavorites.Select(f => f.Slot));
    }

    [Fact]
    public void ContextMenuCommand_TargetsRightClickedRowRatherThanPreviousSelection()
    {
        var favorites = Favorites();
        var previouslySelected = favorites[0];
        var rightClicked = favorites[2];

        Assert.True(MainWindow.ApplyFavoriteSlotClear(favorites, rightClicked));

        Assert.False(previouslySelected.IsEmpty);
        Assert.True(rightClicked.IsEmpty);
    }

    [Fact]
    public void ClearAndRemove_RoundTripThroughPersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"device-favorites-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "favorites.json");
        try
        {
            var favorites = Favorites();
            FavoritesManager.ClearSlot(favorites, favorites[1]);
            FavoritesManager.RemoveSlot(favorites, favorites[2]);

            FavoritesManager.Save(favorites, path);
            var reloaded = FavoritesManager.Load(path);

            Assert.Equal(new[] { 10, 20 }, reloaded.Select(f => f.Id));
            Assert.Equal(new[] { 1, 2 }, reloaded.Select(f => f.Slot));
            Assert.True(reloaded[1].IsEmpty);
            Assert.Equal(string.Empty, reloaded[1].Name);
            Assert.Equal(string.Empty, reloaded[1].Category);
            Assert.Empty(reloaded[1].Tags);
            Assert.Equal(0, reloaded[1].Preset);
            Assert.Equal(0, reloaded[1].Scene);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
