using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private Button _syncPresetNamesButton = null!;
    private Button _cancelPresetNamesButton = null!;
    private ProgressBar _presetNamesProgress = null!;
    private TextBlock _presetNamesStatus = null!;
    private CancellationTokenSource? _presetNamesCts;

    private bool CanSyncPresetNames => _presetNamesCts is null && _midi.InputOpen && _midi.OutputOpen;

    private Control BuildPresetNameSyncCard()
    {
        var card = ApprovedCard("Preset Name Sync", "Read-only device query; active preset is never changed");
        var stack = new StackPanel { Spacing = 10 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _syncPresetNamesButton = new Button { Name = "ConfigPresetSync", Content = "Sync Preset Names", Background = AccentBrush, Foreground = Brushes.White };
        _syncPresetNamesButton.Click += async (_, _) => await SyncPresetNamesAsync();
        _cancelPresetNamesButton = new Button { Content = "Cancel", IsEnabled = false, Foreground = DangerBrush };
        _cancelPresetNamesButton.Click += (_, _) => _presetNamesCts?.Cancel();
        buttons.Children.Add(_syncPresetNamesButton);
        buttons.Children.Add(_cancelPresetNamesButton);
        stack.Children.Add(buttons);

        _presetNamesProgress = new ProgressBar { Minimum = 0, Maximum = 512, Height = 8 };
        stack.Children.Add(_presetNamesProgress);
        _presetNamesStatus = new TextBlock { Name = "PresetSyncStatus", Text = "No synchronized names yet.", Foreground = SecondaryBrush };
        stack.Children.Add(_presetNamesStatus);

        SetApprovedCardContent(card, stack);
        UpdatePresetSyncButtons();
        return card;
    }

    internal async Task SyncPresetNamesAsync()
    {
        if (_presetNamesCts != null || !_midi.InputOpen || !_midi.OutputOpen)
        {
            if (!_midi.InputOpen || !_midi.OutputOpen)
            {
                AppendLog("PRESET NAMES: connect both MIDI input and output first");
            }

            return;
        }

        _presetNamesCts = new CancellationTokenSource();
        _sceneSyncCts?.Cancel(); _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        _cancelPresetNamesButton.IsEnabled = true;
        _presetNamesProgress.Value = 0;
        _presetNamesStatus.Text = "Synchronizing slots 0-511...";
        UpdatePresetSyncButtons();
        var names = new Dictionary<int, string>(_settings.PresetNameCache);
        var refreshedSlots = new HashSet<int>();
        int failures = 0;

        try
        {
            for (int slot = 0; slot <= 511; slot++)
            {
                _presetNamesCts.Token.ThrowIfCancellationRequested();
                try
                {
                    PresetNameResult result = await _queryPresetNameAsync(slot, TimeSpan.FromSeconds(1.5), _presetNamesCts.Token);
                    names[result.Slot] = result.PresetName;
                    refreshedSlots.Add(result.Slot);
                    failures = 0;
                }
                catch (TimeoutException)
                {
                    AppendLog($"PRESET NAMES: slot {slot} timed out");
                    if (++failures >= 3)
                    {
                        throw new IOException("Preset sync stopped after three unanswered requests.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppendLog($"PRESET NAMES: slot {slot} failed — {ex.Message}");
                    throw;
                }

                _presetNamesProgress.Value = slot + 1;
                if ((slot + 1) % 16 == 0 || slot == 511)
                {
                    _presetNamesStatus.Text = $"Synchronized {slot + 1} of 512 ({names.Count} responses)";
                }
            }

            _settings.PresetNameCache = names;
            _saveSettings(_settings);
            UpdateFavoritePresetDisplay();
            string? warning = CurrentFavoritePresetWarning(refreshedSlots, names);
            _presetNamesStatus.Text = warning ?? $"Complete: {refreshedSlots.Count} of 512 preset names synchronized.";
            AppendLog(warning is null
                ? $"PRESET NAMES: synchronized {refreshedSlots.Count} of 512 preset names"
                : $"PRESET NAMES: {warning}");
        }
        catch (OperationCanceledException)
        {
            _presetNamesStatus.Text = $"Cancelled at {_presetNamesProgress.Value:0} of 512. No preset was selected.";
        }
        catch (Exception ex)
        {
            _presetNamesStatus.Text = ex.Message;
            AppendLog($"PRESET NAMES: sync failed — {ex.Message}");
        }
        finally
        {
            _settings.PresetNameCache = names;
            _saveSettings(_settings);
            UpdateFavoritePresetDisplay();
            _presetNamesCts.Dispose();
            _presetNamesCts = null;
            _cancelPresetNamesButton.IsEnabled = false;
            UpdatePresetSyncButtons();
        }
    }

    private string? CurrentFavoritePresetWarning(ISet<int> refreshedSlots, IReadOnlyDictionary<int, string> names)
    {
        if (_favEditingId is null || _favPresetSpinner.Value is not decimal displayed)
        {
            return null;
        }

        int slot = (int)displayed - _settings.DisplayOffset;
        if (refreshedSlots.Contains(slot) && names.TryGetValue(slot, out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return $"Preset {(int)displayed:000} was not returned by the device. The editor selection was preserved.";
    }

    private void UpdatePresetSyncButtons()
    {
        bool connected = _midi.InputOpen && _midi.OutputOpen;
        bool syncing = _presetNamesCts is not null;
        if (_syncPresetNamesButton is not null)
        {
            _syncPresetNamesButton.IsEnabled = connected && !syncing;
        }

        if (_favSyncPresetsButton is not null)
        {
            _favSyncPresetsButton.IsEnabled = connected && !syncing;
            _favSyncPresetsButton.Content = syncing ? "Syncing…" : "↻ Sync";
            ToolTip.SetTip(_favSyncPresetsButton, connected
                ? syncing ? "Preset synchronization is already running." : "Sync preset names from the connected device."
                : "Connect to the device to sync presets.");
        }
    }

}
