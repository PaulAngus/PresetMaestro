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
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

namespace PresetMaestro;

/// <summary>A read-only catalog picker. Selecting returns a device slot; it never sends MIDI.</summary>
public sealed class PresetSelectionWindow : Window
{
    private const int RowHeight = 34;
    private readonly IReadOnlyList<PresetChoice> _catalog;
    private readonly TextBox _search;
    private readonly ListBox _presets;
    private readonly TextBlock _count, _empty;
    private readonly Button _select, _cancel;
    private readonly UniformGrid _grid = new() { Columns = 5, VerticalAlignment = VerticalAlignment.Top };
    private int _preferredSlot;
    private bool _filtering;

    public PresetSelectionWindow(IReadOnlyDictionary<int, string> names, int currentSlot, int displayOffset = 0)
    {
        _catalog = PresetSelection.CreateCatalog(names, displayOffset);
        _preferredSlot = Math.Clamp(currentSlot, 0, 511);
        Title = "Select Preset";
        Width = 1280; Height = 780; MinWidth = 640; MinHeight = 420;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette("AppBrush");
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new("Auto,16,Auto,18,*,14,Auto") };
        root.Children.Add(new TextBlock { Text = "Select Preset", FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Palette("TextBrush") });
        _search = new TextBox { Name = "PresetSearch", Watermark = "Search by preset number or name…", MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_search, "Search by preset number or name");
        Grid.SetRow(_search, 2); root.Children.Add(_search);
        _presets = new ListBox
        {
            Name = "PresetList",
            Background = Palette("SurfaceBrush"),
            BorderThickness = new Thickness(0),
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(0),
            ItemsPanel = new FuncTemplate<Panel?>(() => _grid),
            ItemContainerTheme = new ControlTheme(typeof(ListBoxItem))
            {
                Setters = { new Setter(PaddingProperty, new Thickness(0)), new Setter(MinHeightProperty, 0d),
                    new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch), new Setter(BackgroundProperty, Brushes.Transparent) }
            }
        };
        AutomationProperties.SetName(_presets, "Presets in device order");
        ScrollViewer.SetVerticalScrollBarVisibility(_presets, ScrollBarVisibility.Visible);
        ScrollViewer.SetHorizontalScrollBarVisibility(_presets, ScrollBarVisibility.Disabled);
        KeyboardNavigation.SetTabNavigation(_presets, KeyboardNavigationMode.Once);
        _empty = new TextBlock { Name = "NoPresetMatches", Text = "No matching presets", Foreground = Palette("SecondaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var host = new Grid(); host.Children.Add(_presets); host.Children.Add(_empty);
        var frame = new Border { Child = host, Background = Palette("SurfaceBrush"), BorderBrush = Palette("UiBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true };
        Grid.SetRow(frame, 4); root.Children.Add(frame);
        var footer = new Grid { ColumnDefinitions = new("*,Auto,12,Auto"), RowDefinitions = new("Auto,Auto") };
        footer.Children.Add(new TextBlock { Text = "← ↑ ↓ →  Navigate     Enter  Select     Esc  Cancel", FontSize = 12, Foreground = Palette("SecondaryBrush"), TextWrapping = TextWrapping.Wrap });
        _count = new TextBlock { Name = "PresetCount", Foreground = Palette("SecondaryBrush"), FontSize = 12 };
        Grid.SetRow(_count, 1); footer.Children.Add(_count);
        _cancel = new Button { Name = "CancelPresetSelection", Content = "Cancel", MinWidth = 92, MinHeight = 44 };
        _select = new Button { Name = "ConfirmPresetSelection", Content = "Select Preset", MinWidth = 130, MinHeight = 44, Background = Palette("AccentBrush"), Foreground = Brushes.White };
        _cancel.Click += (_, _) => Close(null);
        _select.Click += (_, _) => ConfirmSelection();
        Grid.SetColumn(_cancel, 1); Grid.SetRowSpan(_cancel, 2); footer.Children.Add(_cancel);
        Grid.SetColumn(_select, 3); Grid.SetRowSpan(_select, 2); footer.Children.Add(_select);
        Grid.SetRow(footer, 6); root.Children.Add(footer);
        Content = root;
        _search.TextChanged += (_, _) => ApplyFilter();
        _presets.SelectionChanged += (_, _) =>
        {
            if (!_filtering && SelectedChoice is { } preset)
            {
                _preferredSlot = preset.Slot;
            }

            PaintRows();
            _select.IsEnabled = SelectedChoice != null;
        };
        _presets.DoubleTapped += (_, e) => { if (e.Source is Visual visual && IsWithinPresetItem(visual)) { ConfirmSelection(); } };
        _presets.SizeChanged += (_, _) => Reflow();
        AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => { Reflow(); _search.Focus(); RevealSelection(); };
        ApplyFilter();
    }

    private PresetChoice? SelectedChoice => (_presets.SelectedItem as ListBoxItem)?.Tag as PresetChoice;
    private static IBrush Palette(string key) => (IBrush)Application.Current!.Resources[key]!;

    private void ApplyFilter()
    {
        var filtered = PresetSelection.Filter(_catalog, _search.Text);
        var arranged = PresetSelection.ArrangeColumnMajor(filtered, _grid.Columns);
        _grid.Height = PresetSelection.RowsForColumnMajorLayout(filtered.Count, _grid.Columns) * RowHeight;
        _filtering = true;
        _presets.Items.Clear();
        foreach (var preset in arranged)
        {
            if (preset == null)
            {
                _presets.Items.Add(new ListBoxItem
                {
                    Content = new Grid { Height = RowHeight },
                    IsEnabled = false,
                    IsHitTestVisible = false,
                    Focusable = false
                });
                continue;
            }
            var label = new TextBlock { Text = preset.Label, FontSize = 14, Foreground = Palette("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            var stripe = new Border { Width = 4, Background = Palette("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, IsHitTestVisible = false };
            var row = new Grid { Height = RowHeight }; row.Children.Add(label); row.Children.Add(stripe);
            var item = new ListBoxItem { Tag = preset, Content = row, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            ToolTip.SetTip(item, preset.Label); AutomationProperties.SetName(item, preset.Label);
            item.PointerEntered += (_, _) => PaintRows(item);
            item.PointerExited += (_, _) => PaintRows();
            _presets.Items.Add(item);
        }
        int wanted = arranged.ToList().FindIndex(p => p?.Slot == _preferredSlot);
        _presets.SelectedIndex = filtered.Count == 0 ? -1 : Math.Max(0, wanted);
        _filtering = false;
        _empty.IsVisible = filtered.Count == 0;
        _select.IsEnabled = filtered.Count != 0;
        _count.Text = filtered.Count == 512 ? "512 presets · device slots 000–511" : $"{filtered.Count} of 512 presets";
        PaintRows(); RevealSelection();
    }

    private void Reflow()
    {
        int columns = PresetSelection.ColumnsForWidth(_presets.Bounds.Width - 20);
        if (_grid.Columns != columns)
        {
            _grid.Columns = columns;
            ApplyFilter();
            return;
        }
        PaintRows(); RevealSelection();
    }

    private void PaintRows(ListBoxItem? hovered = null)
    {
        for (int i = 0; i < _presets.Items.Count; i++)
        {
            var item = (ListBoxItem)_presets.Items[i]!;
            var row = (Grid)item.Content!;
            if (item.Tag is not PresetChoice)
            {
                row.Background = Palette((i / _grid.Columns) % 2 == 0 ? "SurfaceBrush" : "InsetBrush");
                continue;
            }
            bool selected = i == _presets.SelectedIndex;
            row.Background = Palette(selected ? "SelectedBrush" : item == hovered ? "HoverBrush" : (i / _grid.Columns) % 2 == 0 ? "SurfaceBrush" : "InsetBrush");
            ((TextBlock)row.Children[0]).Foreground = Palette(selected ? "AccentBrush" : "TextBrush");
            row.Children[1].IsVisible = selected;
        }
    }

    private void RevealSelection() => Dispatcher.UIThread.Post(() =>
    {
        if (IsVisible && _presets.SelectedItem is ListBoxItem item)
        {
            item.BringIntoView();
        }
    }, DispatcherPriority.Loaded);

    private void FocusSelection()
    {
        if (_presets.SelectedItem is ListBoxItem item)
        {
            item.Focus();
        }
        else
        {
            _presets.Focus();
        }

        RevealSelection();
    }

    private void ConfirmSelection()
    {
        if (SelectedChoice is { } preset)
        {
            Close(preset.Slot);
        }
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(null); e.Handled = true; return; }
        if (e.Key == Key.Enter)
        {
            if (_cancel.IsKeyboardFocusWithin)
            {
                Close(null);
            }
            else
            {
                ConfirmSelection();
            }
            e.Handled = true; return;
        }
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { _search.Focus(); _search.SelectAll(); e.Handled = true; return; }
        if (_search.IsKeyboardFocusWithin)
        {
            if (e.Key is Key.Down or Key.Up) { FocusSelection(); e.Handled = true; }
            return;
        }
        if (!_presets.IsKeyboardFocusWithin)
        {
            return;
        }

        PresetNavigation? move = e.Key switch
        {
            Key.Left => PresetNavigation.Left,
            Key.Right => PresetNavigation.Right,
            Key.Up => PresetNavigation.Up,
            Key.Down => PresetNavigation.Down,
            Key.Home => e.KeyModifiers.HasFlag(KeyModifiers.Control) ? PresetNavigation.First : PresetNavigation.Home,
            Key.End => e.KeyModifiers.HasFlag(KeyModifiers.Control) ? PresetNavigation.Last : PresetNavigation.End,
            Key.PageUp => PresetNavigation.PageUp,
            Key.PageDown => PresetNavigation.PageDown,
            _ => null
        };
        if (move == null)
        {
            return;
        }

        int target = PresetSelection.MoveIndex(_presets.SelectedIndex, _presets.Items.Count, _grid.Columns, move.Value, Math.Max(1, (int)(_presets.Bounds.Height / RowHeight) - 1));
        if (move == PresetNavigation.Last)
        {
            while (target >= 0 && ((ListBoxItem)_presets.Items[target]!).Tag is not PresetChoice)
            {
                target--;
            }
        }

        if (target >= 0 && ((ListBoxItem)_presets.Items[target]!).Tag is PresetChoice)
        {
            _presets.SelectedIndex = target;
        }

        FocusSelection(); e.Handled = true;
    }

    private static bool IsWithinPresetItem(Visual visual)
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
