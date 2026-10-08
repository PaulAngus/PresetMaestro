#if FRACTAL_INDEX
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private bool _connectionLibrarySetupRequired, _connectionLibraryChoiceBusy;
    private IndexDevice? _connectionDefaultLibrary;
    private FractalDeviceVariant? _connectionLibraryVariant;
    private DeviceIndex? _connectionLibraryCandidate, _connectionLibraryOriginal;
    private IndexLibrary? _connectionLibraryStaging;
    private StackPanel? _connectionLibrarySetupPanel, _connectionLibraryTargetChoices;
    private TextBox? _connectionLibraryName;
    private Button? _connectionLibraryCreate, _connectionLibraryOverwrite;
    private TextBlock? _connectionLibrarySetupMessage;
    private string? _connectionLibrarySetupError;

    private void ResetConnectionLibrarySetup()
    {
        _connectionLibrarySetupRequired = false;
        _connectionLibraryChoiceBusy = false;
        _connectionDefaultLibrary = null;
        _connectionLibraryVariant = null;
        _connectionLibraryCandidate = _connectionLibraryOriginal = null;
        _connectionLibraryStaging = null;
        _connectionLibrarySetupError = null;
    }

    private void PrepareConnectionLibrarySetup()
    {
        // Transient windows without a profile store cannot establish a saved library.
        if (_profileStore is null || _detectedDevice is null) { return; }
        if (_indexError is null && _indexCache is { Imported: false, Committed: { Status: "Complete", FinishedAt: not null } } known &&
            known.Device.Variant.ToDeviceModel() == _detectedDevice.Model && known.Committed.Errors.Count == 0 &&
            known.Committed.Presets.Count == FractalDeviceDefinition.For(known.Device).PresetSlots) { return; }

        _connectionLibrarySetupRequired = true;
        try
        {
            if (_indexError is not null) { throw new InvalidDataException(_indexError); }
            var variants = LibraryVariants().Where(v => v.Variant is (FractalDeviceVariant.FM9 or FractalDeviceVariant.FM3 or FractalDeviceVariant.AxeFxIII))
                .Where(v => v.Variant.ToDeviceModel() == _detectedDevice.Model).ToArray();
            _connectionLibraryVariant = _indexCache?.Device.Variant.ToDeviceModel() == _detectedDevice.Model
                ? _indexCache.Device.Variant : variants.Length == 1 ? variants[0].Variant : null;
            // A scan already saved to this profile's assigned library has a destination.
            // Imported baselines still need review before publication, but that must not
            // prevent resuming the new reads. Sync verifies them against this connection.
            if (_indexCache is { } assigned && assigned.Device.Variant.ToDeviceModel() == _detectedDevice.Model &&
                (assigned.LastAttempt is not null || assigned.Committed is not null))
            {
                _connectionDefaultLibrary = null;
                return;
            }
            _connectionDefaultLibrary = AvailableIndexDevices().FirstOrDefault(d => string.Equals(d.Name.Trim(), "Default", StringComparison.OrdinalIgnoreCase));
            PrepareUnusedDefaultLibrary();
        }
        catch (Exception ex) { _connectionLibrarySetupError = ex.Message; }
    }

    private void PrepareUnusedDefaultLibrary()
    {
        if (_connectionLibraryVariant is not { } variant) { return; }
        if (_connectionDefaultLibrary is { } existing)
        {
            var saved = _indexLibrary.Load(existing.Id);
            // A partial scan is data too. Never treat it as a disposable placeholder.
            if (saved is null || saved.Imported || saved.Committed is not null || saved.LastAttempt is not null ||
                existing.Variant != variant || LibraryAssignedProfiles(existing.Id).Any(p => !string.Equals(p, _settings.ActiveProfile, StringComparison.OrdinalIgnoreCase))) { return; }
        }
        var device = new IndexDevice(_connectionDefaultLibrary?.Id ?? Guid.NewGuid(), "Default", variant, _detectedDevice!.Firmware);
        var cache = _indexLibrary.SaveDevice(device, _connectionDefaultLibrary is null ? CurrentConnectionMapping() : null);
        if (!SaveIndexProfile(profile => profile.AssignDevice(cache.Device))) { throw new IOException("Could not assign the Default device. Check the profile's storage access."); }
        _connectionDefaultLibrary = null;
        RefreshIndexContext();
    }

    private DevicePresetMapping CurrentConnectionMapping() => new(_settings.MidiChannel, _settings.DisplayOffset, _settings.SceneCc);

    private Control BuildConnectionLibrarySetup()
    {
        _connectionLibrarySetupPanel = new StackPanel { Name = "ConnectionLibrarySetup", Spacing = 10 };
        _connectionLibrarySetupMessage = new TextBlock { Name = "ConnectionLibrarySetupMessage", TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, FontSize = 14 };
        _connectionLibrarySetupPanel.Children.Add(_connectionLibrarySetupMessage);
        _connectionLibraryTargetChoices = new StackPanel { Spacing = 8 };
        string suggestion = string.IsNullOrWhiteSpace(_detectedDevice!.DeviceName) ? _detectedDevice.ModelLabel : _detectedDevice.DeviceName;
        string name = suggestion;
        var devices = AvailableIndexDevices();
        for (int suffix = 2; devices.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++) { name = suggestion + " " + suffix; }
        _connectionLibraryName = new TextBox { Name = "ConnectionLibraryName", Text = name, Watermark = "New device name" };
        AutomationProperties.SetName(_connectionLibraryName, "New device name");
        _connectionLibraryTargetChoices.Children.Add(new TextBlock { Text = "Device name", Foreground = TextBrush });
        _connectionLibraryTargetChoices.Children.Add(_connectionLibraryName);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        _connectionLibraryCreate = IndexButton("Add new device & sync", "ConnectionLibraryCreate");
        _connectionLibraryCreate.Background = AccentBrush;
        _connectionLibraryCreate.Foreground = Brushes.White;
        _connectionLibraryCreate.MinHeight = 36; _connectionLibraryCreate.FontSize = 14;
        _connectionLibraryCreate.Margin = new(0, 0, 8, 0);
        _connectionLibraryCreate.Click += async (_, _) => await ChooseConnectionLibraryAsync(overwrite: false);
        _connectionLibraryOverwrite = IndexButton("Overwrite Default & sync", "ConnectionLibraryOverwrite");
        _connectionLibraryOverwrite.MinHeight = 36; _connectionLibraryOverwrite.FontSize = 14;
        _connectionLibraryOverwrite.Click += async (_, _) => await ChooseConnectionLibraryAsync(overwrite: true);
        actions.Children.Add(_connectionLibraryCreate); actions.Children.Add(_connectionLibraryOverwrite);
        _connectionLibraryTargetChoices.Children.Add(actions);
        _connectionLibrarySetupPanel.Children.Add(_connectionLibraryTargetChoices);
        return _connectionLibrarySetupPanel;
    }

    private void RefreshConnectionLibrarySetup(bool busy)
    {
        if (_connectionLibrarySetupPanel is null) { return; }
        _connectionLibrarySetupPanel.IsVisible = _connectionLibrarySetupRequired && !busy;
        if (!_connectionLibrarySetupRequired) { return; }
        bool chooseTarget = _connectionDefaultLibrary is not null && _connectionLibraryCandidate is null;
        _connectionLibraryTargetChoices!.IsVisible = chooseTarget;
        _connectionLibraryCreate!.IsEnabled = !busy && _connectionLibraryVariant is not null && _indexError is null;
        _connectionLibraryOverwrite!.IsEnabled = _connectionLibraryCreate.IsEnabled;
        _connectionLibraryName!.IsEnabled = !busy;
        _connectionLibrarySetupMessage!.Text = _connectionLibrarySetupError is { } error ? "Could not set up the device: " + error :
            chooseTarget ? "Add the connected device under a new name, or choose Overwrite Default & sync to replace the saved data for Default. Its existing data is kept until the sync finishes." :
            _connectionLibraryVariant is null ? "Connect a supported device to save its presets, scenes and amps." : "";
        _connectionLibrarySetupMessage.IsVisible = !string.IsNullOrWhiteSpace(_connectionLibrarySetupMessage.Text);
    }

    private async Task ChooseConnectionLibraryAsync(bool overwrite)
    {
        if (!_connectionLibrarySetupRequired || _connectionLibraryChoiceBusy || _presetNamesCts is not null ||
            _connectionLibraryVariant is not { } variant || _detectedDevice is null) { return; }
        long connection = _connectionGeneration;
        int profile = _profileGeneration;
        _connectionLibraryChoiceBusy = true;
        RefreshConnectionSyncOptions();
        bool selected = false;
        try
        {
            if (overwrite)
            {
                var original = _indexLibrary.Load(_connectionDefaultLibrary!.Id) ?? throw new IOException("The Default device is no longer available.");
                string[] assigned = LibraryAssignedProfiles(original.Device.Id);
                if (!await ConfirmProfileAsync("Overwrite Default device",
                    $"Replace the saved presets, scenes and amps in “{LibraryDisplayName(original.Device)}” with the connected {_detectedDevice.ModelLabel}?" +
                    (assigned.Length == 0 ? "" : "\n\nProfiles using this device: " + string.Join(", ", assigned) + ".") +
                    "\n\nExisting data is kept until a complete sync succeeds. Existing tags are retained for review when their preset changes. Presets on the device are not changed.", "Overwrite & sync")) { return; }
                if (connection != _connectionGeneration || profile != _profileGeneration || _detectedDevice is null) { return; }
                var candidate = new DeviceIndex
                {
                    Device = original.Device with { Variant = variant, Firmware = _detectedDevice.Firmware },
                    PresetMapping = original.PresetMapping ?? CurrentConnectionMapping(),
                };
                var staging = new IndexLibrary(Path.Combine(_indexLibrary.DirectoryPath, "ConnectionSetup"));
                // Checkpoints for an overwrite must never replace the occupied library.
                staging.Save(candidate);
                _connectionLibraryOriginal = original;
                _connectionLibraryCandidate = candidate;
                _connectionLibraryStaging = staging;
            }
            else
            {
                var device = new IndexDevice(Guid.NewGuid(), _connectionLibraryName!.Text ?? "", variant, _detectedDevice.Firmware);
                var cache = _indexLibrary.SaveDevice(device, CurrentConnectionMapping());
                if (!SaveIndexProfile(p => p.AssignDevice(cache.Device))) { throw new IOException("Could not assign the new device."); }
                _connectionDefaultLibrary = null;
            }
            _connectionLibrarySetupError = null;
            RefreshIndexContext();
            _initialConnectionSyncContext = ConnectionSyncContext;
            selected = true;
        }
        catch (Exception ex) { _connectionLibrarySetupError = ex.Message; }
        finally { _connectionLibraryChoiceBusy = false; UpdatePresetSyncButtons(); }
        if (selected) { await SyncIndexAsync(); }
    }

    private void PublishConnectionLibrary(DeviceIndex cache)
    {
        if (_connectionLibraryStaging is null) { _indexLibrary.Save(cache); LoadIndexContext(); return; }
        _settings.FractalIndex = _profileStore!.PublishConnectionLibrary(cache, _settings.ActiveProfile, _settings.FractalIndex, _connectionLibraryOriginal!);
        _connectionLibraryCandidate = null;
        var staging = _connectionLibraryStaging;
        _connectionLibraryStaging = null;
        try { staging.Delete(cache.Device.Id); }
        catch (Exception ex) { AppendLog("DEVICE SETUP: could not remove completed staging data: " + ex.Message); }
        LoadIndexContext();
        _initialConnectionSyncContext = ConnectionSyncContext;
    }
}
#endif
