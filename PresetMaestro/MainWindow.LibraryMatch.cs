#if FRACTAL_INDEX
using Avalonia.Threading;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
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

    private void RememberLibraryMatch(DeviceIndex cache, LibraryMatchResult? result,
        LibraryPresetDifference[]? otherNameDifferences = null)
    {
        _libraryMatch = result;
        _libraryCheckOutcome = null;
        // With a content result, name differences are kept only for slots it did not compare.
        if (result is not null) { _libraryNameDifferences = otherNameDifferences ?? []; }
        _libraryCheckBaselineAt = cache.Committed?.StartedAt;
        _matchLibraryId = cache.Device.Id;
        _matchBaselineId = cache.Committed?.Id;
        _matchConnection = _connectionGeneration;
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
        using var diagnostics = BeginSyncDiagnostics(full ? "full-check" : "quick-check");
        Core.PresetNameReadSession? nameRead = null;
        LibraryPresetDifference[] nameDifferences = [];
        _connectionSyncOutcome = null;
        _connectionSyncDeviceCheckContext = null;
        _libraryNameDifferences = [];
        _presetNamesCts = cancellation;
        _indexChecking = true;
        _indexCheckingFull = full;
        _indexCheckingNames = CanReadDeviceNames;
        _syncReadTotal = 512;
        _syncReadCompleted = 0;
        _libraryMatchProgress = new(0, full ? FractalDeviceDefinition.For(cache.Device).PresetSlots
            : Math.Min(LibraryMatch.QuickMatchPresetCount, baseline.Presets.Values.Count(LibraryMatch.IsPopulated)), 0);
        _indexProgress!.Maximum = Math.Max(1, _libraryMatchProgress.Total);
        _indexProgress.Value = 0;
        _indexProgressText!.Text = $"{(full ? "Full" : "Quick")} match · 0 of {_libraryMatchProgress.Total} presets checked";
        _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _favoriteSceneCts?.Cancel();
        UpdatePresetSyncButtons();
        try
        {
            if (_indexCheckingNames)
            {
                nameRead = new Core.PresetNameReadSession(_settings.PresetNameCache);
                var timer = StartPresetNameReadProgress(nameRead, diagnostics);
                try { await ReadPresetNamesAsync(nameRead, cancellation.Token); }
                finally { timer.Stop(); }
                cancellation.Token.ThrowIfCancellationRequested();
                if (profile != _profileGeneration || connection != _connectionGeneration || cache.Device.Id != _indexCache?.Device.Id) { return; }
                if (nameRead.RefreshedSlots.Count != 512)
                {
                    diagnostics.Result("names-incomplete", nameRead.CheckedSlots, errors: 512 - nameRead.RefreshedSlots.Count);
                    RememberLibraryMatch(cache, null);
                    SetConnectionSyncOutcome("Preset-name check incomplete",
                        $"{nameRead.RefreshedSlots.Count} of 512 names returned. Missing replies were retried, but the preset-and-scene check could not start. Saved data was kept.",
                        "Choose Check device again to retry the check, or Done to continue with saved data. A full sync is optional.", needsAttention: true);
                    return;
                }
                // Name differences never stop the content check; they are reported with its result.
                nameDifferences = LibraryMatch.NameDifferences(baseline, nameRead.Names);
                _indexCheckingNames = false;
                RefreshConnectionSyncOptions();
            }
            void Report(LibraryMatchProgress progress) => PostSyncProgress(diagnostics, null, () =>
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
            diagnostics.Result(result.Failed == 0 ? "complete" : "partial", result.Compared, errors: result.Failed);
            if (profile == _profileGeneration && connection == _connectionGeneration && cache.Device.Id == _indexCache?.Device.Id)
            {
                var compared = result.RequestedSlots.ToHashSet();
                var otherNames = nameDifferences.Where(d => !compared.Contains(d.Slot)).ToArray();
                RememberLibraryMatch(cache, result, otherNameDifferences: otherNames);
                _connectionSyncDeviceCheckContext = ConnectionSyncContext;
                SaveLibraryCheckReport(cache, baseline, result);
                string evidence = result.Compared == 0 ? "No populated saved presets were available to compare."
                    : $"{result.Matched} of {result.Compared} {(full ? "populated" : "sampled")} presets match." +
                        (result.Failed == 0 ? " Preset content and all eight scenes were compared." : " Available preset content and saved scenes were compared.");
                if (otherNames.Length > 0)
                { evidence += $" {otherNames.Length} other preset {(otherNames.Length == 1 ? "name differs" : "names differ")} from the saved device."; }
                if (result.Failed > 0) { evidence += $" {result.Failed} reads were unavailable."; }
                if (result.Caution is not null) { evidence += " " + result.Caution; }
                if (!result.IncludesLegacyFingerprints) { evidence += " Saved bypass states were ignored."; }
                if (cache.Imported) { evidence += " This imported device needs review before updating."; }
                bool confirmed = !cache.Imported && result.IsConsistent && otherNames.Length == 0;
                if (confirmed && nameRead is not null)
                {
                    SavePresetNameRead(nameRead);
                }
                SetConnectionSyncOutcome(confirmed ? (full ? "Full check complete" : "Quick check complete") : "Device check needs review",
                    evidence + (confirmed ? $" {(full ? "The device contents are" : "The sample is")} consistent with “{cache.Device.Name}”." : " The assigned device could not be confirmed.") +
                        (confirmed && nameRead is not null ? " All 512 preset names were refreshed automatically. Saved presets, scenes and amps were not refreshed." : " Your saved copy has not been updated."),
                    confirmed ? "Choose Use saved data to continue. Sync device below if you want to refresh preset content, scenes and amps."
                        : result.Failed > 0 ? "Check the MIDI connection, then choose Check device again to retry."
                        : "Review the differences above. Run a Full check for the complete picture, or choose Sync device to refresh the saved copy. Use Manage devices if another device is intended.", needsAttention: !confirmed);
                if (confirmed) { SaveDetectedLibraryFirmware(cache, result); }
                if (confirmed && _detectedDevice?.Firmware is null && _indexCache?.Committed?.EffectiveFirmware is null)
                {
                    SetConnectionSyncOutcome("Device check passed; device setup incomplete",
                        "The presets matched, but the device did not return the software information needed to show saved amp usage.",
                        "Reconnect the MIDI ports, then run Check device again to finish setup.", needsAttention: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            diagnostics.Result("cancelled", nameRead?.CheckedSlots);
            if (connection == _connectionGeneration)
            {
                RememberLibraryMatch(cache, null);
                SetConnectionSyncOutcome("Device check cancelled", "The preset comparison did not finish. Saved device data was kept.",
                    "Choose Check device again to retry, or Done to use the saved data.", needsAttention: true);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Result("failed", nameRead?.CheckedSlots);
            if (connection == _connectionGeneration)
            {
                RememberLibraryMatch(cache, null);
                SetConnectionSyncOutcome("Device check unavailable", "Saved device data was kept. " + ex.Message,
                    "Check the MIDI connection, then choose Check device again to retry.", needsAttention: true);
            }
        }
        finally
        {
            _indexChecking = false;
            _indexCheckingNames = false;
            _libraryMatchProgress = null;
            if (ReferenceEquals(_presetNamesCts, cancellation)) { _presetNamesCts = null; }
            UpdatePresetSyncButtons();
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
                result, _libraryNameDifferences));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppendLog("Could not save device check evidence: " + ex.Message); }
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
            if (!saved.Imported && confirmedMatch?.IsConsistent == true &&
                saved.Committed is { EffectiveFirmware: null } baseline && baseline.Id == cache.Committed?.Id)
            {
                // Keep the original scan and tag identity. Record how the missing
                // compatibility information was established, rather than rewriting history.
                baseline.FirmwareConfirmation = new(firmware, DateTimeOffset.UtcNow, confirmedMatch.IsSample,
                    confirmedMatch.Matched, confirmedMatch.Compared);
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
                SetConnectionSyncOutcome("Device check passed; save failed",
                    "The connected device matched the saved data, but the app could not save the update needed to show amp usage. " + ex.Message,
                    "Make sure the saved device data can be written, then choose Check device again to retry.", needsAttention: true);
            }
        }
    }
}
#endif
