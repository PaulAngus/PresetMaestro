using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;

[assembly: AvaloniaTestApplication(typeof(PresetMaestro.Tests.PickerTestAppBuilder))]

namespace PresetMaestro.Tests;

public static class PickerTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PickerTestApp>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class PickerTestApp : App
{
    public override void Initialize()
    {
        base.Initialize();
        foreach (var (key, color) in new Dictionary<string, string>
        {
            ["AppBrush"] = "#E9EDF0",
            ["SurfaceBrush"] = "#FFFFFF",
            ["InsetBrush"] = "#F4F6F8",
            ["UiBorderBrush"] = "#CDD5DC",
            ["AccentBrush"] = "#1969B0",
            ["TextBrush"] = "#17232D",
            ["SecondaryBrush"] = "#5F6B75",
            ["SelectedBrush"] = "#EAF4FE",
            ["HoverBrush"] = "#F3F7FB"
        })
        {
            Resources[key] = Brush.Parse(color);
        }
    }
}

public class PresetSelectionWindowTests
{
    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout() => Dispatcher.UIThread.RunJobs();
    private static int Slot(ListBox list) => ((PresetChoice)((ListBoxItem)list.SelectedItem!).Tag!).Slot;

    [AvaloniaFact]
    public void LayoutIsFiveColumnsColumnMajorScrollableAndResizes()
    {
        var window = new PresetSelectionWindow(new Dictionary<int, string>(), 3);
        try
        {
            window.Show(); Layout();
            var list = Find<ListBox>(window, "PresetList");
            var grid = list.GetVisualDescendants().OfType<UniformGrid>().Single();
            Assert.Equal(5, grid.Columns);
            Assert.Equal(520, list.Items.Count);
            var items = list.Items.Cast<ListBoxItem>().ToArray();
            Assert.True(items[1].Bounds.X > items[0].Bounds.X);
            Assert.Equal(items[0].Bounds.Y, items[4].Bounds.Y);
            Assert.True(items[5].Bounds.Y > items[0].Bounds.Y);
            Assert.Equal(items[0].Bounds.X, items[5].Bounds.X);
            Assert.Equal(new[] { 0, 104, 208, 312, 416 }, items.Take(5).Select(i => ((PresetChoice)i.Tag!).Slot));
            Assert.Equal(new[] { 1, 105, 209, 313, 417 }, items.Skip(5).Take(5).Select(i => ((PresetChoice)i.Tag!).Slot));
            Assert.Equal(ScrollBarVisibility.Visible, ScrollViewer.GetVerticalScrollBarVisibility(list));
            var selected = items.Single(i => (i.Tag as PresetChoice)?.Slot == 3);
            Assert.True(((Grid)selected.Content!).Children[1].IsVisible);
            Assert.False(((Grid)items[2].Content!).Children[1].IsVisible);
            if (Environment.GetEnvironmentVariable("device_PICKER_SNAPSHOT") is { Length: > 0 } snapshot)
            {
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(snapshot);
            }
            window.Width = 740; Layout();
            Assert.Equal(3, grid.Columns);
            Assert.Equal(3, Slot(list));
            var resizedItems = list.Items.Cast<ListBoxItem>().ToArray();
            Assert.Equal(new[] { 0, 171, 342 }, resizedItems.Take(3).Select(i => ((PresetChoice)i.Tag!).Slot));
            Assert.True(resizedItems[3].Bounds.Y > resizedItems[0].Bounds.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SearchPreservesOrderRestoresSelectionAndHandlesNoResults()
    {
        var window = new PresetSelectionWindow(new Dictionary<int, string> { [9] = "Clean A", [2] = "Clean Z" }, 9);
        try
        {
            window.Show(); Layout();
            var search = Find<TextBox>(window, "PresetSearch");
            var list = Find<ListBox>(window, "PresetList");
            search.Text = "clean"; Layout();
            Assert.Equal(new[] { 2, 9 }, list.Items.Cast<ListBoxItem>().Select(i => i.Tag).OfType<PresetChoice>().Select(p => p.Slot).OrderBy(slot => slot));
            var matchingItems = list.Items.Cast<ListBoxItem>().Where(i => i.Tag is PresetChoice).ToArray();
            Assert.Equal(matchingItems[0].Bounds.Y, matchingItems[1].Bounds.Y);
            Assert.Equal(34, matchingItems[0].Bounds.Height);
            Assert.Equal(9, Slot(list));
            if (Environment.GetEnvironmentVariable("device_PICKER_SEARCH_SNAPSHOT") is { Length: > 0 } snapshot)
            {
                using var bitmap = window.CaptureRenderedFrame();
                bitmap!.Save(snapshot);
            }
            search.Text = "unfindable"; Layout();
            Assert.Empty(list.Items);
            Assert.False(Find<Button>(window, "ConfirmPresetSelection").IsEnabled);
            Assert.True(Find<TextBlock>(window, "NoPresetMatches").IsVisible);
            search.Text = ""; Layout();
            Assert.Equal(9, Slot(list));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardNavigatesGridAndEnterReturnsSelectedDeviceSlot()
    {
        var owner = new Window(); owner.Show();
        var window = new PresetSelectionWindow(new Dictionary<int, string>(), 3);
        try
        {
            var result = window.ShowDialog<int?>(owner); Layout();
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); // Leave search without changing its text.
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(108, Slot(Find<ListBox>(window, "PresetList")));
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(108, await result);
        }
        finally { window.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task EscapeCancelsWithoutReturningAPreset()
    {
        var owner = new Window(); owner.Show();
        var window = new PresetSelectionWindow(new Dictionary<int, string>(), 511);
        try
        {
            var result = window.ShowDialog<int?>(owner); Layout();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Null(await result);
        }
        finally { window.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task OffsetLabelsAreShownButConfirmationReturnsTheDeviceSlot()
    {
        var owner = new Window(); owner.Show();
        var window = new PresetSelectionWindow(new Dictionary<int, string> { [0] = "First" }, 0, displayOffset: 1);
        try
        {
            var result = window.ShowDialog<int?>(owner); Layout();
            var list = Find<ListBox>(window, "PresetList");
            var first = (ListBoxItem)list.Items[0]!;
            Assert.Equal("001  First", ((TextBlock)((Grid)first.Content!).Children[0]).Text);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(0, await result);
        }
        finally { window.Close(); owner.Close(); }
    }
}
