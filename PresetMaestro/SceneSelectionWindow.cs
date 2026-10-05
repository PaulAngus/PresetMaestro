using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace PresetMaestro;

/// <summary>Selects a scene, with an explicit optional action to load it on the device.</summary>
public sealed class SceneSelectionWindow : Window
{
    private readonly ListBox _scenes;
    private readonly TextBlock _selectedSummary;
    private readonly Border[] _sceneTiles = new Border[8];
    private readonly string[] _sceneNames = new string[8];

    public SceneSelectionWindow(IReadOnlyList<string>? names, int currentScene, Func<int, bool>? goTo = null, Func<string?>? navigationUnavailable = null)
    {
        Title = "Select Scene";
        Width = 800;
        Height = 520;
        MinWidth = 640;
        MinHeight = 440;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette("AppBrush");

        var root = new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new RowDefinitions("Auto,16,Auto,12,*,18,Auto"),
        };
        root.Children.Add(new TextBlock
        {
            Text = "Select Scene",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            Foreground = Palette("TextBrush"),
        });

        var check = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Palette("AccentBrush"),
            Child = new TextBlock
            {
                Text = "✓",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _selectedSummary = new TextBlock
        {
            Name = "SelectedSceneSummary",
            FontSize = 14,
            Foreground = Palette("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var selectionBanner = new Border
        {
            Background = Palette("SelectedBrush"),
            BorderBrush = Palette("AccentBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Children = { check, _selectedSummary },
            },
        };
        Grid.SetRow(selectionBanner, 2);
        root.Children.Add(selectionBanner);

        _scenes = new ListBox
        {
            Name = "SceneList",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(0),
            ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = 4, Rows = 2 }),
            ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(PaddingProperty, new Thickness(5)),
                    new Setter(MinHeightProperty, 0d),
                    new Setter(BackgroundProperty, Brushes.Transparent),
                    new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                    new Setter(VerticalContentAlignmentProperty, VerticalAlignment.Stretch),
                },
            },
        };
        AutomationProperties.SetName(_scenes, "Scenes for the selected preset");
        for (int i = 0; i < 8; i++)
        {
            int scene = i + 1;
            string? suppliedName = names is { Count: > 0 } && i < names.Count ? names[i]?.Trim() : null;
            string displayName = string.IsNullOrWhiteSpace(suppliedName) ? $"Scene {scene}" : suppliedName;
            _sceneNames[i] = displayName;

            var number = new TextBlock
            {
                Text = scene.ToString(),
                FontSize = 22,
                FontWeight = FontWeight.Bold,
                Foreground = Palette("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var name = new TextBlock
            {
                Name = $"SceneName{scene}",
                Text = displayName,
                FontSize = 13,
                Foreground = Palette("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*") };
            content.Children.Add(number);
            Grid.SetColumn(name, 1);
            content.Children.Add(name);

            var tile = new Border
            {
                MinHeight = 72,
                Padding = new Thickness(16, 8),
                Background = Palette("SurfaceBrush"),
                BorderBrush = Palette("UiBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = content,
            };
            _sceneTiles[i] = tile;

            var item = new ListBoxItem { Tag = scene, Content = tile };
            AutomationProperties.SetName(item, $"Scene {scene}: {displayName}");
            _scenes.Items.Add(item);
        }
        _goTo = new DeviceGoToButton("Go to scene", "GoToSceneSelection", () => (_scenes.SelectedItem as ListBoxItem)?.Tag as int?, goTo, navigationUnavailable) { MinHeight = 44 };
        _scenes.SelectedIndex = Math.Clamp(currentScene, 1, 8) - 1;
        _scenes.SelectionChanged += (_, _) => UpdateSelectionDisplay();
        _scenes.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual visual && IsWithinSceneItem(visual))
            {
                if (goTo is null) { ConfirmSelection(); } else { _goTo.Go(); }
                e.Handled = true;
            }
        };
        Grid.SetRow(_scenes, 4);
        root.Children.Add(_scenes);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new("Auto,12,Auto") };
        var help = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "8 scenes", FontSize = 12, Foreground = Palette("SecondaryBrush") },
                new Border { Width = 1, Height = 18, Background = Palette("UiBorderBrush") },
                new TextBlock { Text = goTo is null ? "Use ← → ↑ ↓ to change, Enter to select" : "Double-click to go to a scene. Select Scene uses it in the favorite.", FontSize = 13, Foreground = Palette("SecondaryBrush"), TextWrapping = TextWrapping.Wrap },
            },
        };
        footer.Children.Add(help);
        Grid.SetColumnSpan(help, 2);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(_goTo);
        var cancel = new Button { Name = "CancelSceneSelection", Content = "Cancel", MinWidth = 92, MinHeight = 44 };
        var select = new Button
        {
            Name = "ConfirmSceneSelection",
            Content = "Select Scene",
            MinWidth = 130,
            MinHeight = 44,
            Background = Palette("AccentBrush"),
            Foreground = Brushes.White,
        };
        cancel.Click += (_, _) => Close(null);
        select.Click += (_, _) => ConfirmSelection();
        actions.Children.Add(cancel);
        actions.Children.Add(select);
        Grid.SetColumn(actions, 1);
        Grid.SetRow(actions, 2);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 6);
        root.Children.Add(footer);

        Content = root;
        UpdateSelectionDisplay();
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => (_scenes.SelectedItem as ListBoxItem)?.Focus();
    }

    private static IBrush Palette(string key) => (IBrush)Application.Current!.Resources[key]!;
    private readonly DeviceGoToButton _goTo;

    private void UpdateSelectionDisplay()
    {
        int selectedScene = (_scenes.SelectedItem as ListBoxItem)?.Tag as int? ?? 1;
        string selectedName = _sceneNames[selectedScene - 1];
        _goTo?.Refresh();
        _selectedSummary.Text = selectedName == $"Scene {selectedScene}"
            ? $"Selected: Scene {selectedScene}"
            : $"Selected: Scene {selectedScene} — {selectedName}";

        for (int i = 0; i < _sceneTiles.Length; i++)
        {
            bool selected = i + 1 == selectedScene;
            _sceneTiles[i].Background = Palette(selected ? "SelectedBrush" : "SurfaceBrush");
            _sceneTiles[i].BorderBrush = Palette(selected ? "AccentBrush" : "UiBorderBrush");
            _sceneTiles[i].BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void ConfirmSelection()
    {
        if (_scenes.SelectedItem is ListBoxItem { Tag: int scene })
        {
            Close(scene);
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_goTo.IsKeyboardFocusWithin) { _goTo.Go(); } else { ConfirmSelection(); }
            e.Handled = true;
        }
    }

    private static bool IsWithinSceneItem(Visual visual)
    {
        for (Visual? current = visual; current != null; current = current.GetVisualParent())
        {
            if (current is ListBoxItem)
            {
                return true;
            }
        }

        return false;
    }
}
