#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private StackPanel? _connectionSyncDifferences;

    private Control BuildLibraryMatchDetails()
    {
        _connectionSyncDifferences = new StackPanel { Name = "ConnectionSyncDifferences", Spacing = 10, IsVisible = false };
        AutomationProperties.SetName(_connectionSyncDifferences, "Library check differences");
        return _connectionSyncDifferences;
    }

    private void RefreshLibraryMatchDetails(bool running)
    {
        if (_connectionSyncDifferences is null) { return; }
        bool current = !running && _connectionSyncOutcome?.Context == ConnectionSyncContext &&
            ReferenceEquals(_libraryCheckOutcome, _connectionSyncOutcome) &&
            _matchLibraryId == _indexCache?.Device.Id && _matchBaselineId == _indexCache?.Committed?.Id &&
            _matchConnection == _connectionGeneration;
        var presets = _libraryMatch?.Differences ?? _libraryNameDifferences;
        var metadata = _libraryMatch?.MetadataDifferences ?? [];
        _connectionSyncDifferences.IsVisible = current && (presets.Length > 0 || metadata.Length > 0);
        _connectionSyncDifferences.Children.Clear();
        if (!_connectionSyncDifferences.IsVisible) { return; }
        _connectionSyncDifferences.Children.Add(new TextBlock { Text = "What differs", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush });
        string date = _libraryCheckBaselineAt is { } baseline ? $"Saved library: {baseline.LocalDateTime:g}. " : "";
        _connectionSyncDifferences.Children.Add(DetailText(date + (_libraryMatch?.IsSample == true
            ? "This check sampled different random slots; a previous sample may have missed these presets."
            : _libraryMatch is null ? "Only preset names were compared; preset content was not read." : "Every preset slot was checked.")));
        if (_libraryMatch?.IncludesLegacyFingerprints == true)
        {
            _connectionSyncDifferences.Children.Add(DetailText("This older library includes bypass in its fingerprint. Review these differences, then Sync library once to enable comparisons that ignore bypass states."));
        }
        var rows = BuildLibraryDifferenceRows(presets, metadata, "ConnectionSync", concise: false);
        var scroll = new ScrollViewer { Name = "ConnectionSyncDifferencesScroll", Content = rows, MaxHeight = 240, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(scroll, "Presets and settings that differ from the saved library");
        _connectionSyncDifferences.Children.Add(scroll);
    }

    private StackPanel BuildLibraryDifferenceRows(LibraryPresetDifference[] presets, LibraryValueDifference[] metadata, string prefix, bool concise)
    {
        var rows = new StackPanel { Spacing = 12 };
        foreach (var difference in metadata) { rows.Children.Add(DifferenceValues(difference)); }
        foreach (var preset in presets)
        {
            var details = new StackPanel { Spacing = 6 };
            details.Children.Add(new TextBlock
            {
                Name = prefix + "DifferencePreset",
                Text = $"{preset.Slot + _settings.DisplayOffset:D3}  {preset.ConnectedName ?? preset.SavedName ?? "Preset unavailable"}",
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var change in preset.Changes) { details.Children.Add(DifferenceValues(change)); }
            if (!concise && preset.ContentChanged && preset.Changes.Length == 0)
            { details.Children.Add(DetailText("Names, scene names, amp models and saved scene channel selections match.")); }
            if (concise && preset.ContentChanged)
            { details.Children.Add(DetailText("Saved preset data differs; only indexed settings can be shown.")); }
            if (concise && preset.ContentChanged && preset.Changes.Length == 0)
            { details.Children.Add(DetailText("Exact change unavailable. Indexed names, amps and scene channels match.")); }
            if (preset.ReadError is not null || !concise && _libraryMatch is not null)
            { details.Children.Add(DetailText(preset.Explanation)); }
            rows.Children.Add(new Border { Child = details, Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new(1), CornerRadius = new(6), Padding = new(12) });
        }
        return rows;
    }

    private static TextBlock DetailText(string text) => new() { Text = text, FontSize = 14, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };

    private static Control DifferenceValues(LibraryValueDifference difference)
    {
        var values = new StackPanel { Spacing = 3 };
        values.Children.Add(new TextBlock { Text = difference.Setting, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap });
        values.Children.Add(DetailText("Saved: " + (difference.Saved.Length == 0 ? "(blank)" : difference.Saved)));
        values.Children.Add(DetailText("Connected: " + (difference.Connected.Length == 0 ? "(blank)" : difference.Connected)));
        return values;
    }
}
#endif
