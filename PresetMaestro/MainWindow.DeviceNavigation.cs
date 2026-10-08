using PresetMaestro.Core;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private string? DeviceNavigationUnavailable(DeviceModel model)
    {
        if (_detectedDevice is null || !_midi.InputOpen || !_midi.OutputOpen) { return "Connect a Fractal device to use Go to."; }
        if (_detectedDevice.Model != model) { return "This selection belongs to a different Fractal device model."; }
        if (_connectionCts is not null || _presetNamesCts is not null || _changingProfile) { return "Wait for the current read or profile change to finish."; }
#if FRACTAL_INDEX
        if (_connectionLibrarySetupRequired || _connectionLibraryChoiceBusy) { return "Finish the required device sync first."; }
#endif
        return null;
    }

    private bool GoToDevice(int slot, int? scene, DeviceModel model)
    {
        if (DeviceNavigationUnavailable(model) is { } unavailable) { AppendLog("GO TO: " + unavailable); return false; }
        int displayed = slot + _settings.DisplayOffset;
        if (!PresetTranslation.IsValid(displayed, _settings.DisplayOffset, EffectiveMaximum) || scene is < 1 or > 8) { return false; }
        int channel = _settings.MidiChannel == 0 ? 1 : _settings.MidiChannel;
        var translated = PresetTranslation.Translate(displayed, _settings.DisplayOffset, channel);
        bool samePreset = _sceneSlot == slot;
        bool sent = scene is int selectedScene
            ? samePreset ? _midi.SendScene(selectedScene, _settings.SceneCc, channel)
                : _midi.SendFavorite(translated.Bank, translated.ProgramChange, selectedScene, _settings.SceneCc, channel)
            : _midi.SendBankAndPC(translated.Bank, translated.ProgramChange, channel);
        string message = sent ? $"Sent preset {displayed:000}" + (scene is int s ? $", scene {s}." : ".") : "The device could not be reached. Check the MIDI connection.";
        if (sent)
        {
            _lastPreset = _currentPreset; _currentPreset = displayed;
            _currentFavoriteName = null; _currentFavoriteScene = scene;
            if (scene is null || !samePreset) { PresetSent(slot, scene); }
            else { _activeScene = scene; }
            _enteredDigits = string.Empty; UpdateDisplay();
        }
        ShowSendFeedback(sent ? "SENT" : "Could not go to selection", message, warning: !sent);
        AppendLog("GO TO: " + message);
        return sent;
    }

}
