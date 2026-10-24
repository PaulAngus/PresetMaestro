#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private bool _indexRealAmpNames;
    private readonly List<Action> _indexInspectorRefreshAmpActions = [];

    private Border BuildIndexAmpNameToggle()
    {
        var fractal = BuildTagMatchButton("IndexAmpNameFractal", "Fractal");
        var real = BuildTagMatchButton("IndexAmpNameReal", "Real amp");
        AutomationProperties.SetName(fractal, "Show Fractal amp model names");
        AutomationProperties.SetName(real, "Show real amp names");
        ToolTip.SetTip(fractal, "Show Fractal model names in Amp / Channel");
        ToolTip.SetTip(real, "Show real-world amp names in Amp / Channel");
        var modes = new Grid { Width = TagMatchSegmentWidth * 2, Height = 22, ColumnDefinitions = new("*,*") };
        var thumb = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new(1), CornerRadius = new(4), IsHitTestVisible = false };
        modes.Children.Add(thumb); modes.Children.Add(fractal); Grid.SetColumn(real, 1); modes.Children.Add(real);
        void Mode(bool value)
        {
            _indexRealAmpNames = value;
            fractal.IsChecked = !value; real.IsChecked = value;
            fractal.Foreground = value ? SecondaryBrush : AccentBrush; real.Foreground = value ? AccentBrush : SecondaryBrush;
            Grid.SetColumn(thumb, value ? 1 : 0);
            foreach (var refresh in _indexInspectorRefreshAmpActions) { refresh(); }
        }
        fractal.Click += (_, _) => Mode(false); real.Click += (_, _) => Mode(true);
        var toggle = new Border
        {
            Name = "IndexAmpNameToggle",
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new(1),
            CornerRadius = new(5),
            Padding = new(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = modes,
        };
        AutomationProperties.SetName(toggle, "Scene amp names");
        toggle.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End)) { return; }
            bool realNames = e.Key is Key.Right or Key.End;
            Mode(realNames); (realNames ? real : fractal).Focus(); e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Mode(_indexRealAmpNames);
        return toggle;
    }

    private string IndexAmpDisplayName(AmpModelResolution model)
    {
        if (!_indexRealAmpNames) { return model.DisplayName; }
        var family = _ampsCatalog?.Families.FirstOrDefault(f => f.Variants.Any(v => v.ModelId == model.Id));
        if (family is null || family.Family.Confidence == "unmapped") { return $"Real amp not mapped ({model.DisplayName})"; }
        var variant = family.Variants.First(v => v.ModelId == model.Id);
        return variant.SpecificModel == variant.Name ? $"{family.Family.Manufacturer} {family.Family.Name}" : variant.SpecificModel;
    }

    private string RefreshIndexSceneAmpSummary(StackPanel summary, IndexedPreset preset, int scene)
    {
        summary.Children.Clear();
        var displayed = new List<string>();
        var accessible = new List<string>();
        if (preset.Amps.Length == 0)
        {
            summary.Children.Add(new TextBlock { Text = "No Amp blocks", FontSize = 12, Foreground = SecondaryBrush });
        }
        foreach (var amp in preset.Amps)
        {
            var state = amp.Scenes[scene];
            var model = amp.Channels.Single(channel => channel.Channel == state.Channel).Model;
            string name = IndexAmpDisplayName(model);
            string channel = $" · {(char)('A' + state.Channel)}{(state.Bypassed ? " (off)" : "")}";
            // One line per Amp block in either naming mode. Reserve the channel and
            // bypass suffix; long names ellipsize instead of growing the scene dock.
            var line = new SceneAmpNamePanel();
            var label = new TextBlock { Text = name, FontSize = 12, Foreground = SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis };
            var suffix = new TextBlock { Text = channel, FontSize = 12, Foreground = SecondaryBrush };
            ToolTip.SetTip(label, name);
            line.Children.Add(label); line.Children.Add(suffix); summary.Children.Add(line);
            displayed.Add(name + channel);
            accessible.Add($"Amp {amp.BlockNumber}: {name}, channel {(char)('A' + state.Channel)}{(state.Bypassed ? ", bypassed" : "")}" +
                (_indexRealAmpNames ? $"; Fractal: {model.DisplayName}" : ""));
        }
        string text = displayed.Count == 0 ? "No Amp blocks" : string.Join('\n', displayed);
        AutomationProperties.SetName(summary, text); ToolTip.SetTip(summary, text);
        return accessible.Count == 0 ? "No Amp blocks" : string.Join("; ", accessible);
    }
}

// Keep the suffix beside a short name, but reserve its space when a name is long.
internal sealed class SceneAmpNamePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var name = Children[0]; var suffix = Children[1];
        suffix.Measure(Size.Infinity);
        name.Measure(new Size(Math.Max(0, availableSize.Width - suffix.DesiredSize.Width), availableSize.Height));
        return new Size(name.DesiredSize.Width + suffix.DesiredSize.Width, Math.Max(name.DesiredSize.Height, suffix.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var name = Children[0]; var suffix = Children[1];
        double suffixWidth = Math.Min(finalSize.Width, suffix.DesiredSize.Width);
        double nameWidth = Math.Min(name.DesiredSize.Width, Math.Max(0, finalSize.Width - suffixWidth));
        name.Arrange(new Rect(0, 0, nameWidth, finalSize.Height));
        suffix.Arrange(new Rect(nameWidth, 0, suffixWidth, finalSize.Height));
        return finalSize;
    }
}
#endif
