#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private async Task<bool> ConfirmLibraryUpdateAsync(DeviceIndex cache, LibraryMatchResult match)
    {
        int unmatched = Math.Max(0, match.Compared - match.Matched - match.Failed);
        string summary = unmatched > 0 ? $"{unmatched} {(unmatched == 1 ? "preset doesn't" : "presets don't")} match."
            : match.Compared > match.Failed ? "Presets match." : "No presets could be compared.";
        if (match.Failed > 0) { summary += $" {match.Failed} could not be read."; }
        if (match.MetadataDifferences.Length > 0)
        { summary += " " + string.Join(" ", match.MetadataDifferences.Select(difference => difference.Setting + " differs.")); }
        if (_confirmOverride is not null)
        { return await _confirmOverride("Confirm device data update", summary, "Update device data", "Cancel"); }

        var owner = _connectionSyncDialog is { IsVisible: true } sync ? sync : this;
        var source = owner == this ? FocusManager?.GetFocusedElement() as Control : _connectionSyncLibrary;
        var dialog = new Window
        {
            Name = "LibraryUpdateDialog",
            Title = "Confirm device data update",
            Width = Math.Min(580, Math.Max(360, owner.Bounds.Width - 48)),
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
            FontSize = 14,
        };
        var heading = new TextBlock
        {
            Name = "LibraryUpdateSummary",
            Text = summary,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        var header = new StackPanel
        {
            Margin = new(20, 20, 20, 12),
            Spacing = 6,
            Children = { heading, DetailText($"{(cache.Imported ? "Imported device" : "Device")}: {cache.Device.Name}") },
        };
        var decision = new StackPanel { Name = "LibraryUpdateDecision", Margin = new(20, 0, 20, 20), Spacing = 12 };
        var view = IndexButton("View differences", "LibraryUpdateViewDifferences");
        var update = IndexButton("Update device data", "LibraryUpdateAccept");
        var cancel = IndexButton("Cancel", "LibraryUpdateCancel");
        decision.Children.Add(ActionRow(view, "See what changed."));
        decision.Children.Add(ActionRow(update, "Replace saved copy with device data."));
        decision.Children.Add(ActionRow(cancel, "Keep current saved copy."));
        view.IsEnabled = match.Differences.Length > 0 || match.MetadataDifferences.Length > 0 || match.IncludesLegacyFingerprints;
        AutomationProperties.SetName(view, "View differences. See what changed.");
        AutomationProperties.SetName(update, "Update device data. Replace saved copy with device data.");
        AutomationProperties.SetName(cancel, "Cancel. Keep current saved copy.");

        var details = new StackPanel { Name = "LibraryUpdateDifferences", Spacing = 12, Margin = new(20, 0, 20, 16) };
        if (cache.Committed is { } baseline) { details.Children.Add(DetailText($"Saved device data: {baseline.StartedAt.LocalDateTime:g}")); }
        if (match.IncludesLegacyFingerprints)
        { details.Children.Add(DetailText("Older saved data includes bypass states. Updating enables checks that ignore bypass.")); }
        details.Children.Add(BuildLibraryDifferenceRows(match.Differences, match.MetadataDifferences, "LibraryUpdate", concise: true));
        var scroll = new ScrollViewer
        {
            Name = "LibraryUpdateDifferencesScroll",
            Content = details,
            IsVisible = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetName(scroll, "Presets and settings that do not match");
        var back = IndexButton("Back", "LibraryUpdateBack");
        back.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(back, "Back to device update decision");
        var footer = new Border
        {
            Name = "LibraryUpdateReviewFooter",
            IsVisible = false,
            Child = back,
            Padding = new(20, 12),
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new(0, 1, 0, 0),
        };
        var layout = new Grid { RowDefinitions = new("Auto,*,Auto"), MaxHeight = Math.Max(300, Math.Min(620, Bounds.Height - 80)) };
        layout.Children.Add(header);
        Grid.SetRow(decision, 1); layout.Children.Add(decision);
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        dialog.Content = layout;

        void ShowReview(bool review)
        {
            decision.IsVisible = !review; scroll.IsVisible = review; footer.IsVisible = review;
            dialog.Title = review ? "Device differences" : "Confirm device data update";
            (review ? back : view).Focus();
        }
        view.Click += (_, _) => ShowReview(true);
        back.Click += (_, _) => ShowReview(false);
        update.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Opened += (_, _) => (view.IsEnabled ? view : cancel).Focus();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) { return; }
            if (scroll.IsVisible) { ShowReview(false); } else { dialog.Close(false); }
            e.Handled = true;
        };
        bool accepted = await dialog.ShowDialog<bool>(owner);
        // Sync enables its actions in finally, after this decision completes.
        Dispatcher.UIThread.Post(() =>
        {
            if (!owner.IsVisible) { return; }
            if (source?.Focus() != true) { owner.Focus(); }
        });
        return accepted;
    }

    private static Grid ActionRow(Button button, string description)
    {
        button.MinWidth = 142; button.MinHeight = 36; button.Margin = default;
        var row = new Grid { ColumnDefinitions = new("Auto,16,*") };
        row.Children.Add(button);
        var text = DetailText(description); text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 2); row.Children.Add(text);
        return row;
    }
}
#endif
