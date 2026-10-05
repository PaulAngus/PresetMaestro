#if FRACTAL_INDEX
using Avalonia.Controls;
using Avalonia.Threading;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private NumericUpDown? _indexMatchThreshold;
    private TextBlock? _indexMatchStatus;
    private Button? _indexCheckButton;
    private bool _indexChecking;
    private bool _indexCheckingFull;
    private bool _indexCheckingNames;
    private LibraryMatchProgress? _libraryMatchProgress;
    private LibraryMatchResult? _libraryMatch;
    private LibraryPresetDifference[] _libraryNameDifferences = [];
    private DateTimeOffset? _libraryCheckBaselineAt;
    private SyncDialogOutcome? _libraryCheckOutcome;
    private Guid? _matchLibraryId, _matchBaselineId;
    private long _matchConnection;
    private string? _matchNotice;

    private void RememberLibraryMatch(DeviceIndex cache, LibraryMatchResult? result, string? notice = null)
    {
        _libraryMatch = result;
        _libraryCheckOutcome = null;
        if (result is not null) { _libraryNameDifferences = []; }
        _libraryCheckBaselineAt = cache.Committed?.StartedAt;
        _matchLibraryId = cache.Device.Id;
        _matchBaselineId = cache.Committed?.Id;
        _matchConnection = _connectionGeneration;
        _matchNotice = notice;
        RefreshLibraryMatchStatus();
    }

    private void RefreshLibraryMatchStatus()
    {
        if (_indexMatchStatus is null) { return; }
        bool current = _matchLibraryId == _indexCache?.Device.Id && _matchBaselineId == _indexCache?.Committed?.Id &&
            _matchConnection == _connectionGeneration;
        _indexMatchStatus.Text = _indexChecking ? (_indexCheckingNames ? "Checking preset names before reading preset content…" :
            _indexCheckingFull ? "Full match: checking every saved preset and its scenes…" : "Quick match: checking random saved presets and their scenes…") :
            _indexCache is null ? "Assign a device library in Config to check its presets." :
            _detectedDevice is null ? "Device library check: connect the device to compare saved presets." :
            _indexCache.Device.Variant.ToDeviceModel() != _detectedDevice.Model ? "Device model differs from this library." :
            current && _libraryMatch is not null ? _libraryMatch.Describe(_indexProfile.PresetMatchThresholdPercent) +
                (_indexCache.Imported ? " Imported library: confirm before updating." : "") :
            current && _matchNotice is not null ? _matchNotice :
            _indexCache.Committed is null ? "No complete baseline yet. The first confirmed sync will establish it." :
            "Device library has not been checked on this connection. Choose Check library or Sync device.";
        ToolTip.SetTip(_indexMatchStatus, "Matches compare saved preset content at the same slots. A sample is not a full-library percentage. Identical backups can match on different physical units.");
    }

    internal async Task CheckAssignedLibraryAsync(bool full = false)
    {
        if (_connectionLibrarySetupRequired) { return; }
        if (_indexCache is null || _detectedDevice is null ||
            _indexCache.Device.Variant.ToDeviceModel() != _detectedDevice.Model ||
            _presetNamesCts is not null || _changingProfile || !_midi.InputOpen || !_midi.OutputOpen) { return; }
        if (_indexCache.Committed is not { } baseline)
        {
            // An assigned empty library has no saved identity evidence to contradict.
            // A partial or imported library must first be confirmed through Sync.
            if (_indexCache.Browsable is null && !_indexCache.Imported) { SaveDetectedLibraryFirmware(_indexCache); }
            return;
        }
        var cache = IndexJson.Clone(_indexCache);
        cache.Device = cache.Device with { Firmware = _detectedDevice.Firmware };
        int profile = _profileGeneration;
        long connection = _connectionGeneration;
        string? deviceName = _detectedDevice.DeviceName;
        using var cancellation = new CancellationTokenSource();
        Core.PresetNameReadSession? nameRead = null;
        _connectionSyncOutcome = null;
        _libraryNameDifferences = [];
        _presetNamesCts = cancellation;
        _indexChecking = true;
        _indexCheckingFull = full;
        _indexCheckingNames = CanReadDeviceNames;
        _syncReadTotal = 512;
        _syncReadCompleted = 0;
        _libraryMatchProgress = new(0, full ? FractalDeviceDefinition.For(cache.Device.Variant).PresetSlots
            : Math.Min(LibraryMatch.QuickMatchPresetCount, baseline.Presets.Values.Count(LibraryMatch.IsPopulated)), 0);
        _indexProgress!.Maximum = Math.Max(1, _libraryMatchProgress.Total);
        _indexProgress.Value = 0;
        _indexProgressText!.Text = $"{(full ? "Full" : "Quick")} match · 0 of {_libraryMatchProgress.Total} presets checked";
        _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _favoriteSceneCts?.Cancel();
        UpdatePresetSyncButtons();
        RefreshLibraryMatchStatus();
        try
        {
            if (_indexCheckingNames)
            {
                nameRead = new Core.PresetNameReadSession(_settings.PresetNameCache);
                var timer = StartPresetNameReadProgress(nameRead);
                try { await ReadPresetNamesAsync(nameRead, cancellation.Token); }
                finally { timer.Stop(); }
                cancellation.Token.ThrowIfCancellationRequested();
                if (profile != _profileGeneration || connection != _connectionGeneration || cache.Device.Id != _indexCache?.Device.Id) { return; }
                if (nameRead.RefreshedSlots.Count != 512)
                {
                    RememberLibraryMatch(cache, null, "Preset-name check incomplete. Preset content was not checked.");
                    SetConnectionSyncOutcome("Preset-name check incomplete",
                        $"{nameRead.RefreshedSlots.Count} of 512 names returned. The preset-and-scene check did not start. Saved data was kept.",
                        "Check the MIDI connection, then choose Check library again to retry.", needsAttention: true);
                    return;
                }
                var names = LibraryMatch.CompareNames(baseline, nameRead.Names);
                if (names.Compared > 0 && names.Matched * 100L < names.Compared * (long)_indexProfile.PresetMatchThresholdPercent)
                {
                    RememberLibraryMatch(cache, null, $"Preset-name check: {names.Matched} of {names.Compared} populated slots match. Preset content was not checked.");
                    _libraryNameDifferences = LibraryMatch.NameDifferences(baseline, nameRead.Names);
                    SaveLibraryCheckReport(cache, baseline, null);
                    SetConnectionSyncOutcome("Preset names differ from this library",
                        $"{names.Matched} of {names.Compared} populated slots have matching names. The longer preset-and-scene check was skipped. Saved data was kept.",
                        "If you changed presets on this device, choose Sync library. Otherwise use Manage libraries to select its saved library.", needsAttention: true);
                    return;
                }
                _indexCheckingNames = false;
                RefreshConnectionSyncOptions();
            }
            void Report(LibraryMatchProgress progress) => Dispatcher.UIThread.Post(() =>
            {
                if (!_indexChecking || !ReferenceEquals(_presetNamesCts, cancellation)) { return; }
                _libraryMatchProgress = progress;
                _indexProgress!.Maximum = Math.Max(1, progress.Total);
                _indexProgress.Value = progress.Checked;
                _indexProgressText!.Text = $"{(full ? "Full" : "Quick")} match · {progress.Checked} of {progress.Total} presets checked · {progress.Failed} failed";
                RefreshConnectionSyncOptions();
            });
            var result = full
                ? await LibraryMatch.CheckFullAsync(_fractalIndexReader, cache, baseline, deviceName, cancellation.Token, Report)
                : await LibraryMatch.CheckSampleAsync(_fractalIndexReader, cache, baseline, deviceName, cancellation.Token, Report);
            if (profile == _profileGeneration && connection == _connectionGeneration && cache.Device.Id == _indexCache?.Device.Id)
            {
                RememberLibraryMatch(cache, result);
                SaveLibraryCheckReport(cache, baseline, result);
                string evidence = result.Compared == 0 ? "No populated saved presets were available to compare."
                    : $"{result.Matched} of {result.Compared} {(full ? "populated" : "sampled")} presets match ({result.Percent:0.##}%)." +
                        (result.Failed == 0 ? " Preset content and all eight scenes were compared." : " Available preset content and saved scenes were compared.");
                if (result.Failed > 0) { evidence += $" {result.Failed} reads were unavailable."; }
                if (result.Caution is not null) { evidence += " " + result.Caution; }
                if (!result.IncludesLegacyFingerprints) { evidence += " Saved bypass states were ignored."; }
                if (cache.Imported) { evidence += " This imported library needs review before updating."; }
                bool confirmed = !cache.Imported && result.MeetsThreshold(_indexProfile.PresetMatchThresholdPercent);
                if (confirmed && nameRead is not null)
                {
                    SavePresetNameRead(nameRead);
                }
                SetConnectionSyncOutcome(confirmed ? (full ? "Full check complete" : "Quick check complete") : "Library check needs review",
                    evidence + (confirmed ? $" {(full ? "The device contents are" : "The sample is")} consistent with “{cache.Device.Name}”." : " The assigned library could not be confirmed.") +
                        (confirmed && nameRead is not null ? " All 512 preset names were refreshed automatically. Library presets, scenes and amps were not refreshed." : " Your saved copy has not been updated."),
                    confirmed ? "Choose Use saved library to continue. Sync library below if you want to refresh preset content, scenes and amps."
                        : result.Failed > 0 ? "Check the MIDI connection, then choose Check library again to retry."
                        : "Review the differences above. Run a Full check for the complete picture, or choose Sync library to refresh the saved copy. Use Manage libraries if another library is intended.", needsAttention: !confirmed);
                if (!cache.Imported && result.MeetsThreshold(_indexProfile.PresetMatchThresholdPercent))
                { SaveDetectedLibraryFirmware(cache, result); }
                if (confirmed && _detectedDevice?.Firmware is null && _indexCache?.Committed?.EffectiveFirmware is null)
                {
                    SetConnectionSyncOutcome("Library check passed; device setup incomplete",
                        "The presets matched, but the device did not return the software information needed to show saved amp usage.",
                        "Reconnect the MIDI ports, then run Check library again to finish setup.", needsAttention: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (connection == _connectionGeneration)
            {
                RememberLibraryMatch(cache, null, "Library check cancelled. Sync will check again before updating.");
                SetConnectionSyncOutcome("Library check cancelled", "The preset comparison did not finish. Saved library data was kept.",
                    "Choose Check library again to retry, or Done to use the saved data.", needsAttention: true);
            }
        }
        catch (Exception ex)
        {
            if (connection == _connectionGeneration)
            {
                RememberLibraryMatch(cache, null, "Library check unavailable: " + ex.Message);
                SetConnectionSyncOutcome("Library check unavailable", "Saved library data was kept. " + ex.Message,
                    "Check the MIDI connection, then choose Check library again to retry.", needsAttention: true);
            }
        }
        finally
        {
            _indexChecking = false;
            _indexCheckingNames = false;
            _libraryMatchProgress = null;
            if (ReferenceEquals(_presetNamesCts, cancellation)) { _presetNamesCts = null; }
            UpdatePresetSyncButtons();
            RefreshLibraryMatchStatus();
            RefreshAmpsContext();
            RenderIndexResults();
            CompleteInitialConnectionSync();
        }
    }

    private void SaveLibraryCheckReport(DeviceIndex cache, IndexScan baseline, LibraryMatchResult? result)
    {
        if (_profileStore is null) { return; }
        try
        {
            new LibraryCheckReportStore(_indexLibrary.DirectoryPath).Save(new(cache.Device.Id, baseline.Id,
                baseline.StartedAt, DateTimeOffset.UtcNow, _detectedDevice?.DeviceName, _detectedDevice?.Firmware,
                _indexProfile.PresetMatchThresholdPercent, result, _libraryNameDifferences));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppendLog("Could not save library check evidence: " + ex.Message); }
    }

    private void SaveDetectedLibraryFirmware(DeviceIndex cache, LibraryMatchResult? confirmedMatch = null)
    {
        if (_detectedDevice?.Firmware is not { } firmware || _profileStore is null ||
            _indexError is not null || cache.Device.Id != _indexProfile.SelectedDeviceId ||
            cache.Device.Variant.ToDeviceModel() != _detectedDevice.Model) { return; }
        try
        {
            var saved = _indexLibrary.Load(cache.Device.Id) ?? cache;
            bool repaired = false;
            if (!saved.Imported && confirmedMatch?.MeetsThreshold(_indexProfile.PresetMatchThresholdPercent) == true &&
                saved.Committed is { EffectiveFirmware: null } baseline && baseline.Id == cache.Committed?.Id)
            {
                // Keep the original scan and tag identity. Record how the missing
                // compatibility information was established, rather than rewriting history.
                baseline.FirmwareConfirmation = new(firmware, DateTimeOffset.UtcNow, confirmedMatch.IsSample,
                    confirmedMatch.Matched, confirmedMatch.Compared, _indexProfile.PresetMatchThresholdPercent);
                if (saved.LastAttempt?.Id == baseline.Id && saved.LastAttempt.EffectiveFirmware is null)
                { saved.LastAttempt.FirmwareConfirmation = baseline.FirmwareConfirmation; }
                repaired = true;
            }
            if (saved.Device.Firmware == firmware && !repaired) { return; }
            saved.Device = saved.Device with { Firmware = firmware };
            _indexLibrary.Save(saved);
            SaveIndexProfile(profile => profile.AssignDevice(saved.Device));
            RefreshIndexContext();
        }
        catch (Exception ex)
        {
            SetIndexMessage("Could not save detected firmware: " + ex.Message);
            if (confirmedMatch is not null)
            {
                SetConnectionSyncOutcome("Library check passed; save failed",
                    "The device matched this library, but the app could not save the update needed to show amp usage. " + ex.Message,
                    "Make sure the library files can be saved, then choose Check library again to retry.", needsAttention: true);
            }
        }
    }
}
#endif
