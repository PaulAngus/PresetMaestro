using Avalonia.Controls;

namespace PresetMaestro;

public partial class MainWindow
{
    private MidiMappingWindow? _midiMappingWindow;

    private Button BuildMidiMappingButton()
    {
        var button = new Button { Name = "OpenMidiMapping", Content = "Configure…", MinWidth = 100 };
        button.Click += async (_, _) =>
        {
            if (_midiMappingWindow is not null) { _midiMappingWindow.Activate(); return; }
            var dialog = new MidiMappingWindow(_settings.ActiveProfile, _settings.SceneCc, _settings.MidiNoteMap, SaveMidiMapping);
            _midiMappingWindow = dialog;
            try { await dialog.ShowDialog(this); }
            finally { _midiMappingWindow = null; }
        };
        return button;
    }

    private void SaveMidiMapping(int sceneCc, Dictionary<int, string> notes)
    {
        int oldSceneCc = _settings.SceneCc;
        var oldNotes = _settings.MidiNoteMap;
        _settings.SceneCc = sceneCc;
        _settings.MidiNoteMap = notes;
        try { _saveSettings(_settings); }
        catch
        {
            _settings.SceneCc = oldSceneCc;
            _settings.MidiNoteMap = oldNotes;
            throw;
        }
    }
}
