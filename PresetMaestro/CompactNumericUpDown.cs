using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace PresetMaestro;

// Retain Fluent's numeric input, validation and spin behavior, with smaller
// mouse targets and chevrons for the desktop Config cards.
internal sealed class CompactNumericUpDown : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ButtonSpinner>("PART_Spinner") is not { } spinner) { return; }
        // Fluent gives these template parts local widths, which ordinary styles
        // cannot override. Change their sizing without replacing the template.
        spinner.TemplateApplied += (_, args) =>
        {
            Resize(args.NameScope.Find<RepeatButton>("PART_IncreaseButton"));
            Resize(args.NameScope.Find<RepeatButton>("PART_DecreaseButton"));
        };
        spinner.ApplyTemplate();
        foreach (var button in spinner.GetVisualDescendants().OfType<RepeatButton>()) { Resize(button); }
    }

    private static void Resize(RepeatButton? button)
    {
        if (button is null) { return; }
        button.Width = button.MinWidth = 24;
        button.MinHeight = 0;
        button.Padding = default;
        if (button.Content is PathIcon icon) { icon.Width = 10; icon.Height = 5; }
    }
}
