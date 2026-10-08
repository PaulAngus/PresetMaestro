#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private PresetIndexReader _fractalIndexReader = null!;

    private IndexLibrary _indexLibrary = null!;
    private ProfileLibraryCoordinator _libraryProfiles = null!;
    private IndexProfile _indexProfile = new();
    private DeviceIndex? _indexCache;
    private string? _indexError;
    private bool _refreshingIndex, _indexScanning, _indexResumeChecking;
    private ComboBox? _indexAvailableDevices;
    private TextBlock? _indexDeviceLabel, _indexAssignmentLabel;
    private TextBox? _indexDeviceName;
    private TextBlock? _indexFirmware;
    private ComboBox? _indexVariant;
    private TextBlock? _indexConfigStatus;

    private void InitializeFractalIndex()
    {
        _fractalIndexReader = new(_presetNameClient, AmpModelCatalogRegistry.CreateStarter());
        _indexLibrary = new(Path.Combine(_profileStore?.DirectoryPath ?? Path.GetTempPath(), "FractalIndex"));
        _libraryProfiles = new(_settings, _indexLibrary, _saveSettings);
        LoadIndexContext();
    }

    private void LoadIndexContext()
    {
        try
        {
            (_indexProfile, _indexCache) = _libraryProfiles.Load(_profileStore is not null);
            if (_connectionLibraryCandidate is { } candidate)
            { _indexCache = _connectionLibraryStaging?.Load(candidate.Device.Id) ?? candidate; }
            if (_indexCache is not null)
            {
                if (_indexCache.PresetMapping is { } mapping)
                {
                    _settings.MidiChannel = mapping.MidiChannel;
                    _settings.DisplayOffset = mapping.DisplayOffset;
                    _settings.SceneCc = mapping.SceneCc;
                }
            }
            _indexError = null;
            SynchronizeSceneTags();
            if (_profileStore is not null && _indexProfile.PortableAmpReferences.Count > 0)
            {
                try { AmpReferences.Merge(_indexProfile.PortableAmpReferences); _ampReferenceError = null; }
                catch (Exception ex) { _ampReferenceError = "Could not import Wiki links: " + ex.Message; }
            }
        }
        catch (Exception ex) { _indexError = ex.Message; _indexCache = null; _indexProfile = new(); }
    }

    partial void RefreshIndexContext()
    {
        if (_indexLibrary is null) { return; }
        var previousMapping = new DevicePresetMapping(_settings.MidiChannel, _settings.DisplayOffset, _settings.SceneCc);
        int previousCapacity = PickerCapacity;
        LoadIndexContext();
        if (_favListBox is not null) { RefreshFavoritesList(); }
        if (_favPresetSpinner is not null && (previousCapacity != PickerCapacity || previousMapping != new DevicePresetMapping(_settings.MidiChannel, _settings.DisplayOffset, _settings.SceneCc)))
        {
            UpdatePresetCapacityUI();
            UpdateDisplay();
        }
        _refreshingIndex = true;
        try
        {
            string assignment = _indexCache?.Device.Name ?? _indexProfile.AssignedDevice?.Name ?? "Not assigned";
            if (_indexDeviceLabel is not null)
            {
                _indexDeviceLabel.Text = "Device: " + assignment;
                ToolTip.SetTip(_indexDeviceLabel, $"Device assigned to profile '{_settings.ActiveProfile}': {assignment}. Change the assignment in Config.");
            }
            if (_indexAssignmentLabel is not null) { _indexAssignmentLabel.Text = $"Selected device: “{assignment}”"; }
            if (_indexAvailableDevices is not null && _profileStore is not null)
            {
                var devices = AvailableIndexDevices();
                _indexAvailableDevices.ItemsSource = devices;
                _indexAvailableDevices.SelectedItem = devices.FirstOrDefault(d => d.Id == _indexProfile.SelectedDeviceId);
            }
        }
        catch (Exception ex) { _indexError = ex.Message; }
        finally { _refreshingIndex = false; }
        RefreshManagedLibraries();
        RefreshAmpsContext();
        RenderIndexResults();
        UpdateIndexButtons();
        RefreshConnectionStatus();
    }

    private bool SaveIndexProfile(Action<IndexProfile> change)
    {
        if (_indexError is not null || _profileStore is null) { return false; }
        try
        {
            var updated = IndexJson.Clone(_indexProfile);
            change(updated);
            if (updated.SelectedDeviceId != _indexProfile.SelectedDeviceId)
            {
                _indexProfile = _libraryProfiles.Update(updated, _ => { });
                return true;
            }
            FavoritesManager.MutateAndSave(_favorites,
                () => SharedSceneTags.Reconcile(updated, _indexCache, _favorites, _settings.DisplayOffset, importLegacy: false),
                _ => PersistSharedSceneTags(updated));
            if (_favListBox is not null) { RefreshFavoritesList(); }
            return true;
        }
        catch (Exception ex)
        {
            SetIndexMessage("Could not save tags or device settings: " + ex.Message);
            return false;
        }
    }

    partial void AddIndexHeader(StackPanel indicators)
    {
        _indexDeviceLabel = new TextBlock { Name = "IndexAssignedDevice", MaxWidth = 150, FontSize = 12, Foreground = SecondaryBrush, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, IsVisible = _currentPage == AppPage.PresetIndex };
        indicators.Children.Insert(0, _indexDeviceLabel);
        RefreshIndexContext();
    }

    partial void UpdateIndexHeader(double width)
    {
        // Reserve room for navigation, connection status and long profile names.
        if (_indexDeviceLabel is not null)
        {
            _indexDeviceLabel.IsVisible = width >= 1280 && _currentPage is (AppPage.PresetIndex or AppPage.Amps);
        }
    }

    private sealed record IndexVariantChoice(FractalDeviceVariant Variant, string Label)
    {
        public override string ToString() => Label;
    }

    partial void AddIndexConfiguration(StackPanel panel)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "Device for this profile",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Presets, scenes and amps are saved on this PC for offline use. Profiles for the same device share this data.",
            Foreground = SecondaryBrush,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        var content = new StackPanel { Spacing = 8, IsEnabled = _profileStore is not null };
        _indexAssignmentLabel = new TextBlock { Name = "IndexAssignment", FontSize = 12, Foreground = TextBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        content.Children.Add(_indexAssignmentLabel);
        _indexAvailableDevices = new ComboBox { Name = "IndexAvailableDevices", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a saved device" };
        AutomationProperties.SetName(_indexAvailableDevices, "Saved device used by this profile");
        _indexAvailableDevices.ItemTemplate = new FuncDataTemplate<IndexDevice>((device, _) =>
        {
            string name = device is null ? "" : LibraryDisplayName(device);
            var label = new TextBlock { Text = name, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(label, name);
            AutomationProperties.SetName(label, name);
            return label;
        });
        _indexAvailableDevices.SelectionChanged += (_, _) =>
        {
            if (!_refreshingIndex && _indexAvailableDevices.SelectedItem is IndexDevice device && device.Id != _indexProfile.SelectedDeviceId)
            { AssignIndexLibrary(device); }
        };
        content.Children.Add(CompactField("Select device", _indexAvailableDevices));
        var manage = IndexButton("Manage devices…", "ManageLibraries");
        manage.Click += (_, _) => _showManageLibraries?.Invoke();
        content.Children.Add(manage);
        content.Children.Add(new TextBlock
        {
            Text = "Imported a profile for the same device? Select the device you already use. Manage devices lets you rename devices or remove unused copies of their saved data.",
            FontSize = 12,
            Foreground = SecondaryBrush,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        _indexConfigStatus = new TextBlock { Name = "IndexDeviceStatus", FontSize = 12, Foreground = SecondaryBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        content.Children.Add(_indexConfigStatus);
        panel.Children.Add(content);
    }

    private void SetIndexMessage(string message)
    {
        if (_indexStatus is not null) { _indexStatus.Text = message; }
        if (_indexConfigStatus is not null) { _indexConfigStatus.Text = message; }
        if (_libraryManagementStatus is not null) { _libraryManagementStatus.Text = message; }
    }

    private bool IndexDeviceMatchesProfile(FractalDeviceVariant variant)
    {
        if (variant.ToDeviceModel() == _settings.DeviceModel) { return true; }
        SetIndexMessage($"This profile is configured for {_settings.DeviceModel}. Choose a device with the same model, or connect the intended device to configure this profile first.");
        return false;
    }

    private bool CanSyncIndex => !_changingProfile && _connectionCts is null && _presetNamesCts is null && _indexError is null &&
            !_connectionLibraryChoiceBusy && (!_connectionLibrarySetupRequired || _connectionLibraryVariant is not null && (_connectionDefaultLibrary is null || _connectionLibraryCandidate is not null)) &&
            _indexCache is not null && _settings.DeviceModel == _indexCache.Device.Variant.ToDeviceModel() && _detectedDevice?.Model == _indexCache.Device.Variant.ToDeviceModel() && _midi.InputOpen && _midi.OutputOpen;

    private bool CanCheckIndex => CanSyncIndex && !_connectionLibrarySetupRequired && _indexCache?.Committed is not null;

    partial void UpdateIndexButtons()
    {
        if (_indexResumeButton is null) { return; }
        UpdateIndexSendControls();
        _indexResumeButton.IsEnabled = CanSyncIndex && _indexCache?.LastAttempt is { Status: not "Complete" };
        _indexResumeButton.IsVisible = _indexCache?.LastAttempt is { Status: not "Complete" };
        _indexCancelButton!.IsVisible = _indexScanning || _indexChecking;
        _indexProgressPanel!.IsVisible = _indexScanning || _indexChecking;
        RefreshIndexConnectionSyncCompletion();
        UpdateLibraryManagementButtons();
        UpdateIndexHeader(ClientSize.Width);
        if (_indexError is not null) { SetIndexMessage(_indexError + " The original data has been preserved."); }
    }

    internal async Task SyncIndexAsync(bool resume = false)
    {
        var cache = _indexCache is null ? null : IndexJson.Clone(_indexCache);
        if (cache is null || !CanSyncIndex) { return; }
        int generation = _profileGeneration;
        long connection = _connectionGeneration;
        string deviceCheckContext = ConnectionSyncContext;
        string? deviceName = _detectedDevice!.DeviceName;
        // Every new read uses this connection's reported firmware, including null
        // on failure. Never carry a previous connection's version into a new scan.
        cache.Device = cache.Device with { Firmware = _detectedDevice.Firmware };
        var baseline = cache.Committed;
        using var cancellation = new CancellationTokenSource();
        using var diagnostics = BeginSyncDiagnostics(resume ? "resume-sync" : "sync");
        bool diagnosticScanStarted = false;
        string diagnosticOutcome = "not-started";
        _presetNamesCts = cancellation;
        _indexScanning = true;
        _showConnectionSyncChecks = false;
        _connectionSyncOutcome = null;
        _connectionSyncDeviceCheckContext = null;
        _connectionSyncMatchedContext = null;
        _indexProgressMessage = "Preparing sync… Completed reads are saved as it runs.";
        _indexProgressText!.Text = _indexProgressMessage;
        _indexProgress!.Value = 0;
        _indexProgress.Maximum = FractalDeviceDefinition.For(cache.Device).PresetSlots;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        UpdatePresetSyncButtons();
        RenderIndexResults();
        string outcome = "";
        bool libraryUpdated = false;
        bool profileNamesUpdated = false;
        try
        {
            if (baseline is null && !_connectionLibrarySetupRequired && !await ConfirmProfileAsync("Save device data",
                $"Use the connected {_detectedDevice.ModelLabel} as the baseline for “{cache.Device.Name}”? The saved-preset read can take several minutes. Cancel keeps completed reads.", "Sync device"))
            { outcome = "Sync cancelled. Saved device data was not changed."; return; }
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _profileGeneration || connection != _connectionGeneration) { return; }
            await EstablishPresetCapacityAsync(cache, cancellation.Token);
            if (generation != _profileGeneration || connection != _connectionGeneration) { return; }
            // A partial scan may belong to an earlier connection. Check it before reusing reads.
            if (resume && cache.LastAttempt is { Status: not "Complete" } partial)
            {
                _indexResumeChecking = true;
                _indexProgressText!.Text = "Checking saved reads before resuming…";
                RefreshConnectionSyncOptions();
                var check = await LibraryMatch.CheckSampleAsync(_fractalIndexReader, cache, partial, deviceName, cancellation.Token,
                    progress => Dispatcher.UIThread.Post(() =>
                    {
                        if (!_indexResumeChecking || !ReferenceEquals(_presetNamesCts, cancellation)) { return; }
                        _indexProgress!.Maximum = Math.Max(1, progress.Total);
                        _indexProgress.Value = progress.Checked;
                        _indexProgressText.Text = $"Checking saved reads before resuming… {progress.Checked} of {progress.Total} presets checked.";
                        RefreshConnectionSyncOptions();
                    }));
                _indexResumeChecking = false;
                if (!check.IsConsistent)
                {
                    resume = false;
                    _indexProgressText.Text = "Previous reads could not be confirmed. Starting a fresh scan…";
                }
                _indexProgress!.Maximum = FractalDeviceDefinition.For(cache.Device).PresetSlots;
                _indexProgress.Value = resume ? partial.Presets.Count : 0;
                RefreshConnectionSyncOptions();
            }
            diagnosticScanStarted = true;
            await new IndexScanner(_fractalIndexReader, _connectionLibraryStaging ?? _indexLibrary).ScanAsync(cache, resume, progress => PostSyncProgress(diagnostics, progress.Slot, () =>
            {
                if (!_indexScanning || !ReferenceEquals(_presetNamesCts, cancellation)) { return; }
                int checkedSlots = Math.Min(progress.Total, progress.Read + progress.Failed);
                _indexProgress!.Maximum = progress.Total;
                _indexProgress.Value = checkedSlots;
                _indexProgressMessage = progress.Retrying
                    ? $"Retrying preset {progress.Slot + _settings.DisplayOffset:D3} after no reply (attempt 2 of 2)… {progress.Read} of {progress.Total} slots saved."
                    : $"{checkedSlots * 100 / progress.Total}% · {checkedSlots} of {progress.Total} slots checked · {progress.Read - progress.EmptySkipped} presets read · {progress.EmptySkipped} empty skipped · {progress.Failed} need retry";
                _indexProgressText!.Text = _indexProgressMessage;
                RefreshConnectionSyncOptions();
            }), cancellation.Token, publish: false, connectedDeviceName: deviceName);
            if (cache.LastAttempt!.Status != "Complete")
            { outcome = "Partial scan saved. Resume to retry missing presets."; return; }
            var candidate = cache.LastAttempt;
            LibraryMatchResult? match = baseline is null ? null : LibraryMatch.Compare(baseline, candidate.Presets,
                false, deviceName, candidate.Firmware);
            if (match is not null)
            {
                RememberLibraryMatch(cache, match);
                _connectionSyncDeviceCheckContext = deviceCheckContext;
                if ((!match.IsConsistent || cache.Imported) && !await ConfirmLibraryUpdateAsync(cache, match))
                { outcome = "Previous device data kept. New reads were saved separately; they have not replaced its baseline."; return; }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _profileGeneration || connection != _connectionGeneration) { return; }
            cache.Committed = IndexJson.Clone(candidate);
            cache.Imported = false;
            PublishConnectionLibrary(cache);
            libraryUpdated = true;
            CacheLibraryNames(candidate);
            profileNamesUpdated = true;
            _connectionLibrarySetupRequired = false;
            _connectionSyncCheckedContext = ConnectionSyncContext;
            _connectionSyncMatchedContext = ConnectionSyncContext;
            _connectionSyncDeviceCheckContext = ConnectionSyncContext;
            _showConnectionSyncChecks = false;
            RememberLibraryMatch(cache, match);
            outcome = $"Presets, scenes and amps refreshed in “{cache.Device.Name}”. This profile’s preset and scene names were also refreshed automatically. {candidate.Presets.Values.Count(p => p.NameOnlyEmpty)} empty slots skipped.";
        }
        catch (OperationCanceledException)
        {
            diagnosticOutcome = "cancelled";
            outcome = cache.LastAttempt is null ? "Sync cancelled. Saved device data was not changed." : cache.LastAttempt.Status == "Complete"
                ? "Device data update cancelled. Completed reads were saved separately."
                : "Sync cancelled. Completed reads were saved; Resume continues this scan.";
        }
        catch (Exception ex) { diagnosticOutcome = "failed"; outcome = "Sync stopped: " + ex.Message; }
        finally
        {
            var scan = diagnosticScanStarted ? cache.LastAttempt : null;
            diagnostics.Result(scan?.Status.ToLowerInvariant() ?? diagnosticOutcome, scan?.Presets.Count,
                scan?.Presets.Values.Count(p => !string.Equals(p.Name.Trim(), "<EMPTY>", StringComparison.Ordinal)),
                scan?.Presets.Values.Count(p => string.Equals(p.Name.Trim(), "<EMPTY>", StringComparison.Ordinal)), scan?.Errors.Count);
            _indexScanning = false;
            _indexResumeChecking = false;
            _presetNamesCts = null;
            RefreshIndexContext();
            SetIndexMessage(outcome);
            SetConnectionSyncOutcome(libraryUpdated && profileNamesUpdated ? "Device synced" : "Device sync needs attention",
                outcome,
                libraryUpdated && profileNamesUpdated ? "Choose Use saved data to continue. Preset Index, Amps and Favorites now use the refreshed data."
                    : cache.LastAttempt is { Status: not "Complete" } ? "Next: Choose Resume to retry missing presets, or Done to use the data already saved."
                        : "Choose Sync device to retry or review an update, or Done to continue with saved data.", needsAttention: !libraryUpdated || !profileNamesUpdated);
            UpdatePresetSyncButtons();
            if (!libraryUpdated && _connectionSyncDialog is { IsVisible: true })
            {
                (_indexCache?.LastAttempt is { Status: not "Complete" } ? _connectionSyncResume : _connectionSyncLibrary)?.Focus();
            }
            CompleteInitialConnectionSync();
            if (!_connectionLibrarySetupRequired && _detectedDevice is not null) { StartSceneTracking(); }
        }
    }

    private void CacheLibraryNames(IndexScan scan)
    {
        UpdatePresetCapacityUI();
        // The full scan already returned these names. Reuse them rather than
        // issuing a second set of MIDI reads after an accepted library update.
        _settings.PresetNameCache = scan.Presets.ToDictionary(p => p.Key, p => p.Value.Name);
        var cache = SceneCache;
        foreach (var preset in scan.Presets.Values)
        {
            if (preset.Slot == _sceneSlot && cache.TryGetValue(preset.Slot, out var live) && live.Source == "live") { continue; }
            cache[preset.Slot] = new Core.SceneCacheEntry
            {
                Names = preset.NameOnlyEmpty ? Enumerable.Repeat("", 8).ToArray() : preset.SceneNames.ToArray(),
                Source = "stored",
                RetrievedAt = DateTimeOffset.UtcNow,
            };
        }
        _saveSettings(_settings);
        UpdateFavoritePresetDisplay();
        PopulateFavoriteScenes();
        RefreshFavoritesList();
        UpdateDisplay();
    }

    /// <summary>Feature registration seam. Only called for an explicitly selected, connected device.</summary>
    internal Task<IndexedPreset> ReadIndexedPresetAsync(
        FractalDeviceVariant variant, Version? firmware, int slot, CancellationToken token)
    {
        DeviceModel? connected = _detectedDevice?.Model;
        if (variant.ToDeviceModel() != connected)
        {
            throw new InvalidOperationException("Connect and select the matching Fractal device before reading its index.");
        }
        return _fractalIndexReader.ReadAsync(FractalDeviceDefinition.For(variant), firmware, slot, token);
    }
}
#endif
