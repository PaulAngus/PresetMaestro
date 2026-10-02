#if FRACTAL_INDEX
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
    private IndexProfile _indexProfile = new();
    private DeviceIndex? _indexCache;
    private string? _indexError;
    private bool _refreshingIndex, _indexScanning;
    private ComboBox? _indexAvailableDevices;
    private TextBlock? _indexDeviceLabel, _indexAssignmentLabel;
    private TextBox? _indexDeviceName, _indexFirmware;
    private ComboBox? _indexVariant;
    private TextBlock? _indexConfigStatus;

    private void InitializeFractalIndex()
    {
        _fractalIndexReader = new(_presetNameClient, AmpModelCatalogRegistry.CreateStarter());
        _indexLibrary = new(Path.Combine(_profileStore?.DirectoryPath ?? Path.GetTempPath(), "FractalIndex"));
        LoadIndexContext();
    }

    private void LoadIndexContext()
    {
        try
        {
            _indexProfile = IndexJson.ReadProfile(_settings.FractalIndex);
            if (_profileStore is not null)
            {
                foreach (var snapshot in _indexProfile.PortableSnapshots)
                {
                    if (_indexLibrary.Load(snapshot.Device.Id) is null) { _indexLibrary.Save(snapshot); }
                }
            }
            _indexCache = _indexProfile.SelectedDeviceId is Guid id
                ? _indexLibrary.Load(id) ?? _indexProfile.PortableSnapshots.FirstOrDefault(c => c.Device.Id == id)
                    ?? new DeviceIndex { Device = _indexProfile.Devices.Single(d => d.Id == id), Imported = true } : null;
            _indexError = null;
        }
        catch (Exception ex) { _indexError = ex.Message; _indexCache = null; _indexProfile = new(); }
    }

    partial void RefreshIndexContext()
    {
        if (_indexLibrary is null) { return; }
        LoadIndexContext();
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
        RenderIndexResults();
        UpdateIndexButtons();
    }

    private bool SaveIndexProfile(Action<IndexProfile> change)
    {
        if (_indexError is not null || _profileStore is null) { return false; }
        var priorJson = _settings.FractalIndex;
        try
        {
            var updated = IndexJson.Clone(_indexProfile);
            change(updated);
            _settings.FractalIndex = IndexJson.ToElement(updated);
            _saveSettings(_settings);
            _indexProfile = updated;
            return true;
        }
        catch (Exception ex)
        {
            _settings.FractalIndex = priorJson;
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
        var card = ApprovedCard("Device library for this profile", "One library per profile. Several profiles can share its saved device data.", compact: true);
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
        _indexMatchThreshold = new NumericUpDown
        {
            Name = "IndexMatchThreshold",
            Minimum = 1,
            Maximum = 100,
            Increment = 1,
            FormatString = "0",
            Value = _indexProfile.PresetMatchThresholdPercent,
            Width = 100
        };
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
        SetApprovedCardContent(card, content);
        panel.Children.Add(card);
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
            _indexCache is not null && _settings.DeviceModel == _indexCache.Device.Variant.ToDeviceModel() && _detectedDevice?.Model == _indexCache.Device.Variant.ToDeviceModel() && _midi.InputOpen && _midi.OutputOpen;
        _indexSyncButton.IsEnabled = ready;
        _indexResumeButton!.IsEnabled = ready && _indexCache?.LastAttempt is { Status: not "Complete" };
        _indexResumeButton.IsVisible = _indexCache?.LastAttempt is { Status: not "Complete" };
        _indexCancelButton!.IsVisible = _indexScanning || _indexChecking;
        _indexProgressPanel!.IsVisible = _indexScanning;
        if (_indexCheckButton is not null) { _indexCheckButton.IsEnabled = ready && _indexCache?.Committed is not null; }
        if (_indexMatchThreshold is not null) { _indexMatchThreshold.IsEnabled = !_indexScanning && !_indexChecking && !_changingProfile; }
        RefreshLibraryMatchStatus();
        UpdateLibraryManagementButtons();
        if (_indexDeviceLabel is not null) { _indexDeviceLabel.IsVisible = _currentPage == AppPage.PresetIndex; }
        if (_headerStatusLabel is not null) { _headerStatusLabel.IsVisible = _currentPage != AppPage.PresetIndex; }
        if (_indexError is not null) { SetIndexMessage(_indexError + " The original data has been preserved."); }
    }

    internal async Task SyncIndexAsync(bool resume = false)
    {
        var cache = _indexCache is null ? null : IndexJson.Clone(_indexCache);
        if (cache is null || _indexSyncButton?.IsEnabled != true) { return; }
        int generation = _profileGeneration;
        long connection = _connectionGeneration;
        string? deviceName = _detectedDevice!.DeviceName;
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
        try
        {
            if (baseline is null && !await ConfirmProfileAsync("Establish device library",
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
            await new IndexScanner(_fractalIndexReader, _indexLibrary).ScanAsync(cache, resume, progress => Dispatcher.UIThread.Post(() =>
            {
                if (!_indexScanning) { return; }
                int checkedSlots = Math.Min(progress.Total, progress.Read + progress.Failed);
                _indexProgress!.Maximum = progress.Total;
                _indexProgress.Value = checkedSlots;
                _indexProgressMessage = $"{checkedSlots * 100 / progress.Total}% · {checkedSlots} of {progress.Total} slots checked · {progress.Read - progress.EmptySkipped} presets read · {progress.EmptySkipped} empty skipped · {progress.Failed} failed";
                _indexProgressText!.Text = _indexProgressMessage;
            }), cancellation.Token, publish: false, connectedDeviceName: deviceName);
            if (cache.LastAttempt!.Status != "Complete")
            { outcome = "Partial scan saved. Resume to retry missing presets."; return; }
            var candidate = cache.LastAttempt;
            LibraryMatchResult? match = baseline is null ? null : LibraryMatch.Compare(baseline, candidate.Presets,
                false, deviceName, candidate.Firmware);
            if (match is not null)
            {
                RememberLibraryMatch(cache, match);
                if ((!match.MeetsThreshold(threshold) || cache.Imported) && !await ConfirmProfileAsync("Confirm library update",
                    match.Describe(threshold) + $"\n\nUse these reads to update “{cache.Device.Name}”? Preset edits, moves or firmware changes can reduce the match. Identical backups can also match on different units. Cancel keeps the previous library.", "Update library"))
                { outcome = "Previous library kept. New reads were saved separately; they have not replaced its baseline."; return; }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _profileGeneration || connection != _connectionGeneration) { return; }
            cache.Committed = IndexJson.Clone(candidate);
            cache.Imported = false;
            _indexLibrary.Save(cache);
            RememberLibraryMatch(cache, match, "Confirmed sync established this library's baseline. Future connections will compare a sample against it.");
            outcome = $"Device index synchronized. {candidate.Presets.Values.Count(p => p.NameOnlyEmpty)} empty slots skipped.";
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
            UpdatePresetSyncButtons();
        }
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
