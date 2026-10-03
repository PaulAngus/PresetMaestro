using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private Button _syncPresetNamesButton = null!;
    private Button _cancelPresetNamesButton = null!;
    private Button? _syncOptionsButton;
    private ProgressBar _presetNamesProgress = null!;
    private TextBlock _presetNamesStatus = null!;
    private CancellationTokenSource? _presetNamesCts;
    private bool _syncingPresetNames;
    private bool _presetSyncWaitShown;

    private bool CanSyncPresetNames => !_changingProfile && CanReadDeviceNames && _connectionCts is null && _presetNamesCts is null && _midi.InputOpen && _midi.OutputOpen;

    private Control BuildPresetNameSyncCard()
    {
        var card = ApprovedCard("Preset Name Sync", "Read-only device query; active preset is never changed", compact: true);
        var stack = new StackPanel { Spacing = 8 };

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        _syncPresetNamesButton = new Button { Name = "ConfigPresetSync", Content = "Sync Preset Names", Background = AccentBrush, Foreground = Brushes.White };
        _syncPresetNamesButton.Click += async (_, _) => await SyncPresetNamesAsync();
        _cancelPresetNamesButton = new Button { Name = "ConfigPresetSyncCancel", Content = "Cancel", IsEnabled = false, Foreground = DangerBrush };
        _cancelPresetNamesButton.Click += (_, _) => _presetNamesCts?.Cancel();
        buttons.Children.Add(_syncPresetNamesButton);
        _syncPresetNamesButton.Margin = new(0, 0, 8, 0);
        _cancelPresetNamesButton.Margin = new(0, 0, 8, 0);
        buttons.Children.Add(_cancelPresetNamesButton);
        _syncOptionsButton = new Button { Content = "Sync options…", Name = "ConfigSyncOptions" };
        _syncOptionsButton.Click += (_, _) => OpenConnectionSyncOptions();
        buttons.Children.Add(_syncOptionsButton);
        stack.Children.Add(buttons);

        _presetNamesProgress = new ProgressBar { Minimum = 0, Maximum = 512, Height = 4 };
        stack.Children.Add(_presetNamesProgress);
        _presetNamesStatus = new TextBlock
        {
            Name = "PresetSyncStatus",
            Text = CanReadDeviceNames
                ? $"{_settings.PresetNameCache.Count} cached preset names in this profile."
                : UnsupportedNameReads,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(_presetNamesStatus);

        SetApprovedCardContent(card, stack);
        UpdatePresetSyncButtons();
        return card;
    }

    internal async Task SyncPresetNamesAsync()
    {
        if (!CanReadDeviceNames)
        {
            _presetNamesStatus.Text = UnsupportedNameReads;
            return;
        }
        if (_changingProfile || _connectionCts is not null || _presetNamesCts != null || !_midi.InputOpen || !_midi.OutputOpen)
        {
            if (!_midi.InputOpen || !_midi.OutputOpen)
            {
                AppendLog("PRESET NAMES: connect both MIDI input and output first");
            }

            return;
        }

        _presetNamesCts = new CancellationTokenSource();
        _syncingPresetNames = true;
        _connectionSyncOutcome = null;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        _cancelPresetNamesButton.IsEnabled = true;
        _presetNamesProgress.Value = 0;
        _presetNamesProgress.Maximum = 512;
        _presetNamesStatus.Text = "Synchronizing slots 0-511...";
        UpdatePresetSyncButtons();
        var names = new Dictionary<int, string>(_settings.PresetNameCache);
        var refreshedSlots = new HashSet<int>();
        int failures = 0;
        int checkedSlots = 0;
        int responseCount = 0;
        CancellationToken token = _presetNamesCts.Token;
        // Device replies can arrive much faster than the UI can render. Keep the
        // request loop identical for Config and the dialog; repaint only on a timer.
        var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        progressTimer.Tick += (_, _) =>
        {
            int displayedSlots = Volatile.Read(ref checkedSlots);
            _presetNamesProgress.Value = displayedSlots;
            _presetNamesStatus.Text = $"Synchronized {displayedSlots} of 512 ({Volatile.Read(ref responseCount)} responses)";
            if (_connectionSyncDialog is not null)
            {
                _connectionSyncProgress!.Value = displayedSlots;
                _connectionSyncStatus!.Text = $"{displayedSlots} of 512 preset slots checked.";
            }
        };
        progressTimer.Start();

        try
        {
            // Neither the next MIDI request nor response processing may wait for
            // the UI dispatcher. Only the timer and completion update controls.
            await Task.Run(async () =>
            {
                for (int slot = 0; slot <= 511; slot++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        PresetNameResult result = await _queryPresetNameAsync(slot, TimeSpan.FromSeconds(1.5), token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        names[result.Slot] = result.PresetName;
                        refreshedSlots.Add(result.Slot);
                        Volatile.Write(ref responseCount, refreshedSlots.Count);
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

                    Volatile.Write(ref checkedSlots, slot + 1);
                }
            }, token);

            _settings.PresetNameCache = names;
            _saveSettings(_settings);
            UpdateFavoritePresetDisplay();
            string? warning = CurrentFavoritePresetWarning(refreshedSlots, names);
            bool incomplete = refreshedSlots.Count < 512 || warning is not null;
            _presetNamesStatus.Text = warning ?? (incomplete
                ? $"Incomplete: {refreshedSlots.Count} of 512 preset names synchronized. {512 - refreshedSlots.Count} names were not returned; existing cached names were kept."
                : "Complete: 512 of 512 preset names synchronized.");
            SetConnectionSyncOutcome(incomplete ? "Preset-name sync needs attention" : "Preset names synced",
                $"{refreshedSlots.Count} of 512 preset names refreshed in profile “{_settings.ActiveProfile}”. Only the name list was refreshed." + (warning is null ? "" : " " + warning),
                incomplete ? "Next: Choose Sync names to retry missing names, or Done to use the saved list."
                    : "Next: Choose Done to continue. Other sync options are optional.", needsAttention: incomplete);
            AppendLog(warning is null
                ? $"PRESET NAMES: synchronized {refreshedSlots.Count} of 512 preset names"
                : $"PRESET NAMES: {warning}");
        }
        catch (OperationCanceledException)
        {
            _presetNamesStatus.Text = $"Cancelled at {checkedSlots} of 512. No preset was selected.";
            SetConnectionSyncOutcome("Preset-name sync cancelled", $"{refreshedSlots.Count} of 512 names refreshed. Completed reads were saved; other cached names were kept.",
                "Next: Choose Sync names to retry, or Done to use the saved list.", needsAttention: true);
        }
        catch (Exception ex)
        {
            _presetNamesStatus.Text = ex.Message;
            AppendLog($"PRESET NAMES: sync failed — {ex.Message}");
            SetConnectionSyncOutcome("Preset-name sync stopped", $"{refreshedSlots.Count} of 512 names refreshed. Completed reads were saved. {ex.Message}",
                "Next: Check the MIDI connection, then choose Sync names to retry.", needsAttention: true);
        }
        finally
        {
            progressTimer.Stop();
            _presetNamesProgress.Value = checkedSlots;
            _settings.PresetNameCache = names;
            _saveSettings(_settings);
            UpdateFavoritePresetDisplay();
            _presetNamesCts.Dispose();
            _presetNamesCts = null;
            _syncingPresetNames = false;
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
        UpdateIndexButtons();
        bool connected = _midi.InputOpen && _midi.OutputOpen && CanReadDeviceNames;
        bool syncing = _presetNamesCts is not null;
        bool checkingLibrary = false;
#if FRACTAL_INDEX
        checkingLibrary = _indexChecking;
#endif
        bool connecting = _connectionCts is not null;
        if (_syncOptionsButton is not null) { _syncOptionsButton.IsEnabled = _detectedDevice is not null && _midi.InputOpen && _midi.OutputOpen && !connecting && !_changingProfile; }
        if (_syncPresetNamesButton is not null)
        {
            _syncPresetNamesButton.IsEnabled = CanSyncPresetNames;
            _syncPresetNamesButton.Content = connecting ? "Connecting…" : checkingLibrary ? "Checking library…" : "Sync Preset Names";
            if (_presetNamesProgress is not null)
            {
                _presetNamesProgress.IsIndeterminate = connecting || checkingLibrary;
                _presetNamesProgress.Height = connecting || checkingLibrary ? 8 : 4;
            }
            if (_presetNamesStatus is not null)
            {
                if (connecting || checkingLibrary)
                {
                    _presetSyncWaitShown = true;
                    _presetNamesStatus.Text = connecting
                        ? "Connecting and reading device information… Preset-name sync will be available when this finishes."
                        : "Connected. Checking this device against the assigned library… Preset-name sync will be available when this finishes.";
                }
                else if (_presetSyncWaitShown)
                {
                    _presetSyncWaitShown = false;
                    _presetNamesStatus.Text = !CanReadDeviceNames ? UnsupportedNameReads : connected
                        ? $"Ready to sync preset names. {_settings.PresetNameCache.Count} cached names in this profile; sync when you want to refresh them."
                        : $"Connect the device to sync preset names. {_settings.PresetNameCache.Count} cached names in this profile.";
                }
            }
            if (checkingLibrary || _syncingFavoriteScenes) { _cancelPresetNamesButton.IsEnabled = true; }
            else if (!syncing) { _cancelPresetNamesButton.IsEnabled = false; }
        }

        if (_favSyncPresetsButton is not null)
        {
            _favSyncPresetsButton.IsEnabled = CanSyncPresetNames;
            _favSyncPresetsButton.Content = checkingLibrary ? "Checking…" : syncing ? "Syncing…" : "↻ Sync";
            ToolTip.SetTip(_favSyncPresetsButton, !CanReadDeviceNames ? UnsupportedNameReads : connected
                ? checkingLibrary ? "Checking the connected device against its library. Preset-name sync will be available when this finishes."
                    : syncing ? "Preset synchronization is already running." : "Sync preset names from the connected device."
                : "Connect to the device to sync presets.");
        }
        RefreshConnectionSyncOptions();
    }

}
