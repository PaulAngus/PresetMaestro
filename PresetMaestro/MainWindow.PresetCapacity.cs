#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PresetMaestro.FractalIndex;

namespace PresetMaestro;

public partial class MainWindow
{
    private async Task EstablishPresetCapacityAsync(DeviceIndex cache, CancellationToken token)
    {
        if (!cache.Device.NeedsCapacityDetection) { return; }
        _indexProgressMessage = "Checking preset capacity…";
        _indexProgressText!.Text = _indexProgressMessage;
        _indexProgress!.IsIndeterminate = true;
        RefreshConnectionSyncOptions();
        try
        {
            int? slots = await _fractalIndexReader.DetectPresetCapacityAsync(cache.Device, token);
            token.ThrowIfCancellationRequested();
            if (slots is null) { slots = await ConfirmPresetCapacityAsync(token); }
            token.ThrowIfCancellationRequested();
            if (slots is not (512 or 1024)) { throw new OperationCanceledException(token); }
            cache.Device = cache.Device with { PresetCapacity = slots };
            _indexProgress.Maximum = slots.Value;
        }
        finally { _indexProgress.IsIndeterminate = false; }
    }

    private async Task<int?> ConfirmPresetCapacityAsync(CancellationToken token)
    {
        var owner = _connectionSyncDialog is { IsVisible: true } sync ? sync : this;
        var origin = owner.FocusManager?.GetFocusedElement();
        var choices = new ComboBox
        {
            Name = "PresetCapacityChoice",
            ItemsSource = new[] { "512 slots", "1024 slots" },
            PlaceholderText = "Select preset capacity",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(choices, "Preset capacity");
        var continueButton = ProfileCommand("PresetCapacityContinue", "Continue sync");
        continueButton.IsEnabled = false;
        var cancel = ProfileCommand("PresetCapacityCancel", "Cancel");
        var dialog = new Window
        {
            Name = "PresetCapacityDialog",
            Title = "Confirm preset capacity",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = SurfaceBrush,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Preset capacity could not be detected", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "No valid reply arrived from the upper preset range. Select your device’s capacity to continue, or cancel and retry the connection. A missing reply does not prove a smaller capacity. Your choice is saved with this device.", Foreground = TextBrush, TextWrapping = TextWrapping.Wrap },
                    choices,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { continueButton, cancel } },
                },
            },
        };
        choices.SelectionChanged += (_, _) => continueButton.IsEnabled = choices.SelectedIndex >= 0;
        continueButton.Click += (_, _) => dialog.Close((int?)(choices.SelectedIndex == 0 ? 512 : 1024));
        cancel.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => choices.Focus();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
        };
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
        try { return await dialog.ShowDialog<int?>(owner); }
        finally { origin?.Focus(); }
    }
}
#endif
