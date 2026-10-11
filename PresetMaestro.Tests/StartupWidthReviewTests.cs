using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed partial class DeviceNavigationTests
{
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void OtherTableViewsRetainTheirResultsWhenResizedToTheProposedStartingWidth(string theme)
    {
        var (window, midi) = Window(theme: theme, width: 1000, height: 800);
        string[] names = ["59 Bassman Tweed 5F6-A", "65 Bassman Blackface", "61 Vibrolux Brownface", "Satriani JS410 Orange Channel", "Smallbox Friedman V30", "MorganAC20", "Clean 800 'verbs", "Plexi 100W Jumped + 4x12"];
        var metrics = new List<object>();
        try
        {
            Click(Find<Button>(window, "NavPresetIndex"));
            var cache = (DeviceIndex)typeof(MainWindow).GetField("_indexCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            foreach (int slot in cache.Committed!.Presets.Keys.ToArray())
            { cache.Committed.Presets[slot] = cache.Committed.Presets[slot] with { Name = names[slot % names.Length] }; }
            IndexView(window, 1); IndexView(window, 0);
            Review("presets", Find<ListBox>(window, "IndexPresetList"), "IndexTableHeaders");

            var favorites = (List<Favorite>)typeof(MainWindow).GetField("_favorites", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            favorites.AddRange(Enumerable.Range(0, 40).Select(slot => new Favorite
            {
                Id = slot + 1,
                Slot = slot + 1,
                Preset = slot + 1,
                Scene = slot % 8 + 1,
                Name = names[slot % names.Length],
                Tags = slot % 3 == 0 ? ["Fender", "Mid Gain"] : slot % 3 == 1 ? ["Clean"] : [],
            }));
            Click(Find<Button>(window, "NavFavorites"));
            typeof(MainWindow).GetMethod("RefreshFavoritesList", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [true, null, null]);
            Dispatcher.UIThread.RunJobs();
            Review("favorites", Find<ListBox>(window, "FavoriteTables"), "FavoriteTableHeaders");

            Click(Find<Button>(window, "NavAmps"));
            Review("amps", Find<ListBox>(window, "AmpsDirectory"), "AmpsTableHeaders");
            Assert.Empty(midi.Writes);
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is not null) { File.WriteAllText(Path.Combine(output, $"startup-width-{theme.ToLowerInvariant()}.json"), JsonSerializer.Serialize(metrics)); }
        }
        finally { window.Close(); }

        void Review(string view, ListBox list, string headerName)
        {
            var items = list.Items.Cast<object>().ToArray();
            foreach (int width in new[] { 1000, 1320, 1360 })
            {
                window.Width = width; Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()?.Dispose(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(items, list.Items.Cast<object>());
                var viewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 1);
                var headers = Find<Grid>(window, headerName).Children.ToArray();
                var labels = list.GetVisualDescendants().OfType<TextBlock>().Where(label => names.Contains(label.Text) || label.Name == "AmpFamilyName").ToArray();
                var samples = labels.DistinctBy(label => label.Text).Select(label => new { label.Text, Width = label.Bounds.Width, Fits = Fits(label) }).ToArray();
                // The accepted three-table Favorites layout permits long tagged names to truncate.
                if (width == 1360 && view != "favorites") { Assert.All(samples, sample => Assert.True(sample.Fits, $"{view}: {sample.Text}")); }
                if (view == "favorites") { Assert.Equal(width == 1360 ? 3 : 2, headers.Length); }
                metrics.Add(new { View = view, WindowWidth = width, Tables = headers.Length, TableWidth = headers[0].Bounds.Width, Samples = samples });
                string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    using var bitmap = window.CaptureRenderedFrame();
                    bitmap!.Save(Path.Combine(output, $"startup-{view}-{width}-{theme.ToLowerInvariant()}.png"));
                }
            }
        }
        static bool Fits(TextBlock label)
        {
            var natural = new TextBlock { Text = label.Text, FontSize = label.FontSize, FontFamily = label.FontFamily, FontWeight = label.FontWeight };
            natural.Measure(Size.Infinity);
            return natural.DesiredSize.Width <= label.Bounds.Width + 1;
        }
    }
}
