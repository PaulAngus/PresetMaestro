#if FRACTAL_INDEX
using Avalonia.Controls;
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
    private ComboBox? _connectionLibraryVariantChoice;
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
            known.Committed.Presets.Count == FractalDeviceDefinition.For(known.Device.Variant).PresetSlots) { return; }

        _connectionLibrarySetupRequired = true;
        try
        {
            if (_indexError is not null) { throw new InvalidDataException(_indexError); }
            var variants = LibraryVariants().Where(v => v.Variant.ToDeviceModel() == _detectedDevice.Model).ToArray();
            _connectionLibraryVariant = _indexCache?.Device.Variant.ToDeviceModel() == _detectedDevice.Model
                ? _indexCache.Device.Variant : variants.Length == 1 ? variants[0].Variant : null;
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
        if (!SaveIndexProfile(profile => profile.AssignDevice(cache.Device))) { throw new IOException("Could not assign the Default library. Check the profile's storage access."); }
        _connectionDefaultLibrary = null;
        RefreshIndexContext();
    }

    private DevicePresetMapping CurrentConnectionMapping() => new(_settings.MidiChannel, _settings.DisplayOffset, _settings.SceneCc);

    private Control BuildConnectionLibrarySetup()
    {
        _connectionLibrarySetupPanel = new StackPanel { Name = "ConnectionLibrarySetup", Spacing = 10 };
        _connectionLibrarySetupMessage = new TextBlock { Name = "ConnectionLibrarySetupMessage", TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, FontSize = 14 };
        _connectionLibrarySetupPanel.Children.Add(_connectionLibrarySetupMessage);
        var variants = LibraryVariants().Where(v => v.Variant.ToDeviceModel() == _detectedDevice!.Model).ToArray();
        _connectionLibraryVariantChoice = new ComboBox
        {
            Name = "ConnectionLibraryVariant",
            ItemsSource = variants,
            PlaceholderText = "Choose your Axe-Fx III hardware variant",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = variants.FirstOrDefault(v => v.Variant == _connectionLibraryVariant),
        };
        _connectionLibraryVariantChoice.SelectionChanged += (_, _) =>
        {
            if (_connectionLibraryVariantChoice.SelectedItem is not IndexVariantChoice choice) { return; }
            _connectionLibraryVariant = choice.Variant;
            try { PrepareUnusedDefaultLibrary(); }
            catch (Exception ex) { _connectionLibrarySetupError = ex.Message; }
            _initialConnectionSyncContext = ConnectionSyncContext;
            RefreshConnectionSyncOptions();
        };
        _connectionLibrarySetupPanel.Children.Add(_connectionLibraryVariantChoice);
        _connectionLibraryTargetChoices = new StackPanel { Spacing = 8 };
        string suggestion = string.IsNullOrWhiteSpace(_detectedDevice!.DeviceName) ? _detectedDevice.ModelLabel : _detectedDevice.DeviceName;
        string name = suggestion;
        var devices = AvailableIndexDevices();
        for (int suffix = 2; devices.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++) { name = suggestion + " " + suffix; }
        _connectionLibraryName = new TextBox { Name = "ConnectionLibraryName", Text = name, Watermark = "New library name" };
        _connectionLibraryTargetChoices.Children.Add(_connectionLibraryName);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        _connectionLibraryCreate = IndexButton("Create new library & sync", "ConnectionLibraryCreate");
        _connectionLibraryCreate.Margin = new(0, 0, 8, 0);
        _connectionLibraryCreate.Click += async (_, _) => await ChooseConnectionLibraryAsync(overwrite: false);
        _connectionLibraryOverwrite = IndexButton("Overwrite Default & sync", "ConnectionLibraryOverwrite");
        _connectionLibraryOverwrite.Click += async (_, _) => await ChooseConnectionLibraryAsync(overwrite: true);
        actions.Children.Add(_connectionLibraryCreate); actions.Children.Add(_connectionLibraryOverwrite);
        _connectionLibraryTargetChoices.Children.Add(actions);
        _connectionLibrarySetupPanel.Children.Add(_connectionLibraryTargetChoices);
        return _connectionLibrarySetupPanel;
    }

    private void RefreshConnectionLibrarySetup(bool busy)
    {
        if (_connectionLibrarySetupPanel is null) { return; }
        _connectionLibrarySetupPanel.IsVisible = _connectionLibrarySetupRequired;
        if (!_connectionLibrarySetupRequired) { return; }
        bool chooseTarget = _connectionDefaultLibrary is not null && _connectionLibraryCandidate is null;
        _connectionLibraryTargetChoices!.IsVisible = chooseTarget;
        _connectionLibraryVariantChoice!.IsVisible = _connectionLibraryVariant is null;
        _connectionLibraryVariantChoice.IsEnabled = !busy;
        _connectionLibraryCreate!.IsEnabled = !busy && _connectionLibraryVariant is not null && _indexError is null;
        _connectionLibraryOverwrite!.IsEnabled = _connectionLibraryCreate.IsEnabled;
        _connectionLibraryName!.IsEnabled = !busy;
        _connectionLibrarySetupMessage!.Text = _connectionLibrarySetupError is { } error ? "Library setup needs attention: " + error :
            chooseTarget ? "Default is already in use. Create a new library, or explicitly overwrite Default after a complete sync. Its existing data is kept until the new sync succeeds." :
            _connectionLibraryVariant is null ? "Choose the hardware variant so the full sync reads the correct number of preset slots." :
            $"A complete sync to “{_indexCache?.Device.Name ?? "Default"}” is required before continuing. Preset names alone cannot populate Preset Index and Amps.";
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
                var original = _indexLibrary.Load(_connectionDefaultLibrary!.Id) ?? throw new IOException("The Default library is no longer available.");
                string[] assigned = LibraryAssignedProfiles(original.Device.Id);
                if (!await ConfirmProfileAsync("Overwrite Default library",
                    $"Replace the saved presets, scenes and amps in “{LibraryDisplayName(original.Device)}” with the connected {_detectedDevice.ModelLabel}?" +
                    (assigned.Length == 0 ? "" : "\n\nProfiles using this library: " + string.Join(", ", assigned) + ".") +
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
                if (!SaveIndexProfile(p => p.AssignDevice(cache.Device))) { throw new IOException("Could not assign the new library."); }
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
        if (_connectionLibraryStaging is null) { _indexLibrary.Save(cache); return; }
        _settings.FractalIndex = _profileStore!.PublishConnectionLibrary(cache, _settings.ActiveProfile, _settings.FractalIndex, _connectionLibraryOriginal!);
        _connectionLibraryCandidate = null;
        var staging = _connectionLibraryStaging;
        _connectionLibraryStaging = null;
        try { staging.Delete(cache.Device.Id); }
        catch (Exception ex) { AppendLog("LIBRARY SETUP: could not remove completed staging data: " + ex.Message); }
        LoadIndexContext();
        _initialConnectionSyncContext = ConnectionSyncContext;
    }
}
#endif
