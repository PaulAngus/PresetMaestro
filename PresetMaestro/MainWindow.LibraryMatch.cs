#if FRACTAL_INDEX
using Avalonia.Controls;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private NumericUpDown? _indexMatchThreshold;
    private TextBlock? _indexMatchStatus;
    private Button? _indexCheckButton;
    private bool _indexChecking;
    private LibraryMatchResult? _libraryMatch;
    private Guid? _matchLibraryId, _matchBaselineId;
    private long _matchConnection;
    private string? _matchNotice;

    private void RememberLibraryMatch(DeviceIndex cache, LibraryMatchResult? result, string? notice = null)
    {
        _libraryMatch = result;
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
        _indexMatchStatus.Text = _indexChecking ? "Checking a sample of saved presets…" :
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

    internal async Task CheckAssignedLibraryAsync()
    {
        if (_indexCache?.Committed is not { } baseline || _detectedDevice is null ||
            _indexCache.Device.Variant.ToDeviceModel() != _detectedDevice.Model ||
            _presetNamesCts is not null || _changingProfile || !_midi.InputOpen || !_midi.OutputOpen) { return; }
        var cache = IndexJson.Clone(_indexCache);
        int profile = _profileGeneration;
        long connection = _connectionGeneration;
        string? deviceName = _detectedDevice.DeviceName;
        using var cancellation = new CancellationTokenSource();
        _presetNamesCts = cancellation;
        _indexChecking = true;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _favoriteSceneCts?.Cancel();
        UpdatePresetSyncButtons();
        RefreshLibraryMatchStatus();
        try
        {
            var result = await LibraryMatch.CheckSampleAsync(_fractalIndexReader, cache, baseline, deviceName, cancellation.Token);
            if (profile == _profileGeneration && connection == _connectionGeneration && cache.Device.Id == _indexCache?.Device.Id)
            { RememberLibraryMatch(cache, result); }
        }
        catch (OperationCanceledException)
        {
            if (connection == _connectionGeneration) { RememberLibraryMatch(cache, null, "Library check cancelled. Sync will check again before updating."); }
        }
        catch (Exception ex)
        {
            if (connection == _connectionGeneration) { RememberLibraryMatch(cache, null, "Library check unavailable: " + ex.Message); }
        }
        finally
        {
            _indexChecking = false;
            if (ReferenceEquals(_presetNamesCts, cancellation)) { _presetNamesCts = null; }
            UpdatePresetSyncButtons();
            RefreshLibraryMatchStatus();
        }
    }
}
#endif
