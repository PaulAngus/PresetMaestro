#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.Core;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private TextBlock? _indexPresetNumber;
    private Button? _indexPresetReadout, _indexSendCommand;

    private Control BuildIndexSendControls()
    {
        var controls = new StackPanel { Name = "IndexSendControls", Orientation = Orientation.Horizontal, Spacing = 6 };
        controls.Children.Add(new TextBlock { Text = "Preset", Foreground = SecondaryBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        _indexPresetNumber = new TextBlock
        {
            Name = "IndexPresetNumber",
            Text = "---",
            FontFamily = AppFonts.Display,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _indexPresetReadout = new Button
        {
            Name = "IndexPresetReadout",
            Content = _indexPresetNumber,
            Width = 72,
            Height = 28,
            MinHeight = 28,
            Padding = new Thickness(4, 0),
            CornerRadius = new CornerRadius(5),
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _indexPresetReadout.Click += (_, _) => _indexPresetReadout.Focus();
        controls.Children.Add(_indexPresetReadout);
        var clear = new Button
        {
            Name = "IndexClearCommand",
            Content = "CLR",
            Width = 42,
            Height = 28,
            MinHeight = 28,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 0),
            FontSize = 12,
            Foreground = DangerBrush,
            FontWeight = FontWeight.SemiBold,
        };
        AutomationProperties.SetName(clear, "Clear entered preset number");
        ToolTip.SetTip(clear, "Clear entered preset number (Delete).");
        clear.Click += (_, _) => HandleClear();
        controls.Children.Add(clear);
        _indexSendCommand = new Button
        {
            Name = "IndexSendCommand",
            Content = "SEND",
            Width = 52,
            Height = 28,
            MinHeight = 28,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 0),
            FontSize = 12,
            Background = AccentBrush,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
        };
        _indexSendCommand.Click += (_, _) => HandleSend();
        controls.Children.Add(_indexSendCommand);
        UpdateIndexSendControls();
        return controls;
    }

    private void UpdateIndexSendControls()
    {
        if (_indexPresetNumber is null || _indexSendCommand is null || _indexPresetReadout is null) { return; }
        bool entered = _enteredDigits.Length > 0;
        int? displayed = entered ? int.TryParse(_enteredDigits, out int value) ? value : null
            : _indexSelectedSlot is int slot ? slot + _settings.DisplayOffset : null;
        bool valid = displayed is int preset && PresetTranslation.IsValid(preset, _settings.DisplayOffset, EffectiveMaximum);
        _indexPresetNumber.Text = entered ? _enteredDigits : displayed?.ToString("000") ?? "---";
        _indexPresetNumber.Foreground = entered ? valid ? AccentBrush : DangerBrush : displayed.HasValue ? TextBrush : SecondaryBrush;
        string range = $"{_settings.DisplayOffset}–{EffectiveMaximum}";
        string entryHint = _settings.KeyboardEntryEnabled
            ? $"Type a preset number ({range}), then press Enter or SEND. Backspace edits; CLR or Delete clears."
            : "Enable keyboard entry in Config to type a preset number, or select a preset or scene in the index.";
        ToolTip.SetTip(_indexPresetReadout, entryHint);
        AutomationProperties.SetName(_indexPresetReadout, $"Preset number: {_indexPresetNumber.Text}. {entryHint}");
        int? scene = !entered && _indexSelectedScene is int selected ? selected + 1 : null;
        string action = displayed.HasValue ? $"Send preset {displayed.Value:000}" + (scene.HasValue ? $", scene {scene}" : "") : "Send preset";
        string? unavailable = DeviceNavigationUnavailable(_indexCache?.Device.Variant.ToDeviceModel() ?? PickerDeviceModel);
        _indexSendCommand.IsEnabled = valid && unavailable is null;
        AutomationProperties.SetName(_indexSendCommand, action);
        ToolTip.SetTip(_indexSendCommand, unavailable ?? (!displayed.HasValue ? "Enter a preset number or select a preset or scene."
            : !valid ? $"Enter a preset number from {range}." : action + " (Enter)."));
    }

    private void SendIndexEntry()
    {
        int? scene = null;
        int displayed;
        if (_enteredDigits.Length > 0)
        {
            if (!int.TryParse(_enteredDigits, out displayed)) { return; }
            if (!PresetTranslation.IsValid(displayed, _settings.DisplayOffset, EffectiveMaximum))
            {
                ShowSendFeedback("Preset number is out of range", $"Enter a preset number from {_settings.DisplayOffset} to {EffectiveMaximum}.", warning: true);
                return;
            }
        }
        else
        {
            if (_indexSelectedSlot is not int slot) { return; }
            displayed = slot + _settings.DisplayOffset;
            scene = _indexSelectedScene is int selected ? selected + 1 : null;
        }
        GoToDevice(displayed - _settings.DisplayOffset, scene, _indexCache?.Device.Variant.ToDeviceModel() ?? PickerDeviceModel);
    }
}
#endif
