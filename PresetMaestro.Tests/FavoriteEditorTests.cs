using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.Midi;
using PresetNameSync.Core;
using System.Reflection;
using System.Text.Json;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    private static readonly Favorite Sample = new()
    {
        Id = 10,
        Slot = 1,
        Name = "Clean",
        Tags = ["bright", "dry"],
        Preset = 101,
        Scene = 2,
    };

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Field<T>(MainWindow window, string name) where T : class =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static T? FieldOrDefault<T>(MainWindow window, string name) where T : class =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } field
            ? field.GetValue(window) as T
            : null;

    private static void Invoke(MainWindow window, string name, params object[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);

    private static void SetField(MainWindow window, string name, object? value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static MainWindow CreateWindow(
        FakeMidi? midi = null,
        List<Favorite>? favorites = null,
        Func<int, TimeSpan, CancellationToken, Task<PresetNameResult>>? query = null,
        Action<List<Favorite>>? saveFavorites = null,
        Func<string, string, string, string, Task<bool>>? confirm = null,
        AppSettings? settings = null,
        Func<PresetSelectionWindow, Task<int?>>? presetPicker = null,
        Func<SceneSelectionWindow, Task<int?>>? scenePicker = null) =>
        new(settings ?? new AppSettings { Theme = "Light" }, favorites ?? [Clone(Sample)], midi ?? new FakeMidi(), query,
            _ => { }, saveFavorites ?? (_ => { }), confirm, presetPicker, scenePicker);

    [AvaloniaFact]
    public void HeaderUsesPresetMaestroBrandingWithStandardIconSpacing()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var identity = Find<StackPanel>(window, "AppIdentity");
            var logo = Find<Image>(window, "AppLogo");
            var title = Find<TextBlock>(window, "AppTitle");

            Assert.Equal("Preset Maestro", title.Text);
            Assert.NotNull(logo.Source);
            Assert.Equal(32, logo.Width);
            Assert.Equal(32, logo.Height);
            Assert.Equal(18, title.FontSize);
            Assert.Equal(12, identity.Spacing);
            Assert.Equal(Orientation.Horizontal, identity.Orientation);
        }
        finally { window.Close(); }
    }

    private static Favorite Clone(Favorite favorite) => new()
    {
        Id = favorite.Id,
        Slot = favorite.Slot,
        Name = favorite.Name,
        Tags = favorite.Tags.ToList(),
        Preset = favorite.Preset,
        Scene = favorite.Scene,
    };

    [AvaloniaFact]
    public void NavigationSelectsEntryModeAndConfigHasNoModeRadios()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Preset", Field<object>(window, "_mode").ToString());
            Click(Find<Button>(window, "NavFavorites"));
            Assert.Equal("Favorite", Field<object>(window, "_mode").ToString());

            Click(Find<Button>(window, "NavConfig"));
            Assert.Equal("Favorite", Field<object>(window, "_mode").ToString());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<RadioButton>(),
                radio => radio.Content as string is "Presets" or "Favorites");

            Click(Find<Button>(window, "NavPresetSender"));
            Assert.Equal("Preset", Field<object>(window, "_mode").ToString());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoriteCommandButtonsAreMoreCompactThanNavigation()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var favoritesNavigation = Find<Button>(window, "NavFavorites");
            Click(favoritesNavigation);
            Dispatcher.UIThread.RunJobs();

            foreach (string name in new[] { "FavoriteDetailsToggle", "FavoriteClearCommand", "FavoriteSendCommand" })
            {
                var button = Find<Button>(window, name);
                Assert.True(button.Bounds.Height < favoritesNavigation.Bounds.Height);
                Assert.True(button.FontSize < favoritesNavigation.FontSize);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ChangingThemeKeepsConfigVisibleAndChecksTheSelectedRadio()
    {
        var window = CreateWindow(settings: new AppSettings { Theme = "Light" });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "NavConfig"));

            Field<RadioButton>(window, "_darkThemeRadio").IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Config", Field<object>(window, "_currentPage").ToString());
            Assert.True(Field<RadioButton>(window, "_darkThemeRadio").IsChecked);
            Assert.False(Field<RadioButton>(window, "_lightThemeRadio").IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoritesScreenSendsTheFavoritesPresetAndScene()
    {
        var midi = new FakeMidi { OutputOpen = true };
        var window = CreateWindow(midi);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var favoritesButton = Find<Button>(window, "NavFavorites");
            Click(favoritesButton);
            favoritesButton.Focus();
            Invoke(window, "HandleDigit", 1);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal((0, 101, 2, 34, 1), midi.LastFavorite);
            Assert.Equal(0, midi.PresetSendCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SelectedFavoriteShowsNamesOnlyAndMovesItsDetailStripWithoutMidiActivity()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 10, Slot = 1, Name = "First", Preset = 101, Scene = 2 },
            new() { Id = 20, Slot = 2, Name = "Second", Preset = 202, Scene = 3 },
        };
        var settings = SettingsWithNames(displayOffset: 1,
            presetNames: new() { [100] = "Brit 800", [201] = "Plexi" },
            sceneNames: new() { [100] = SceneNames(2, "Lead"), [201] = SceneNames(3, "Rhythm") });
        var midi = new FakeMidi { OutputOpen = true };
        int queries = 0;
        var window = CreateWindow(midi, favorites, (_, _, _) =>
        {
            queries++;
            throw new InvalidOperationException("Selection must not query the device.");
        }, settings: settings);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.OfType<ListBoxItem>().ToArray();

            Assert.All(rows, row => Assert.False(DetailStrip(row).IsVisible));

            list.SelectedItem = rows[0];
            Dispatcher.UIThread.RunJobs();
            Assert.True(DetailStrip(rows[0]).IsVisible);
            Assert.Equal("Brit 800 | Lead", DetailText(rows[0]).Text);
            Assert.DoesNotContain(rows[0].GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "FavoriteCategoryDetail");
            Assert.DoesNotContain("Preset", DetailText(rows[0]).Text);
            Assert.DoesNotContain("Scene", DetailText(rows[0]).Text);
            Assert.DoesNotContain("101", DetailText(rows[0]).Text);
            Assert.DoesNotContain("2", DetailText(rows[0]).Text);
            Assert.Contains(rows[0].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "101");
            Assert.Contains(rows[0].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "2");
            Assert.Equal(((Control)rows[0].Content!).Bounds.Width, DetailStrip(rows[0]).Bounds.Width, 1);
            Assert.InRange(DetailStrip(rows[0]).Bounds.Height, 1, 20);
            Assert.Equal(HorizontalAlignment.Right, DetailText(rows[0]).HorizontalAlignment);
            Assert.Equal(0, Grid.GetColumn(DetailText(rows[0])));
            Assert.Equal(TextWrapping.Wrap, DetailText(rows[0]).TextWrapping);
            Assert.Equal(TextTrimming.None, DetailText(rows[0]).TextTrimming);
            Assert.Equal(Brushes.Transparent, DetailStrip(rows[0]).Background);
            Assert.NotEqual(Brushes.Transparent, ((Grid)((Grid)rows[0].Content!).Children[0]!).Background);

            list.SelectedItem = rows[1];
            Assert.False(DetailStrip(rows[0]).IsVisible);
            Assert.True(DetailStrip(rows[1]).IsVisible);
            Assert.Equal("Plexi | Rhythm", DetailText(rows[1]).Text);
            Assert.DoesNotContain(rows[1].GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "FavoriteCategoryDetail");

            list.SelectedItem = null;
            Assert.All(rows, row => Assert.False(DetailStrip(row).IsVisible));
            Assert.Equal(0, queries);
            Assert.Equal(0, midi.TotalSendCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoriteDetailsToggleShowsDetailsForAllRowsAndCanReturnToSelectedOnly()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 10, Slot = 1, Name = "First", Preset = 101, Scene = 2 },
            new() { Id = 20, Slot = 2, Name = "Second", Preset = 202, Scene = 3 },
        };
        var settings = SettingsWithNames(displayOffset: 1,
            presetNames: new() { [100] = "Brit 800", [201] = "Plexi" },
            sceneNames: new() { [100] = SceneNames(2, "Lead"), [201] = SceneNames(3, "Rhythm") });
        var window = CreateWindow(favorites: favorites, settings: settings);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.OfType<ListBoxItem>().ToArray();
            list.SelectedItem = rows[0];
            var toggle = Find<Button>(window, "FavoriteDetailsToggle");
            var toggleIcon = Field<PathIcon>(window, "_favDetailsToggleIcon");
            double selectedOnlyIconTop = toggleIcon.Data!.Bounds.Top;

            Assert.Equal("Show preset and scene details for all favourites", AutomationProperties.GetName(toggle));
            Assert.Equal("Show preset and scene details for all favourites", ToolTip.GetTip(toggle));
            Click(toggle);
            Assert.All(rows, row => Assert.True(DetailStrip(row).IsVisible));
            Assert.All(rows, row => Assert.Equal(Brushes.Transparent, DetailStrip(row).Background));
            Assert.Equal("Plexi | Rhythm", DetailText(rows[1]).Text);
            Assert.Same(rows[0], list.SelectedItem);
            Assert.Equal("Show preset and scene details only for the selected favourite", AutomationProperties.GetName(toggle));
            Assert.Equal("Show preset and scene details only for the selected favourite", ToolTip.GetTip(toggle));
            Assert.Equal(2, toggleIcon.Data!.Bounds.Top);

            Click(toggle);
            Assert.True(DetailStrip(rows[0]).IsVisible);
            Assert.False(DetailStrip(rows[1]).IsVisible);
            Assert.Equal("Show preset and scene details for all favourites", AutomationProperties.GetName(toggle));
            Assert.Equal(selectedOnlyIconTop, toggleIcon.Data!.Bounds.Top);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetectedHardwareFavoriteUsesOnlyTheExistingLeftAccentWithoutChangingRowFill()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 10, Slot = 1, Name = "Active", Preset = 101, Scene = 2 },
            new() { Id = 20, Slot = 2, Name = "Selected", Preset = 202, Scene = 3 },
        };
        var window = CreateWindow(favorites: favorites, settings: new AppSettings { Theme = "Light", DisplayOffset = 1 });
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.OfType<ListBoxItem>().ToArray();
            var selectionAccents = Field<Dictionary<ListBoxItem, Border>>(window, "_favSelectionAccents");
            var activeAccents = Field<Dictionary<ListBoxItem, Border>>(window, "_favActiveAccents");
            var mainRows = Field<Dictionary<ListBoxItem, Grid>>(window, "_favMainRows");

            // UI state alone is insufficient: the red marker is reserved for a
            // preset-and-scene pair reported by the existing hardware poll.
            SetField(window, "_sceneSlot", 100);
            SetField(window, "_activeScene", 2);
            list.SelectedItem = rows[0];
            Invoke(window, "UpdateFavoriteRowStates", (object)null!);
            Assert.False(activeAccents[rows[0]].IsVisible);
            Assert.Equal(Color.Parse("#1969B0"), ((ISolidColorBrush)selectionAccents[rows[0]].Background!).Color);

            SetField(window, "_detectedPresetSlot", 100);
            SetField(window, "_detectedScene", 2);
            IBrush selectedFill = mainRows[rows[0]].Background!;
            Invoke(window, "UpdateFavoriteRowStates", (object)null!);
            Assert.True(selectionAccents[rows[0]].IsVisible);
            Assert.False(activeAccents[rows[0]].IsVisible);
            Assert.Equal(Color.Parse("#C84444"), ((ISolidColorBrush)selectionAccents[rows[0]].Background!).Color);
            Assert.Equal(selectedFill, mainRows[rows[0]].Background);

            list.SelectedItem = rows[1];
            Dispatcher.UIThread.RunJobs();
            Assert.True(activeAccents[rows[0]].IsVisible);
            Assert.Equal(Color.Parse("#C84444"), ((ISolidColorBrush)activeAccents[rows[0]].Background!).Color);
            Assert.True(selectionAccents[rows[1]].IsVisible);
            Assert.Equal(Color.Parse("#1969B0"), ((ISolidColorBrush)selectionAccents[rows[1]].Background!).Color);
            Assert.Equal(selectionAccents[rows[1]].Width, activeAccents[rows[0]].Width);
            Assert.Equal(selectionAccents[rows[1]].HorizontalAlignment, activeAccents[rows[0]].HorizontalAlignment);
            Assert.Equal(selectionAccents[rows[1]].Bounds.Size, activeAccents[rows[0]].Bounds.Size);
            Assert.Equal(4, activeAccents[rows[0]].Bounds.Width);
            Assert.Equal(26, activeAccents[rows[0]].Bounds.Height);
            if (Environment.GetEnvironmentVariable("FAVORITE_ACTIVE_ACCENT_SNAPSHOT") is { Length: > 0 } path)
            {
                Dispatcher.UIThread.RunJobs();
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(path);
            }

            SetField(window, "_detectedScene", 1);
            Invoke(window, "UpdateFavoriteRowStates", (object)null!);
            Assert.False(activeAccents[rows[0]].IsVisible);
            Assert.Equal(Color.Parse("#1969B0"), ((ISolidColorBrush)selectionAccents[rows[1]].Background!).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoriteDetailStripHandlesOneOrNoCachedNames()
    {
        var favorites = new List<Favorite>
        {
            new() { Id = 10, Slot = 1, Name = "Preset only", Preset = 11, Scene = 1 },
            new() { Id = 20, Slot = 2, Name = "Scene only", Preset = 22, Scene = 4 },
            new() { Id = 30, Slot = 3, Name = "No names", Preset = 33, Scene = 8 },
        };
        var settings = SettingsWithNames(displayOffset: 0,
            presetNames: new() { [11] = "Brit 800" },
            sceneNames: new() { [22] = SceneNames(4, "Lead") });
        var window = CreateWindow(favorites: favorites, settings: settings);
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavFavorites"));
            Dispatcher.UIThread.RunJobs();
            var list = Field<ListBox>(window, "_favListBox");
            var rows = list.Items.OfType<ListBoxItem>().ToArray();

            list.SelectedItem = rows[0];
            Assert.Equal("Brit 800", DetailText(rows[0]).Text);
            Assert.True(DetailStrip(rows[0]).IsVisible);

            list.SelectedItem = rows[1];
            Assert.Equal("Lead", DetailText(rows[1]).Text);
            Assert.True(DetailStrip(rows[1]).IsVisible);

            list.SelectedItem = rows[2];
            Assert.Equal(string.Empty, DetailText(rows[2]).Text);
            Assert.False(DetailStrip(rows[2]).IsVisible);
        }
        finally { window.Close(); }
    }

    private static Border DetailStrip(ListBoxItem row) =>
        row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "FavoriteDetailStrip");

    private static TextBlock DetailText(ListBoxItem row) =>
        row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "FavoriteDetailText");

    private static string[] SceneNames(int scene, string name)
    {
        var names = new string[8];
        names[scene - 1] = name;
        return names;
    }

    private static AppSettings SettingsWithNames(
        int displayOffset,
        Dictionary<int, string> presetNames,
        Dictionary<int, string[]> sceneNames)
    {
        const string input = "Test input";
        const string output = "Test output";
        string key = JsonSerializer.Serialize(new[] { input, output });
        return new AppSettings
        {
            Theme = "Light",
            DisplayOffset = displayOffset,
            MidiInputPort = input,
            MidiOutputPort = output,
            PresetNameCache = presetNames,
            SceneNameCaches = new()
            {
                [key] = sceneNames.ToDictionary(pair => pair.Key, pair => new SceneCacheEntry { Names = pair.Value }),
            },
        };
    }

    private static void ShowEditor(MainWindow window, Favorite favorite)
    {
        window.ShowFavoriteEditor(favorite, isNew: false);
        window.Show();
        Click(Find<Button>(window, "NavFavorites"));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MidiLogTextCanBeSelectedCopiedAndCleared()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name == "NavDiagnostics");
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();

            var diagnostics = Find<Button>(window, "OpenDiagnostics");

            Click(diagnostics);
            Dispatcher.UIThread.RunJobs();

            var log = Find<TextBox>(window, "MidiLogText");
            Assert.True(log.IsReadOnly);
            Assert.True(log.AcceptsReturn);

            Invoke(window, "AppendLog", "copy this line");
            Assert.Contains("copy this line", log.Text);
            log.SelectAll();
            Assert.Equal(log.Text!.Length, Math.Abs(log.SelectionEnd - log.SelectionStart));

            Click(Find<Button>(window, "ClearMidiLog"));
            Assert.Equal(string.Empty, log.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DiagnosticsBackButtonReturnsToConfig()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();

            Click(Find<Button>(window, "OpenDiagnostics"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Diagnostics", Field<object>(window, "_currentPage").ToString());

            Click(Find<Button>(window, "BackToConfig"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Config", Field<object>(window, "_currentPage").ToString());
            Assert.NotNull(Find<Button>(window, "OpenDiagnostics"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DiagnosticsShowsBuildVersionAndPlacesBackButtonBelowSignalCard()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Click(Find<Button>(window, "OpenDiagnostics"));
            Dispatcher.UIThread.RunJobs();

            var version = Find<TextBlock>(window, "DiagnosticsVersion");
            var content = Find<Grid>(window, "DiagnosticsContent");
            var signal = Find<Border>(window, "SignalDiagnosticsCard");
            var log = Find<Border>(window, "MidiLogCard");
            var back = Find<Button>(window, "BackToConfig");

            Assert.Equal($"Version: {AppVersion.Current}", version.Text);
            Assert.Equal(0, Grid.GetRow(signal));
            Assert.Equal(0, Grid.GetColumn(signal));
            Assert.Equal(0, Grid.GetRow(log));
            Assert.Equal(2, Grid.GetColumn(log));
            Assert.Equal(2, Grid.GetRow(back));
            Assert.Equal(0, Grid.GetColumn(back));
            Assert.Same(content, signal.Parent);
            Assert.Same(content, log.Parent);
            Assert.Same(content, back.Parent);

            Point versionOrigin = version.TranslatePoint(default, window)!.Value;
            Point signalOrigin = signal.TranslatePoint(default, window)!.Value;
            Point logOrigin = log.TranslatePoint(default, window)!.Value;
            Point backOrigin = back.TranslatePoint(default, window)!.Value;
            Assert.True(versionOrigin.X > logOrigin.X);
            Assert.InRange(Math.Abs(signalOrigin.Y - logOrigin.Y), 0, 0.5);
            Assert.InRange(signalOrigin.Y - (versionOrigin.Y + version.Bounds.Height), 10, 16);
            Assert.InRange(backOrigin.Y - (signalOrigin.Y + signal.Bounds.Height), 10, 16);
            Assert.InRange(Math.Abs(backOrigin.X - signalOrigin.X), 0, 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ActionsRenderAsOneThreeColumnRowInsideEditorAtMinimumWidthAndRemainAccessible()
    {
        var favorite = Clone(Sample);
        var window = CreateWindow(favorites: [favorite]);
        try
        {
            window.Width = window.MinWidth;
            ShowEditor(window, favorite);

            var card = Find<Border>(window, "FavoriteEditorCard");
            var actions = Find<Grid>(window, "FavoriteActions");
            Button[] buttons =
            [
                Find<Button>(window, "FavoriteSave"), Find<Button>(window, "FavoriteCancel"),
                Find<Button>(window, "FavoriteDelete"),
            ];

            Assert.Equal(3, actions.Children.Count);
            Assert.Equal(buttons[0].Bounds.Width, buttons[1].Bounds.Width, 1);
            Assert.Equal(buttons[1].Bounds.Width, buttons[2].Bounds.Width, 1);
            foreach (var button in buttons)
            {
                Point origin = button.TranslatePoint(default, card)!.Value;
                Assert.True(button.IsVisible);
                Assert.True(button.Focusable);
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= card.Bounds.Width);
            }
            Assert.Equal(new[] { "Save", "Cancel", "Delete" }, buttons.Select(button => button.Content));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name is "FavoriteClearSlot" or "FavoriteRemoveSlot");
            var sync = Find<Button>(window, "FavoritePresetSync");
            Point syncOrigin = sync.TranslatePoint(default, card)!.Value;
            Assert.True(syncOrigin.X >= 0 && syncOrigin.X + sync.Bounds.Width <= card.Bounds.Width);
            Assert.Equal("Sync presets", AutomationProperties.GetName(sync));
            buttons[0].Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.True(buttons[1].IsFocused);
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.True(buttons[2].IsFocused);
            if (Environment.GetEnvironmentVariable("device_FAVORITE_MIN_SNAPSHOT") is { Length: > 0 } minimumSnapshot)
            {
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(minimumSnapshot);
            }
            window.Width = 1200;
            Dispatcher.UIThread.RunJobs();
            if (Environment.GetEnvironmentVariable("device_FAVORITE_CURRENT_SNAPSHOT") is { Length: > 0 } currentSnapshot)
            {
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(currentSnapshot);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PresetAndSceneValueFieldsAreAccessiblePickerButtonsWithChevrons()
    {
        var settings = SettingsWithNames(0, new() { [101] = "Bassman Crunch" }, new() { [101] = ["Clean", "Crunch", "Lead", "Rhythm", "Solo", "Delay", "Ambient", "Outro"] });
        var favorite = Clone(Sample);
        int presetPickerCalls = 0;
        int scenePickerCalls = 0;
        var window = CreateWindow(
            favorites: [favorite],
            settings: settings,
            presetPicker: _ => { presetPickerCalls++; return Task.FromResult<int?>(null); },
            scenePicker: _ => { scenePickerCalls++; return Task.FromResult<int?>(null); });
        try
        {
            ShowEditor(window, favorite);

            var preset = Find<Button>(window, "FavoritePresetPicker");
            var scene = Find<Button>(window, "FavoriteScenePicker");

            Assert.True(preset.Focusable);
            Assert.True(scene.Focusable);
            Assert.Equal("Select preset", AutomationProperties.GetName(preset));
            Assert.Equal("Select scene", AutomationProperties.GetName(scene));
            Assert.Contains("101", Field<TextBlock>(window, "_favPresetDisplayLabel").Text);
            Assert.Contains("Crunch", Field<TextBlock>(window, "_favoriteSceneDisplay").Text);
            Assert.All(new[] { preset, scene }, picker =>
                Assert.Single(picker.GetVisualDescendants().OfType<PathIcon>()));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.Name is "FavoriteSelectPreset" or "FavoriteSelectScene");

            Click(preset);
            Dispatcher.UIThread.RunJobs();
            Click(scene);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, presetPickerCalls);
            Assert.Equal(1, scenePickerCalls);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SaveAndCancelUseExistingEditorCommands()
    {
        var favorite = Clone(Sample);
        int saves = 0;
        var window = CreateWindow(favorites: [favorite], saveFavorites: _ => saves++);
        try
        {
            ShowEditor(window, favorite);
            var card = Find<Border>(window, "FavoriteEditorCard");
            Find<TextBox>(window, "FavoriteName").Text = "Edited";
            Click(Find<Button>(window, "FavoriteSave"));
            Assert.Equal("Edited", favorite.Name);
            Assert.Equal(1, saves);
            Assert.False(card.IsVisible);

            ShowEditor(window, favorite);
            Find<TextBox>(window, "FavoriteName").Text = "Discard me";
            Click(Find<Button>(window, "FavoriteCancel"));
            Assert.Equal("Edited", favorite.Name);
            Assert.Equal(1, saves);
            Assert.False(card.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NewFavoriteUsesTheCurrentPresetAndScene()
    {
        var window = CreateWindow();
        try
        {
            typeof(MainWindow).GetField("_currentPreset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 101);
            typeof(MainWindow).GetField("_activeScene", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 4);

            Click(Field<Button>(window, "_favNewBtn"));

            Assert.Equal(101, Field<NumericUpDown>(window, "_favPresetSpinner").Value);
            Assert.Equal(4, Field<NumericUpDown>(window, "_favSceneSpinner").Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PresetSenderDisplayShowsTheCachedActiveSceneName()
    {
        var settings = new AppSettings { Theme = "Light", DisplayOffset = 1 };
        settings.PresetNameCache[174] = "High Landrons";
        settings.SceneNameCaches["test"] = new Dictionary<int, SceneCacheEntry>
        {
            [174] = new() { Names = ["Intro", "Lead"] }
        };
        var window = CreateWindow(settings: settings);
        try
        {
            typeof(MainWindow).GetField("_sceneCacheKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, "test");
            typeof(MainWindow).GetField("_sceneSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 174);
            typeof(MainWindow).GetField("_currentPreset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 175);
            typeof(MainWindow).GetField("_activeScene", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 2);

            Invoke(window, "UpdateDisplay");

            Assert.Equal("High Landrons", Field<TextBlock>(window, "_currentPresetNameLabel").Text);
            Assert.Equal("Scene 2 � Lead", Field<TextBlock>(window, "_currentSceneNameLabel").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FavoriteEditorUsesNewFavoriteButtonAndCentersAddGlyph()
    {
        var favorite = Clone(Sample);
        var window = CreateWindow(favorites: [favorite]);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            ShowEditor(window, favorite);
            var add = Find<Button>(window, "FavoriteAdd");
            Assert.Equal(HorizontalAlignment.Center, add.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, add.VerticalContentAlignment);
            var addContent = Assert.IsType<StackPanel>(add.Content);
            var glyph = Assert.Single(addContent.Children.OfType<PathIcon>());
            Assert.Equal("New", Assert.Single(addContent.Children.OfType<TextBlock>()).Text);
            Assert.Equal(12, glyph.Width);
            Assert.Equal(12, glyph.Height);
            Assert.Equal(Orientation.Horizontal, addContent.Orientation);

            var card = Find<Border>(window, "FavoriteEditorCard");
            var newFavorite = Find<Button>(window, "FavoriteNew");
            Assert.Equal("New Favorite", newFavorite.Content);
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Edit Favorite");

            Click(newFavorite);
            Assert.Equal("New Favorite", Field<TextBlock>(window, "_favEditorTitle").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DeleteDialogCancelsClearsOrRemovesAndShiftsSlots()
    {
        var favorites = new List<Favorite>
        {
            Clone(Sample),
            new() { Id = 20, Slot = 2, Name = "Lead", Preset = 202, Scene = 3 },
            new() { Id = 30, Slot = 3, Name = "Ambient", Preset = 303, Scene = 4 },
        };
        var window = CreateWindow(favorites: favorites);
        try
        {
            ShowEditor(window, favorites[0]);
            var delete = Find<Button>(window, "FavoriteDelete");
            Click(delete);
            Dispatcher.UIThread.RunJobs();

            var dialog = Find<Border>(window, "FavoriteDeleteDialog");
            Assert.True(Find<Border>(window, "FavoriteDeleteDialogBackdrop").IsVisible);
            Assert.Equal("Delete slot?", AutomationProperties.GetName(dialog));
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("shift later slots up") == true);
            Assert.True(Find<Button>(window, "FavoriteDeleteShiftUp").IsFocused);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "FavoriteDeleteDialogBackdrop").IsVisible);
            Assert.True(delete.IsFocused);

            Click(delete);
            Click(Find<Button>(window, "FavoriteDeleteCancel"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(Find<Border>(window, "FavoriteDeleteDialogBackdrop").IsVisible);
            Assert.True(delete.IsFocused);
            Assert.False(favorites[0].IsEmpty);

            Click(delete);
            Click(Find<Button>(window, "FavoriteDeleteClearSlot"));
            Assert.True(favorites[0].IsEmpty);
            Assert.Equal(1, favorites[0].Slot);

            ShowEditor(window, favorites.Single(favorite => favorite.Id == 20));
            Click(Find<Button>(window, "FavoriteDelete"));
            Click(Find<Button>(window, "FavoriteDeleteShiftUp"));
            Assert.Equal(new[] { 10, 30 }, favorites.Select(favorite => favorite.Id));
            Assert.Equal(new[] { 1, 2 }, favorites.Select(favorite => favorite.Slot));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SyncIsDisabledWhenDisconnectedWithConnectionTooltip()
    {
        var favorite = Clone(Sample);
        var window = CreateWindow(favorites: [favorite]);
        try
        {
            ShowEditor(window, favorite);
            var sync = Find<Button>(window, "FavoritePresetSync");
            Assert.False(sync.IsEnabled);
            Assert.Equal("Connect to the device to sync presets.", ToolTip.GetTip(sync));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task HeaderSyncUsesSharedCommandDisablesWhileBusyAndUpdatesPresetData()
    {
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        var favorite = Clone(Sample);
        var firstQuery = new TaskCompletionSource<bool>();
        int queryCount = 0;
        async Task<PresetNameResult> Query(int slot, TimeSpan _, CancellationToken token)
        {
            queryCount++;
            if (slot == 0)
            {
                await firstQuery.Task.WaitAsync(token);
            }

            return new PresetNameResult(slot, slot == 101 ? "New Clean" : $"Preset {slot}");
        }
        var settings = new AppSettings { Theme = "Light", PresetNameCache = new() { [101] = "Old Clean" } };
        var window = CreateWindow(midi, [favorite], Query, settings: settings);
        try
        {
            ShowEditor(window, favorite);
            var sync = Find<Button>(window, "FavoritePresetSync");
            Click(sync);
            await Task.Yield();
            Assert.False(sync.IsEnabled);
            Assert.Equal("Syncing�", sync.Content);

            firstQuery.SetResult(true);
            while (queryCount < 512 || !sync.IsEnabled)
            {
                await Task.Delay(1);
            }

            Assert.Equal(512, queryCount);
            Assert.Equal("New Clean", settings.PresetNameCache[101]);
            Assert.Contains("New Clean", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("New Clean") == true).Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SyncFailureIsReportedWithoutClosingEditorOrLosingUnsavedEdits()
    {
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        var favorite = Clone(Sample);
        var window = CreateWindow(midi, [favorite], (_, _, _) => throw new IOException("device stopped responding"));
        try
        {
            ShowEditor(window, favorite);
            Find<TextBox>(window, "FavoriteName").Text = "Unsaved name";
            Field<NumericUpDown>(window, "_favPresetSpinner").Value = 222;
            Field<NumericUpDown>(window, "_favSceneSpinner").Value = 7;
            Field<List<string>>(window, "_favEditingTags").Add("unsaved-tag");

            await window.SyncPresetNamesAsync();

            Assert.True(Find<Border>(window, "FavoriteEditorCard").IsVisible);
            Assert.Equal("Unsaved name", Find<TextBox>(window, "FavoriteName").Text);
            Assert.Equal(222, Field<NumericUpDown>(window, "_favPresetSpinner").Value);
            Assert.Equal(7, Field<NumericUpDown>(window, "_favSceneSpinner").Value);
            Assert.Equal(new[] { "bright", "dry", "unsaved-tag" }, Field<List<string>>(window, "_favEditingTags"));

            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("device stopped responding", Find<TextBlock>(window, "PresetSyncStatus").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MissingSelectedPresetIsPreservedAndReportedAfterSuccessfulSync()
    {
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        var favorite = Clone(Sample);
        var window = CreateWindow(midi, [favorite], (_, _, _) => Task.FromResult(new PresetNameResult(0, "Only returned preset")));
        try
        {
            ShowEditor(window, favorite);
            await window.SyncPresetNamesAsync();

            Assert.Equal(101, Field<NumericUpDown>(window, "_favPresetSpinner").Value);
            Click(Find<Button>(window, "NavConfig"));
            Dispatcher.UIThread.RunJobs();
            string status = Find<TextBlock>(window, "PresetSyncStatus").Text!;
            Assert.Contains("Preset 101 was not returned", status);
            Assert.Contains("selection was preserved", status);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BrowsingConfiguredPickerDoesNotSendMidiOrQueryNames()
    {
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        int queries = 0, opened = 0;
        var settings = new AppSettings { DeviceModel = DeviceModel.AxeFxIII };
        var window = CreateWindow(midi, settings: settings,
            query: (_, _, _) => { queries++; throw new InvalidOperationException("Unexpected query"); },
            presetPicker: picker =>
            {
                opened++;
                try
                {
                    picker.Show(); Dispatcher.UIThread.RunJobs();
                    var list = Find<ListBox>(picker, "PresetList");
                    Assert.Equal(1024, list.Items.Cast<ListBoxItem>().Count(i => i.Tag is PresetChoice));
                    Find<TextBox>(picker, "PresetSearch").Text = "1023";
                    picker.Width = 640; Dispatcher.UIThread.RunJobs();
                    picker.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                    picker.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
                    Assert.Equal(0, midi.TotalSendCount);
                    Assert.Equal(0, queries);
                    return Task.FromResult<int?>(null);
                }
                finally { picker.Close(); }
            });
        try
        {
            ShowEditor(window, Sample);
            Click(Find<Button>(window, "FavoritePresetPicker"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, opened);
            Assert.Equal(0, midi.TotalSendCount);
            Assert.Equal(0, queries);
        }
        finally { window.Close(); }
    }

    private sealed class FakeMidi : IMidiManager
    {
        public (int Bank, int Program, int Scene, int SceneCc, int Channel)? LastFavorite { get; private set; }
        public int PresetSendCount { get; private set; }
        public int FavoriteSendCount { get; private set; }
        public int SysExSendCount { get; private set; }
        public int SceneSendCount { get; private set; }
        public int TotalSendCount => PresetSendCount + FavoriteSendCount + SysExSendCount + SceneSendCount;
        public event EventHandler<string>? LogMessage { add { } remove { } }
        public event EventHandler<NoteOnEventArgs>? NoteOnReceived { add { } remove { } }
        public event EventHandler<byte[]>? SysexMessageReceived { add { } remove { } }
        public event EventHandler<int>? PresetChangeReceived { add { } remove { } }
        public bool InputOpen { get; set; }
        public bool OutputOpen { get; set; }
        public IReadOnlyCollection<string> ThruInputPorts => Array.Empty<string>();
        public IReadOnlyList<string> GetInputPortNames() => Array.Empty<string>();
        public IReadOnlyList<string> GetOutputPortNames() => Array.Empty<string>();
        public bool OpenInput(string portName, out string? error) { error = null; InputOpen = true; return true; }
        public bool OpenOutput(string portName, out string? error) { error = null; OutputOpen = true; return true; }
        public void CloseInput() => InputOpen = false;
        public void CloseOutput() => OutputOpen = false;
        public bool OpenThruInput(string portName, out string? error) { error = null; return true; }
        public void CloseThruInput(string portName) { }
        public void CloseAllThruInputs() { }
        public bool SendBankAndPC(int bank, int pc, int midiChannel) { PresetSendCount++; return true; }
        public bool SendFavorite(int bank, int pc, int scene, int sceneCc, int midiChannel)
        {
            FavoriteSendCount++;
            LastFavorite = (bank, pc, scene, sceneCc, midiChannel);
            return true;
        }
        public bool SendSysEx(byte[] frame) { SysExSendCount++; return true; }
        public bool SendScene(int scene, int sceneCc, int midiChannel) { SceneSendCount++; return true; }
        public void Dispose() { }
    }
}
