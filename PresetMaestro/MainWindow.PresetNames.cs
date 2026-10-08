using Avalonia.Controls;
using Avalonia.Threading;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private Button? _syncOptionsButton;
    private int _syncReadCompleted, _syncReadTotal = 512;
    private CancellationTokenSource? _presetNamesCts;
    private bool _syncingPresetNames;

    private bool CanSyncPresetNames => !_changingProfile && CanReadDeviceNames && _connectionCts is null && _presetNamesCts is null && _midi.InputOpen && _midi.OutputOpen
#if FRACTAL_INDEX
        && !_connectionLibrarySetupRequired
#endif
        ;

    private Button BuildSyncOptionsButton()
    {
        _syncOptionsButton = new Button { Content = "Sync options…", Name = "ConfigSyncOptions", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        _syncOptionsButton.Click += (_, _) => OpenConnectionSyncOptions();
        UpdatePresetSyncButtons();
        return _syncOptionsButton;
    }

    internal async Task SyncPresetNamesAsync()
    {
#if FRACTAL_INDEX
        if (_connectionLibrarySetupRequired) { return; }
#endif
        if (!CanReadDeviceNames)
        {
            SetConnectionSyncOutcome("Preset-name sync unavailable", UnsupportedNameReads, "Use saved data or choose another sync supported by this device.", needsAttention: true);
            RefreshConnectionSyncOptions();
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
        using var diagnostics = BeginSyncDiagnostics("sync-names");
        _syncingPresetNames = true;
        _connectionSyncOutcome = null;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        _syncReadCompleted = 0;
        _syncReadTotal = 512;
        UpdatePresetSyncButtons();
        var read = new Core.PresetNameReadSession(_settings.PresetNameCache);
        CancellationToken token = _presetNamesCts.Token;
        var progressTimer = StartPresetNameReadProgress(read, diagnostics);

        try
        {
            await ReadPresetNamesAsync(read, token);
            string? warning = CurrentFavoritePresetWarning(read.RefreshedSlots, read.Names);
            bool incomplete = read.RefreshedSlots.Count < 512 || warning is not null;
            diagnostics.Result(incomplete ? "partial" : "complete", read.CheckedSlots, errors: 512 - read.RefreshedSlots.Count);
            SetConnectionSyncOutcome(incomplete ? "Preset-name sync needs attention" : "Preset names synced",
                $"{read.RefreshedSlots.Count} of 512 preset names refreshed in profile “{_settings.ActiveProfile}”. Only the name list was refreshed." + (warning is null ? "" : " " + warning),
                incomplete ? "Next: Choose Sync names to retry missing names, or Done to use the saved list."
                    : "Next: Choose Done to continue. Use Sync device if you also changed preset content or scenes.", needsAttention: incomplete);
            AppendLog(warning is null
                ? $"PRESET NAMES: synchronized {read.RefreshedSlots.Count} of 512 preset names"
                : $"PRESET NAMES: {warning}");
        }
        catch (OperationCanceledException)
        {
            diagnostics.Result("cancelled", read.CheckedSlots);
            SetConnectionSyncOutcome("Preset-name sync cancelled", $"{read.RefreshedSlots.Count} of 512 names refreshed. Completed reads were saved; other cached names were kept.",
                "Next: Choose Sync names to retry, or Done to use the saved list.", needsAttention: true);
        }
        catch (Exception ex)
        {
            diagnostics.Result("failed", read.CheckedSlots);
            AppendLog($"PRESET NAMES: sync failed — {ex.Message}");
            SetConnectionSyncOutcome("Preset-name sync stopped", $"{read.RefreshedSlots.Count} of 512 names refreshed. Completed reads were saved. {ex.Message}",
                "Next: Check the MIDI connection, then choose Sync names to retry.", needsAttention: true);
        }
        finally
        {
            progressTimer.Stop();
            _syncReadCompleted = read.CheckedSlots;
            try { SavePresetNameRead(read); }
            catch (Exception ex)
            {
                diagnostics.Result("save-failed", read.CheckedSlots, errors: 512 - read.RefreshedSlots.Count);
                AppendLog($"PRESET NAMES: could not save refreshed names — {ex.Message}");
                SetConnectionSyncOutcome("Preset names could not be saved", "The previous name cache was kept. " + ex.Message,
                    "Check that the profile folder is writable, then choose Sync names to retry.", needsAttention: true);
            }
            finally
            {
                _presetNamesCts.Dispose();
                _presetNamesCts = null;
                _syncingPresetNames = false;
                UpdatePresetSyncButtons();
            }
        }
    }

    private Task ReadPresetNamesAsync(Core.PresetNameReadSession read, CancellationToken token) => read.ReadAsync(_queryPresetNameAsync, AppendLog, token);

    private DispatcherTimer StartPresetNameReadProgress(Core.PresetNameReadSession read, Core.SyncDiagnosticLog.Session diagnostics)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        long previousTick = System.Diagnostics.Stopwatch.GetTimestamp();
        timer.Tick += (_, _) =>
        {
            long tick = System.Diagnostics.Stopwatch.GetTimestamp();
            diagnostics.Record(new("ui.timer-delay", null,
                Math.Max(0, System.Diagnostics.Stopwatch.GetElapsedTime(previousTick, tick).TotalMilliseconds - 100), "complete"));
            previousTick = tick;
            int slots = read.CheckedSlots;
            _syncReadCompleted = slots;
            RefreshConnectionSyncOptions();
            diagnostics.Record(new("ui.update", null, System.Diagnostics.Stopwatch.GetElapsedTime(tick).TotalMilliseconds, "complete"));
        };
        timer.Start();
        return timer;
    }

    private void SavePresetNameRead(Core.PresetNameReadSession read)
    {
        var previous = _settings.PresetNameCache;
        _settings.PresetNameCache = read.Names;
        try { _saveSettings(_settings); }
        catch { _settings.PresetNameCache = previous; throw; }
        UpdateFavoritePresetDisplay();
        UpdateDisplay();
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
        if (_syncOptionsButton is not null)
        {
            _syncOptionsButton.IsEnabled = _detectedDevice is not null && _midi.InputOpen && _midi.OutputOpen && !connecting && !_changingProfile;
            _syncOptionsButton.Content = syncing ? "Sync progress…" : "Sync options…";
            ToolTip.SetTip(_syncOptionsButton, _syncOptionsButton.IsEnabled ? "Refresh preset names, favorite scene names, or all saved device data." : "Connect a supported device to choose what to sync.");
        }
        if (!connecting && !syncing) { _syncReadCompleted = 0; }

        if (_favSyncPresetsButton is not null)
        {
            _favSyncPresetsButton.IsEnabled = CanSyncPresetNames;
            _favSyncPresetsButton.Content = checkingLibrary ? "Checking…" : syncing ? "Syncing…" : "↻ Sync";
            ToolTip.SetTip(_favSyncPresetsButton, !CanReadDeviceNames ? UnsupportedNameReads : connected
                ? checkingLibrary ? "Checking the connected device against its saved data. Preset-name sync will be available when this finishes."
                    : syncing ? "Preset synchronization is already running." : "Sync preset names from the connected device."
                : "Connect to the device to sync presets.");
        }
        RefreshConnectionStatus();
        RefreshConnectionSyncOptions();
    }

}
