using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MouseDoubleClickSendsClickedFavoriteOnceUnlessEmpty(bool selectAnother, bool empty)
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi, [Clone(Sample), new Favorite { Id = 20, Slot = 2, Name = empty ? "" : "Lead", Preset = empty ? 0 : 42, Scene = empty ? 0 : 5 }]);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.Cast<ListBoxItem>().ToArray();
            list.SelectedItem = selectAnother ? rows[0] : null;
            Dispatcher.UIThread.RunJobs();
            var point = rows[1].TranslatePoint(new Point(80, 12), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            if (!empty)
            {
                Assert.Equal((0, 42, 5, 34, 1), midi.LastFavorite);
            }
            Assert.Equal(empty ? 0 : 1, midi.FavoriteSendCount);
        }
        finally { window.Close(); }
    }

}
