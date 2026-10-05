#if FRACTAL_INDEX
using Avalonia.Automation;
using Avalonia;
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
    private bool _refreshingIndex, _indexScanning;
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
        LoadIndexContext();
        if (_favPresetSpinner is not null && previousMapping != new DevicePresetMapping(_settings.MidiChannel, _settings.DisplayOffset, _settings.SceneCc))
        {
            UpdatePresetCapacityUI();
            RefreshFavoritesList();
            UpdateDisplay();
        }
        _refreshingIndex = true;
        try
        {
            string assignment = _indexCache?.Device.Name ?? _indexProfile.AssignedDevice?.Name ?? "Not assigned";
            if (_indexDeviceLabel is not null)
            {
                _indexDeviceLabel.Text = "Library: " + assignment;
                ToolTip.SetTip(_indexDeviceLabel, $"Device library assigned to profile '{_settings.ActiveProfile}': {assignment}. Change the assignment in Config.");
            }
            if (_indexAssignmentLabel is not null) { _indexAssignmentLabel.Text = $"{_settings.ActiveProfile} → {assignment}"; }
            if (_indexMatchThreshold is not null) { _indexMatchThreshold.Value = _indexProfile.PresetMatchThresholdPercent; }
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
    }

    private bool SaveIndexProfile(Action<IndexProfile> change)
    {
        if (_indexError is not null || _profileStore is null) { return false; }
        try
        {
            _indexProfile = _libraryProfiles.Update(_indexProfile, change);
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

    private sealed record IndexVariantChoice(FractalDeviceVariant Variant, string Label)
    {
        public override string ToString() => Label;
    }

    partial void AddIndexConfiguration(StackPanel panel)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "Device library for this profile",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = TextBrush,
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "One library per profile. Several profiles can share its saved device data.",
            Foreground = SecondaryBrush,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        var content = new StackPanel { Spacing = 8, IsEnabled = _profileStore is not null };
        _indexAssignmentLabel = new TextBlock { Name = "IndexAssignment", FontSize = 12, Foreground = TextBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        content.Children.Add(_indexAssignmentLabel);
        _indexAvailableDevices = new ComboBox { Name = "IndexAvailableDevices", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a saved device library" };
        _indexAvailableDevices.ItemTemplate = new FuncDataTemplate<IndexDevice>((device, _) => new TextBlock
        {
            Text = device is null ? "" : LibraryDisplayName(device),
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        });
        _indexAvailableDevices.SelectionChanged += (_, _) =>
        {
            if (!_refreshingIndex && _indexAvailableDevices.SelectedItem is IndexDevice device && device.Id != _indexProfile.SelectedDeviceId)
            { AssignIndexLibrary(device); }
        };
        content.Children.Add(CompactField("Assigned library", _indexAvailableDevices));
        var manage = IndexButton("Manage library", "ManageLibraries");
        manage.Click += (_, _) => _showManageLibraries?.Invoke();
        content.Children.Add(manage);
        _indexMatchThreshold = new CompactNumericUpDown
        {
            Name = "IndexMatchThreshold",
            Minimum = 1,
            Maximum = 100,
            Increment = 1,
            FormatString = "0",
            Value = _indexProfile.PresetMatchThresholdPercent,
            Width = 100
        };
        AutomationProperties.SetName(_indexMatchThreshold, "Preset match threshold percent");
        _indexMatchThreshold.ValueChanged += (_, _) =>
        {
            if (_refreshingIndex || _indexScanning || _indexChecking || _changingProfile || _indexMatchThreshold.Value is not decimal value) { return; }
            int rounded = (int)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
            if (SaveIndexProfile(profile => profile.PresetMatchThresholdPercent = rounded)) { RefreshLibraryMatchStatus(); }
            if (_indexMatchThreshold.Value != _indexProfile.PresetMatchThresholdPercent)
            {
                _refreshingIndex = true;
                try { _indexMatchThreshold.Value = _indexProfile.PresetMatchThresholdPercent; }
                finally { _refreshingIndex = false; }
            }
        };
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
        {
            new TextBlock { Text = "Preset match threshold (%)", VerticalAlignment = VerticalAlignment.Center, Foreground = TextBrush },
            _indexMatchThreshold,
        }
        });
        content.Children.Add(new TextBlock
        {
            Text = "For this profile. Empty slots are excluded. A match suggests the library belongs to this device; it cannot identify a physical unit.",
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
        SetIndexMessage($"This profile is configured for {_settings.DeviceModel}. Choose a matching library, or connect the intended device to configure this profile first.");
        return false;
    }

    partial void UpdateIndexButtons()
    {
        if (_indexSyncButton is null) { return; }
        bool ready = !_changingProfile && _connectionCts is null && _presetNamesCts is null && _indexError is null &&
            !_connectionLibraryChoiceBusy && (!_connectionLibrarySetupRequired || _connectionLibraryVariant is not null && (_connectionDefaultLibrary is null || _connectionLibraryCandidate is not null)) &&
            _indexCache is not null && _settings.DeviceModel == _indexCache.Device.Variant.ToDeviceModel() && _detectedDevice?.Model == _indexCache.Device.Variant.ToDeviceModel() && _midi.InputOpen && _midi.OutputOpen;
        _indexSyncButton.IsEnabled = ready;
        _indexResumeButton!.IsEnabled = ready && _indexCache?.LastAttempt is { Status: not "Complete" };
        _indexResumeButton.IsVisible = _indexCache?.LastAttempt is { Status: not "Complete" };
        _indexCancelButton!.IsVisible = _indexScanning || _indexChecking;
        _indexProgressPanel!.IsVisible = _indexScanning || _indexChecking;
        if (_indexCheckButton is not null) { _indexCheckButton.IsEnabled = ready && !_connectionLibrarySetupRequired && _indexCache?.Committed is not null; }
        if (_indexMatchThreshold is not null) { _indexMatchThreshold.IsEnabled = !_indexScanning && !_indexChecking && !_changingProfile; }
        RefreshLibraryMatchStatus();
        RefreshIndexConnectionSyncCompletion();
        UpdateLibraryManagementButtons();
        if (_indexDeviceLabel is not null) { _indexDeviceLabel.IsVisible = _currentPage is AppPage.PresetIndex or AppPage.Amps; }
        if (_headerStatusLabel is not null) { _headerStatusLabel.IsVisible = _currentPage is not (AppPage.PresetIndex or AppPage.Amps); }
        if (_indexError is not null) { SetIndexMessage(_indexError + " The original data has been preserved."); }
    }

    internal async Task SyncIndexAsync(bool resume = false)
    {
        var cache = _indexCache is null ? null : IndexJson.Clone(_indexCache);
        if (cache is null || _indexSyncButton?.IsEnabled != true) { return; }
        int generation = _profileGeneration;
        long connection = _connectionGeneration;
        string? deviceName = _detectedDevice!.DeviceName;
        // Every new read uses this connection's reported firmware, including null
        // on failure. Never carry a previous connection's version into a new scan.
        cache.Device = cache.Device with { Firmware = _detectedDevice.Firmware };
        int threshold = _indexProfile.PresetMatchThresholdPercent;
        var baseline = cache.Committed;
        using var cancellation = new CancellationTokenSource();
        _presetNamesCts = cancellation;
        _indexScanning = true;
        _indexProgressMessage = "Preparing sync… Completed reads are saved as it runs.";
        _indexProgressText!.Text = _indexProgressMessage;
        _indexProgress!.Value = 0;
        _indexProgress.Maximum = FractalDeviceDefinition.For(cache.Device.Variant).PresetSlots;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel();
        UpdatePresetSyncButtons();
        RenderIndexResults();
        string outcome = "";
        bool libraryUpdated = false;
        bool profileNamesUpdated = false;
        try
        {
            if (baseline is null && !_connectionLibrarySetupRequired && !await ConfirmProfileAsync("Establish device library",
                $"Use the connected {_detectedDevice.ModelLabel} as the baseline for “{cache.Device.Name}”? The saved-preset read can take several minutes. Cancel keeps completed reads.", "Sync device"))
            { outcome = "Sync cancelled. The library was not changed."; return; }
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _profileGeneration || connection != _connectionGeneration) { return; }
            // A partial scan may belong to an earlier connection. Check it before reusing reads.
            if (resume && cache.LastAttempt is { Status: not "Complete" } partial)
            {
                _indexProgressText!.Text = "Checking saved reads before resuming…";
                var check = await LibraryMatch.CheckSampleAsync(_fractalIndexReader, cache, partial, deviceName, cancellation.Token);
                if (!check.MeetsThreshold(threshold))
                {
                    resume = false;
                    _indexProgressText.Text = "Previous reads could not be confirmed. Starting a fresh scan…";
                }
            }
            await new IndexScanner(_fractalIndexReader, _connectionLibraryStaging ?? _indexLibrary).ScanAsync(cache, resume, progress => Dispatcher.UIThread.Post(() =>
            {
                if (!_indexScanning) { return; }
                int checkedSlots = Math.Min(progress.Total, progress.Read + progress.Failed);
                _indexProgress!.Maximum = progress.Total;
                _indexProgress.Value = checkedSlots;
                _indexProgressMessage = $"{checkedSlots * 100 / progress.Total}% · {checkedSlots} of {progress.Total} slots checked · {progress.Read - progress.EmptySkipped} presets read · {progress.EmptySkipped} empty skipped · {progress.Failed} failed";
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
                if ((!match.MeetsThreshold(threshold) || cache.Imported) && !await ConfirmLibraryUpdateAsync(cache, match))
                { outcome = "Previous library kept. New reads were saved separately; they have not replaced its baseline."; return; }
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
            _showConnectionSyncChecks = false;
            RememberLibraryMatch(cache, match, "Confirmed sync established this library's baseline. Choose Quick match or Full match on future connections to compare it.");
            outcome = $"Presets, scenes and amps refreshed in “{cache.Device.Name}”. This profile’s preset and scene names were also refreshed automatically. {candidate.Presets.Values.Count(p => p.NameOnlyEmpty)} empty slots skipped.";
        }
        catch (OperationCanceledException)
        {
            outcome = cache.LastAttempt?.Status == "Complete"
                ? "Library update cancelled. Completed reads were saved separately."
                : "Sync cancelled. Completed reads were saved; Resume continues this scan.";
        }
        catch (Exception ex) { outcome = "Sync stopped: " + ex.Message; }
        finally
        {
            _indexScanning = false;
            _presetNamesCts = null;
            RefreshIndexContext();
            SetIndexMessage(outcome);
            SetConnectionSyncOutcome(libraryUpdated && profileNamesUpdated ? "Device library synced" : "Library sync needs attention",
                outcome,
                libraryUpdated && profileNamesUpdated ? "Choose Use saved library to continue. Preset Index, Amps and Favorites now use the refreshed data."
                    : cache.LastAttempt is { Status: not "Complete" } ? "Next: Choose Resume to retry missing presets, or Done to use the data already saved."
                        : "Choose Sync library to retry or review an update, or Done to continue with saved data.", needsAttention: !libraryUpdated || !profileNamesUpdated);
            UpdatePresetSyncButtons();
            CompleteInitialConnectionSync();
            if (!_connectionLibrarySetupRequired && _detectedDevice is not null) { StartSceneTracking(); }
        }
    }

    private void CacheLibraryNames(IndexScan scan)
    {
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
