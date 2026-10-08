#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private Action? _showManageLibraries;
    private ListBox? _managedLibraries;
    private Guid? _managedLibraryId;
    private bool _creatingLibrary, _refreshingLibraries;
    private TextBlock? _libraryManagementStatus, _libraryHeading, _libraryFacts, _libraryCount, _libraryModelLabel;
    private Button? _libraryCreate, _librarySave, _libraryRename, _libraryUse, _libraryDelete, _libraryCancel;
    private Dictionary<Guid, string> _libraryDisplayNames = [];
    private ComboBox? _libraryChannel, _libraryOffset;
    private NumericUpDown? _librarySceneCc;
    private Control? _libraryMapping, _libraryNameField;

    private bool LibraryManagementBusy => _presetNamesCts is not null || _connectionCts is not null || _changingProfile || _indexScanning || _indexChecking || _indexError is not null;

    private IndexDevice[] AvailableIndexDevices()
    {
        // Profile references can outlive a saved library. Only saved files are assignable.
        var devices = _indexLibrary.ListDevices().ToArray();
        CacheLibraryDisplayNames(devices);
        return devices;
    }

    private void CacheLibraryDisplayNames(IEnumerable<IndexDevice> devices)
    {
        var assignments = new List<(string Name, Guid? Id)>();
        foreach (string name in _profileStore?.ListProfiles() ?? [])
        {
            try { assignments.Add((name, IndexJson.ReadProfile(ReadManagedProfile(name).FractalIndex).SelectedDeviceId)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
            { /* Display context from an unreadable profile must not hide saved libraries. */ }
        }
        _libraryDisplayNames = [];
        foreach (var group in devices.DistinctBy(d => d.Id).GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in group.OrderBy(d => d.Id))
            {
                string name = device.Name;
                if (group.Count() > 1)
                {
                    var profiles = assignments.Where(p => p.Id == device.Id).Select(p => p.Name).ToArray();
                    string context = profiles.Length > 0 ? $"Used by {profiles[0]}" + (profiles.Length > 1 ? $" +{profiles.Length - 1}" : "")
                        : _indexLibrary.LoadSummary(device.Id)?.Imported == true ? "Imported copy" : "Unassigned copy";
                    string basis = $"{name} · {device.Variant.ToDeviceModel()} · {context}";
                    name = basis;
                    for (int copy = 2; usedLabels.Contains(name); copy++) { name = $"{basis} ({copy})"; }
                }
                usedLabels.Add(name);
                _libraryDisplayNames.Add(device.Id, name);
            }
        }
    }

    private string LibraryDisplayName(IndexDevice device) => _libraryDisplayNames.GetValueOrDefault(device.Id, device.Name);

    private static IndexVariantChoice[] LibraryVariants() =>
    [
        new(FractalDeviceVariant.FM9, "FM9"), new(FractalDeviceVariant.FM3, "FM3"),
        new(FractalDeviceVariant.AxeFxIII, "Axe-Fx III"),
        new(FractalDeviceVariant.AxeFxIIIOriginal, "Axe-Fx III Original · 512 slots"),
        new(FractalDeviceVariant.AxeFxIIIMarkII, "Axe-Fx III Mark II · 1024 slots"),
        new(FractalDeviceVariant.AxeFxIIIMarkIITurbo, "Axe-Fx III Mark II Turbo · 1024 slots"),
    ];

    partial void InitializeLibraryManagement(ContentControl host, Action showConfig)
    {
        var page = BuildManageLibrariesPage(showConfig);
        _showManageLibraries = () =>
        {
            _creatingLibrary = false;
            _managedLibraryId = _indexProfile.SelectedDeviceId;
            RefreshManagedLibraries();
            _currentPage = AppPage.ManageLibraries;
            host.Content = page;
            UpdateIndexButtons();
        };
        if (_currentPage == AppPage.ManageLibraries) { host.Content = page; }
    }

    private Control BuildManageLibrariesPage(Action showConfig)
    {
        var page = new Grid { Name = "LibraryManagementLayout", Margin = new Thickness(20, 12, 20, 20), RowDefinitions = new RowDefinitions("Auto,8,*") };
        var back = ProfileCommand("LibrariesBackToConfig", "← Back to Config");
        back.Background = Brushes.Transparent; back.BorderThickness = new Thickness(0);
        back.Click += (_, _) => { _creatingLibrary = false; RefreshIndexContext(); showConfig(); };
        page.Children.Add(back);
        var content = new StackPanel { Spacing = 12, IsEnabled = _profileStore is not null };
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolbar.Children.Add(new TextBlock { Text = "Manage devices", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center });
        _libraryCreate = ProfileCommand("IndexCreateDevice", "+ Add device…");
        _libraryCreate.Foreground = AccentBrush;
        _libraryCreate.Click += (_, _) =>
        {
            if (LibraryManagementBusy) { return; }
            _creatingLibrary = true;
            try { RefreshManagedLibraryDetails(); }
            catch (Exception ex) { SetIndexMessage("Could not read device details: " + ex.Message); }
            _indexDeviceName!.Focus();
        };
        Grid.SetColumn(_libraryCreate, 1); toolbar.Children.Add(_libraryCreate); content.Children.Add(toolbar);
        var split = new Grid { Name = "LibraryManagementSplit", ColumnDefinitions = new ColumnDefinitions("320,12,*") };
        var left = new StackPanel { Spacing = 6 };
        _managedLibraries = new ListBox
        {
            Name = "ManagedLibraries",
            ItemContainerTheme = CompactListItemTheme(),
            MaxHeight = 330,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        AutomationProperties.SetName(_managedLibraries, "Devices");
        ScrollViewer.SetHorizontalScrollBarVisibility(_managedLibraries, ScrollBarVisibility.Disabled);
        _managedLibraries.SelectionChanged += (_, _) =>
        {
            if (_refreshingLibraries || _managedLibraries.SelectedItem is not ListBoxItem { Tag: Guid id }) { return; }
            _managedLibraryId = id; _creatingLibrary = false;
            try { RefreshManagedLibraryDetails(); }
            catch (Exception ex) { SetIndexMessage("Could not read device details: " + ex.Message); }
        };
        left.Children.Add(ProfilePane(_managedLibraries));
        _libraryCount = new TextBlock { FontSize = 11, Foreground = SecondaryBrush };
        left.Children.Add(_libraryCount);
        split.Children.Add(left);
        var details = new StackPanel { Spacing = 10 };
        _libraryHeading = new TextBlock { Name = "ManagedLibrary", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        details.Children.Add(_libraryHeading);
        _libraryFacts = new TextBlock { Name = "LibraryFacts", FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        details.Children.Add(_libraryFacts);
        _indexDeviceName = new TextBox { Name = "IndexDeviceName", Watermark = "e.g. Stage FM9", MaxLength = 80 };
        _indexFirmware = new TextBlock { Name = "IndexFirmware", FontSize = 14, Foreground = TextBrush };
        _indexVariant = new ComboBox { Name = "IndexDeviceVariant", ItemsSource = LibraryVariants(), HorizontalAlignment = HorizontalAlignment.Stretch };
        _libraryNameField = CompactField("Device name", _indexDeviceName);
        details.Children.Add(_libraryNameField);
        AutomationProperties.SetName(_indexDeviceName, "Device name");
        _libraryModelLabel = new TextBlock { FontSize = 14, Foreground = TextBrush };
        var model = new Grid(); model.Children.Add(_indexVariant); model.Children.Add(_libraryModelLabel);
        details.Children.Add(CompactField("Device model", model));
        details.Children.Add(CompactField("Firmware", _indexFirmware));
        details.Children.Add(new TextBlock
        {
            Text = "The device model and firmware are saved on this PC. Firmware is read from the connected device; each saved scan keeps the version it was read with.",
            FontSize = 12,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        _libraryMapping = BuildLibraryPresetMapping();
        details.Children.Add(_libraryMapping);
        var commands = new WrapPanel { Orientation = Orientation.Horizontal };
        _librarySave = ProfileCommand("IndexUpdateDevice", "Save changes");
        _librarySave.Click += (_, _) => SaveManagedLibrary();
        _libraryRename = ProfileCommand("IndexRenameDevice", "Rename…");
        _libraryRename.Click += async (_, _) => await RenameManagedLibraryAsync();
        _libraryUse = ProfileCommand("IndexLinkDevice", "Use for this profile");
        _libraryUse.Background = AccentBrush; _libraryUse.Foreground = Brushes.White;
        _libraryUse.Click += (_, _) =>
        {
            if (_managedLibraryId is Guid id && _indexLibrary.Load(id) is { } cache) { AssignIndexLibrary(cache.Device); }
        };
        _libraryDelete = ProfileCommand("IndexDeleteDevice", "Delete…");
        _libraryDelete.Foreground = DangerBrush;
        _libraryDelete.Click += async (_, _) => await DeleteManagedLibraryAsync();
        _libraryCancel = ProfileCommand("IndexCancelCreate", "Cancel");
        _libraryCancel.Click += (_, _) => { _creatingLibrary = false; RefreshManagedLibraryDetails(); };
        foreach (var button in new[] { _librarySave, _libraryRename, _libraryUse, _libraryDelete, _libraryCancel })
        { button.Margin = new Thickness(0, 0, 6, 6); commands.Children.Add(button); }
        details.Children.Add(commands);
        var pane = ProfilePane(details); pane.Padding = new Thickness(12); Grid.SetColumn(pane, 2); split.Children.Add(pane);
        content.Children.Add(split);
        _libraryManagementStatus = new TextBlock { Name = "LibraryManagementStatus", FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(_libraryManagementStatus);
        var card = new Border
        {
            Background = SurfaceBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 11, 12, 17),
            BoxShadow = Elevation,
            Child = content,
            VerticalAlignment = VerticalAlignment.Top
        };
        var scroll = new ScrollViewer { Content = card, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); page.Children.Add(scroll);
        RefreshManagedLibraries();
        return page;
    }

    private Control BuildLibraryPresetMapping()
    {
        var card = ApprovedCard("Preset Mapping", "For this device · shared by all assigned profiles", compact: true);
        card.Name = "LibraryPresetMapping";
        _libraryChannel = new ComboBox { Name = "MidiChannel", HorizontalAlignment = HorizontalAlignment.Stretch };
        _libraryChannel.Items.Add("Omni");
        for (int channel = 1; channel <= 16; channel++) { _libraryChannel.Items.Add(channel.ToString()); }
        _libraryOffset = new ComboBox
        {
            Name = "DisplayOffset",
            ItemsSource = new[] { "0 (device mapping disabled)", "1 (display starts at 001)" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _librarySceneCc = new NumericUpDown { Name = "SceneCc", Minimum = 0, Maximum = 127, Increment = 1, FormatString = "0", Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(_libraryChannel, "Device MIDI channel");
        AutomationProperties.SetName(_libraryOffset, "Device display offset");
        AutomationProperties.SetName(_librarySceneCc, "Device Scene Select CC number");
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("100,12,*,12,100") };
        fields.Children.Add(CompactField("MIDI channel", _libraryChannel));
        var offset = CompactField("Display offset", _libraryOffset);
        Grid.SetColumn(offset, 2); fields.Children.Add(offset);
        var scene = CompactField("Scene CC#", _librarySceneCc);
        Grid.SetColumn(scene, 4); fields.Children.Add(scene);
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(fields);
        stack.Children.Add(new TextBlock
        {
            Text = "Save changes to apply this mapping. Detected preset limits are applied automatically. Program Change mapping must be disabled on the device when Display Offset is 0.",
            FontSize = 12,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        SetApprovedCardContent(card, stack);
        return card;
    }

    private DevicePresetMapping ManagedLibraryMapping(DeviceIndex? cache) =>
        Core.ProfileLibraryCoordinator.ResolveMapping(cache, _indexProfile, _settings, _profileStore?.ListProfiles() ?? [], ReadManagedProfile);
    private string[] LibraryAssignedProfiles(Guid id) => (_profileStore?.ListProfiles() ?? [])
        .Where(name => IndexJson.ReadProfile(ReadManagedProfile(name).FractalIndex).SelectedDeviceId == id).ToArray();

    private void RefreshManagedLibraries()
    {
        if (_managedLibraries is null || _refreshingLibraries) { return; }
        _refreshingLibraries = true;
        try
        {
            var devices = AvailableIndexDevices();
            if (_managedLibraryId is null || devices.All(d => d.Id != _managedLibraryId))
            { _managedLibraryId = devices.FirstOrDefault(d => d.Id == _indexProfile.SelectedDeviceId)?.Id ?? (devices.Length > 0 ? devices[0].Id : null); }
            _managedLibraries.Items.Clear();
            foreach (var device in devices)
            {
                var line = new StackPanel { Margin = new Thickness(8, 4), Spacing = 2 };
                string name = LibraryDisplayName(device);
                line.Children.Add(new TextBlock { Text = name, Foreground = TextBrush, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
                string state = device.Id == _indexProfile.SelectedDeviceId ? "Assigned to this profile" : "";
                line.Children.Add(new TextBlock { Text = device.Variant.ToDeviceModel() + (state.Length > 0 ? " · " + state : ""), FontSize = 11, Foreground = SecondaryBrush });
                var row = new Grid(); row.Children.Add(line);
                row.Children.Add(new Border { Name = "LibrarySelectionAccent", Width = 3, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false });
                var item = new ListBoxItem { Tag = device.Id, Content = row, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(item, name); AutomationProperties.SetName(item, name + " " + state);
                _managedLibraries.Items.Add(item);
            }
            _managedLibraries.SelectedItem = _managedLibraries.Items.OfType<ListBoxItem>().FirstOrDefault(i => (Guid)i.Tag! == _managedLibraryId);
            _libraryCount!.Text = devices.Length == 1 ? "1 device" : $"{devices.Length} devices";
            RefreshManagedLibraryDetails();
        }
        catch (Exception ex) { SetIndexMessage("Could not load devices: " + ex.Message); }
        finally { _refreshingLibraries = false; }
    }

    private void RefreshManagedLibraryDetails()
    {
        if (_indexDeviceName is null || _libraryHeading is null) { return; }
        var cache = _managedLibraryId is Guid id ? _indexLibrary.Load(id) : null;
        _libraryHeading.Text = _creatingLibrary ? "Add device" : cache is null ? "No device selected" : LibraryDisplayName(cache.Device);
        _indexDeviceName.Text = _creatingLibrary ? "" : cache?.Device.Name ?? "";
        _libraryNameField!.IsVisible = _creatingLibrary;
        _indexFirmware!.Text = (_creatingLibrary ? _detectedDevice?.Firmware : cache?.Device.Firmware) ?? "Not detected";
        var choices = LibraryVariants().Where(c => !_creatingLibrary ||
            c.Variant is (FractalDeviceVariant.FM9 or FractalDeviceVariant.FM3 or FractalDeviceVariant.AxeFxIII))
            .Where(c => !_creatingLibrary || _detectedDevice is null || c.Variant.ToDeviceModel() == _detectedDevice.Model).ToArray();
        _indexVariant!.ItemsSource = choices;
        _indexVariant.SelectedItem = _creatingLibrary ? choices.First(c => c.Variant.ToDeviceModel() == _settings.DeviceModel) : choices.FirstOrDefault(c => c.Variant == cache?.Device.Variant);
        // Connected families are informational; Axe-Fx capacity is established during sync.
        _indexVariant.IsVisible = _creatingLibrary && choices.Length > 1;
        _libraryModelLabel!.IsVisible = !_indexVariant.IsVisible;
        _libraryModelLabel.Text = (_indexVariant.SelectedItem as IndexVariantChoice)?.Label ?? "—";
        if (_creatingLibrary && _indexVariant.SelectedItem is IndexVariantChoice { Variant: FractalDeviceVariant.AxeFxIII })
        { _libraryModelLabel.Text += " · Capacity detected during sync"; }
        else if (cache?.Device.Variant == FractalDeviceVariant.AxeFxIII)
        { _libraryModelLabel.Text += cache.Device.PresetCapacity is int slots ? $" · {slots} slots" : " · Capacity detected during sync"; }
        var mapping = ManagedLibraryMapping(_creatingLibrary ? null : cache);
        _libraryChannel!.SelectedIndex = mapping.MidiChannel;
        _libraryOffset!.SelectedIndex = mapping.DisplayOffset;
        _librarySceneCc!.Value = mapping.SceneCc;
        foreach (var item in _managedLibraries!.Items.OfType<ListBoxItem>())
        {
            bool selected = !_creatingLibrary && (Guid)item.Tag! == _managedLibraryId;
            var row = (Grid)item.Content!;
            row.Background = selected ? ThemeBrush("SelectedBrush") : Brushes.Transparent;
            row.Children.OfType<Border>().Single().IsVisible = selected;
        }
        if (_creatingLibrary) { _libraryFacts!.Text = "Add a device, then choose Use for this profile to assign it."; }
        else if (cache is not null)
        {
            string profiles;
            try { profiles = string.Join(", ", LibraryAssignedProfiles(cache.Device.Id)); }
            catch (Exception) { profiles = "Unavailable — a profile could not be read"; }
            int count = cache.Browsable?.Presets.Values.Count(LibraryMatch.IsPopulated) ?? 0;
            string scan = cache.Browsable?.FinishedAt?.ToLocalTime().ToString("g") ?? "Not synced";
            _libraryFacts!.Text = $"Used by profiles: {(profiles.Length == 0 ? "None" : profiles)}\n{count} saved presets · Last scan: {scan}\n"
                + (cache.Imported ? "Saved copy from a profile import" : "Saved device");
        }
        else { _libraryFacts!.Text = "Choose + Add device to save another device."; }
        UpdateLibraryManagementButtons();
    }

    private void UpdateLibraryManagementButtons()
    {
        if (_librarySave is null) { return; }
        bool ready = !LibraryManagementBusy && _profileStore is not null;
        IndexLibrarySummary? cache = null;
        try { if (_managedLibraryId is Guid id) { cache = _indexLibrary.LoadSummary(id); } }
        catch (Exception ex) { ready = false; SetIndexMessage("Could not read device: " + ex.Message); }
        _libraryCreate!.IsEnabled = ready && !_creatingLibrary;
        _librarySave.Content = _creatingLibrary ? "Add device" : "Save changes";
        _librarySave.IsEnabled = ready && (_creatingLibrary || cache is not null);
        _libraryRename!.IsVisible = !_creatingLibrary && cache is not null;
        _libraryRename.IsEnabled = ready && cache is not null;
        _libraryUse!.IsVisible = !_creatingLibrary;
        _libraryUse.IsEnabled = ready && cache is not null && cache.Device.Id != _indexProfile.SelectedDeviceId;
        _libraryDelete!.IsVisible = !_creatingLibrary && cache is not null;
        _libraryDelete.IsEnabled = ready && cache is not null;
        _libraryCancel!.IsVisible = _creatingLibrary;
        _indexDeviceName!.IsEnabled = ready && _creatingLibrary;
        _indexVariant!.IsEnabled = ready && _creatingLibrary && _indexVariant.IsVisible;
        _libraryMapping!.IsEnabled = ready && (_creatingLibrary || cache is not null);
        _managedLibraries!.IsEnabled = ready;
        if (_indexAvailableDevices is not null) { _indexAvailableDevices.IsEnabled = ready; }
    }

    private void AssignIndexLibrary(IndexDevice device)
    {
        if (LibraryManagementBusy) { RefreshIndexContext(); return; }
        if (!IndexDeviceMatchesProfile(device.Variant)) { RefreshIndexContext(); return; }
        try
        {
            var cache = _indexLibrary.Load(device.Id);
            if (cache is null)
            {
                RefreshIndexContext();
                SetIndexMessage("This device is no longer available. Choose another saved device.");
                return;
            }
            device = cache.Device;
            if (cache is { PresetMapping: null })
            {
                // Resolve the old assignment before replacing it, so another device
                // cannot inherit the active device's legacy mapping by accident.
                cache.PresetMapping = ManagedLibraryMapping(cache);
                _indexLibrary.Save(cache);
            }
        }
        catch (Exception ex) { SetIndexMessage("Could not load device mapping: " + ex.Message); return; }
        if (SaveIndexProfile(profile => profile.AssignDevice(device)))
        {
            _indexSelectedSlot = null; RefreshIndexContext();
            SetIndexMessage($"'{device.Name}' is now assigned to '{_settings.ActiveProfile}'.");
            _ = CheckAssignedLibraryAsync();
        }
    }

    private void SaveManagedLibrary()
    {
        if (LibraryManagementBusy) { return; }
        try
        {
            if (!_creatingLibrary && _managedLibraryId is null) { return; }
            if (_indexVariant?.SelectedItem is not IndexVariantChoice choice) { throw new ArgumentException("Choose a device model."); }
            bool creating = _creatingLibrary;
            // Editing a name cannot edit the saved device identity, even through stale UI state.
            var existing = creating ? null : _indexLibrary.Load(_managedLibraryId!.Value)
                ?? throw new InvalidOperationException("This device is no longer available.");
            var variant = existing?.Device.Variant ?? (_detectedDevice is { } detected && choice.Variant.ToDeviceModel() != detected.Model
                ? LibraryVariants().First(c => c.Variant.ToDeviceModel() == detected.Model).Variant : choice.Variant);
            var device = existing?.Device ?? new IndexDevice(Guid.NewGuid(), _indexDeviceName?.Text ?? "", variant, _detectedDevice?.Firmware);
            if (_librarySceneCc?.Value is not decimal sceneCc || sceneCc != decimal.Truncate(sceneCc))
            { throw new ArgumentException("Scene CC# must be a whole number from 0 to 127."); }
            var mapping = new DevicePresetMapping(_libraryChannel!.SelectedIndex, _libraryOffset!.SelectedIndex, (int)sceneCc);
            if (!mapping.IsValid) { throw new ArgumentException("Choose a valid MIDI channel, display offset and Scene CC#."); }
            var saved = _indexLibrary.SaveDevice(device, mapping);
            _managedLibraryId = saved.Device.Id; _creatingLibrary = false;
            RefreshIndexContext();
            SetIndexMessage(creating ? "Device added. Choose Use for this profile to assign it." : "Device changes saved.");
        }
        catch (Exception ex) { SetIndexMessage(ex.Message); }
    }

    private async Task DeleteManagedLibraryAsync()
    {
        if (LibraryManagementBusy || _managedLibraryId is not Guid id) { return; }
        _changingProfile = true;
        UpdateIndexButtons();
        try
        {
            var cache = _indexLibrary.Load(id)!;
            string[] profiles = LibraryAssignedProfiles(id);
            if (profiles.Length > 0) { SetIndexMessage("Assign another device to these profiles before deleting: " + string.Join(", ", profiles) + "."); return; }
            if (!await ConfirmProfileAsync("Delete device", $"Delete '{LibraryDisplayName(cache.Device)}' and its saved index, preset/scene tags and historical references from all profiles? This cannot be undone. Presets on your hardware are not changed.", "Delete device")) { return; }
            _settings.FractalIndex = _profileStore!.DeleteIndexLibrary(id, _settings.ActiveProfile, _settings.FractalIndex);
            _managedLibraryId = null;
            RefreshIndexContext();
            SetIndexMessage("Device deleted.");
        }
        catch (Exception ex) { SetIndexMessage(ex.Message); }
        finally { _changingProfile = false; UpdateIndexButtons(); }
    }
}
#endif
