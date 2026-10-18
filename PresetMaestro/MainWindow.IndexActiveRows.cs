#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly List<(Border Accent, Control Owner, int Slot, int? Scene)> _indexPresetActiveRows = [];
    private readonly List<(Border Accent, Control Owner, int Slot, int? Scene)> _indexSceneActiveRows = [];
    private readonly List<(Border Accent, Control Owner, int Slot, int? Scene)> _indexInspectorActiveRows = [];

    private Border BuildIndexActiveAccent(Control owner, int slot, int? scene, double leadingMargin, bool inspector = false)
    {
        var accent = new Border
        {
            Name = "IndexActiveAccent",
            Width = 4,
            Background = DangerBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new(leadingMargin, 0, 0, 0),
            IsHitTestVisible = false,
            IsVisible = false,
        };
        (inspector ? _indexInspectorActiveRows : scene is null ? _indexPresetActiveRows : _indexSceneActiveRows).Add((accent, owner, slot, scene));
        UpdateIndexActiveRow(accent, owner, slot, scene);
        return accent;
    }

    partial void UpdateIndexActiveRows()
    {
        foreach (var (accent, owner, slot, scene) in _indexPresetActiveRows.Concat(_indexSceneActiveRows).Concat(_indexInspectorActiveRows))
        {
            UpdateIndexActiveRow(accent, owner, slot, scene);
        }
    }

    private void UpdateIndexActiveRow(Border accent, Control owner, int slot, int? scene)
    {
        bool active = _detectedPresetSlot == slot && (scene is null || _detectedScene == scene);
        accent.IsVisible = active;
        string description = active ? "Active on connected device" : "";
        AutomationProperties.SetHelpText(owner, description);
        if (owner is not ListBoxItem && owner.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault() is { } item)
        { AutomationProperties.SetHelpText(item, description); }
    }
}
#endif
