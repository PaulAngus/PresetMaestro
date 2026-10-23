using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private CancellationTokenSource? _presetConfirmationCts;
    private int? _presetConfirmationScene;
    internal Task PresetConfirmationTask { get; private set; } = Task.CompletedTask;
    private bool _sendFeedbackPending;

    private void CancelPresetConfirmation()
    {
        _presetConfirmationCts?.Cancel();
        _presetConfirmationCts = null;
        _presetConfirmationScene = null;
        if (_sendFeedbackPending) { HideSendFeedback(); _sendFeedbackPending = false; }
    }

    private void ShowPresetConfirmationError(string title, string detail)
    {
        ShowSendFeedback(title, detail, warning: true);
        _sendFeedbackPanel.Background = ThemeBrush("SendErrorBackgroundBrush");
        _sendFeedbackPanel.BorderBrush = _sendFeedbackIcon.Foreground = _sendFeedbackTitle.Foreground =
            _sendFeedbackDetail.Foreground = ThemeBrush("SendErrorForegroundBrush");
    }

    private void DisconnectAfterTransportFailure()
    {
        bool pending = _presetConfirmationCts is not null;
        bool scenePending = _presetConfirmationScene.HasValue;
        Disconnect();
        if (pending && !_sceneClosing)
        {
            _currentPreset = null;
            _currentFavoriteName = null; _currentFavoriteScene = null;
            UpdateDisplay();
            ShowPresetConfirmationError(scenePending ? "No scene confirmation" : "No preset confirmation", "The MIDI connection was lost. Reconnect the device and try again.");
        }
    }

    private async Task ConfirmPresetSelectionAsync(int slot, int? expectedScene, string sentDetail, bool refreshScenes, CancellationTokenSource cts)
    {
        int profile = _profileGeneration;
        long connection = _connectionGeneration;
        int offset = _settings.DisplayOffset;
        PresetNameResult? observed = null;
        int? observedScene = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(4));
        bool Current() => ReferenceEquals(_presetConfirmationCts, cts) && !cts.IsCancellationRequested &&
            !_sceneClosing && profile == _profileGeneration && connection == _connectionGeneration;
        try
        {
            // Coalesce rapid sends and let the device finish loading. Recheck a
            // different slot until the deadline rather than rejecting an old reply.
            await Task.Delay(200, deadline.Token);
            while (true)
            {
                try
                {
                    if (expectedScene.HasValue)
                    {
                        var state = await _presetNameClient.ReadSceneConfirmationAsync(slot, deadline.Token);
                        observed = state.Preset; observedScene = state.Scene?.Index + 1;
                    }
                    else { observed = await _presetNameClient.ReadPresetConfirmationAsync(deadline.Token); }
                }
                catch (TimeoutException) { }
                if (!Current()) { return; }
                if (observed?.Slot == slot && (!expectedScene.HasValue || observedScene == expectedScene))
                {
                    _currentPreset = slot + offset;
                    _sceneSlot = slot;
                    _detectedPresetSlot = slot;
                    _activeScene = _detectedScene = observedScene;
                    UpdateDisplay(); UpdateFavoriteRowStates(); UpdateIndexActiveRows();
                    ShowSendFeedback(expectedScene.HasValue ? "Preset and scene confirmed" : "Preset confirmed", sentDetail);
                    AppendLog($"PRESET CONFIRMED: requested {slot + offset}; device reported {observed.Slot + offset}." +
                        (expectedScene.HasValue ? $" SCENE CONFIRMED: requested {expectedScene}; device reported {observedScene}." : ""));
                    if (refreshScenes && CanReadDeviceNames) { _ = RefreshScenesAsync(slot); }
                    return;
                }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!Current()) { return; }
            _currentPreset = observed is null ? null : observed.Slot + offset;
            _sceneSlot = observed?.Slot;
            _activeScene = observedScene;
            _detectedPresetSlot = observed?.Slot;
            _detectedScene = observedScene;
            _currentFavoriteName = null; _currentFavoriteScene = null;
            UpdateDisplay(); UpdateFavoriteRowStates(); UpdateIndexActiveRows();
            bool wrongPreset = observed is not null && observed.Slot != slot;
            string detail = expectedScene.HasValue && !wrongPreset
                ? observedScene.HasValue
                    ? $"Requested scene {expectedScene} on preset {slot + offset}; device reports scene {observedScene}. Try again."
                    : $"Scene {expectedScene} on preset {slot + offset} was not confirmed. Check the MIDI connection and try again."
                : observed is not null
                ? $"Requested preset {slot + offset}; device reports {observed.Slot + offset}. Try again."
                : $"Preset {slot + offset} was not confirmed. Check the MIDI connection and try again.";
            string title = expectedScene.HasValue && !wrongPreset
                ? observedScene.HasValue ? "Scene did not change" : "No scene confirmation"
                : observed is null ? "No preset confirmation" : "Preset did not change";
            ShowPresetConfirmationError(title, detail);
            AppendLog($"SELECTION CONFIRMATION FAILED: {detail} ({ex.Message})");
        }
        finally
        {
            if (ReferenceEquals(_presetConfirmationCts, cts)) { _presetConfirmationCts = null; _presetConfirmationScene = null; }
            cts.Dispose();
        }
    }
}
