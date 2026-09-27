using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly Func<string, string, Task<string?>>? _profileNamePromptOverride;
    private string? _selectedProfileName;
    private ListBox? _managedProfiles;
    private TextBlock _managedProfileLabel = null!;
    private TextBlock _managedProfileCount = null!;
    private TextBlock _managedProfileError = null!;
    private StackPanel _managedProfileFacts = null!;
    private Button _useProfileButton = null!;
    private readonly List<Button> _managedDataActions = [];

    private static Button ProfileCommand(string name, string label) => new()
    {
        Name = name,
        Content = label,
        Height = 28,
        MinHeight = 28,
        Padding = new Thickness(9, 0),
        CornerRadius = new CornerRadius(5),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Border ProfilePane(Control content) => new()
    {
        BorderBrush = UiBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Child = content,
        ClipToBounds = true,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private Control BuildManageProfilesPage(Action showConfig)
    {
        var page = new Grid
        {
            Name = "ProfileManagementLayout",
            Margin = new Thickness(20, 12, 20, 20),
            RowDefinitions = new RowDefinitions("Auto,8,*"),
        };
        var back = ProfileCommand("ProfilesBackToConfig", "← Back to Config");
        back.Background = Brushes.Transparent;
        back.BorderThickness = new Thickness(0);
        back.Click += (_, _) => showConfig();
        page.Children.Add(back);

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,12,Auto,8,Auto"), IsEnabled = _profileStore is not null };
        var toolbar = new Grid { Name = "ProfileManagementToolbar", ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        toolbar.Children.Add(new TextBlock { Text = "Manage Profiles", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center });
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var create = ProfileCommand("ProfileCreate", "+ Create…");
        create.Background = InsetBrush;
        create.Foreground = AccentBrush;
        create.Click += async (_, _) => await PromptProfileActionAsync("create");
        var import = ProfileCommand("ProfileImport", "Import…");
        import.Click += async (_, _) => await TransferProfileAsync(export: false);
        var rescan = ProfileCommand("ProfileManagementRescan", "Rescan");
        rescan.Click += async (_, _) => await RunProfileActionAsync("rescan", "");
        commands.Children.Add(create); commands.Children.Add(import); commands.Children.Add(rescan);
        Grid.SetColumn(commands, 1); toolbar.Children.Add(commands); content.Children.Add(toolbar);

        var split = new Grid { Name = "ProfileManagementSplit", ColumnDefinitions = new ColumnDefinitions("300,12,*") };
        var listColumn = new StackPanel();
        _managedProfiles = new ListBox
        {
            Name = "ManagedProfiles",
            ItemContainerTheme = CompactListItemTheme(),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MaxHeight = 260,
        };
        AutomationProperties.SetName(_managedProfiles, "Profiles");
        ScrollViewer.SetHorizontalScrollBarVisibility(_managedProfiles, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_managedProfiles, ScrollBarVisibility.Auto);
        _managedProfiles.SelectionChanged += (_, _) =>
        {
            if (!_refreshingProfiles && _managedProfiles.SelectedItem is ListBoxItem { Tag: string name })
            {
                _selectedProfileName = name;
                RefreshManagedProfileDetails();
                UpdateManagedProfileSelection();
            }
        };
        var list = new StackPanel();
        list.Children.Add(new Border { Height = 26, Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(0, 0, 0, 1), Child = ProfileListColumns("Profile", "Device", "Status", heading: true) });
        list.Children.Add(_managedProfiles);
        listColumn.Children.Add(ProfilePane(list));
        _managedProfileCount = new TextBlock { Name = "ProfileCount", FontSize = 11, Foreground = SecondaryBrush, Height = 24, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 3) };
        listColumn.Children.Add(_managedProfileCount); split.Children.Add(listColumn);

        var details = new StackPanel { Spacing = 0 };
        var detailHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center };
        _managedProfileLabel = new TextBlock { Name = "ManagedProfile", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 230, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(_managedProfileLabel);
        var selected = new TextBlock { Text = "Selected profile", FontSize = 11, Foreground = SecondaryBrush, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(selected, 1); heading.Children.Add(selected); detailHeader.Children.Add(heading);
        _useProfileButton = ProfileCommand("ProfileUse", "Use profile");
        _useProfileButton.Background = AccentBrush; _useProfileButton.Foreground = Brushes.White;
        _useProfileButton.Click += async (_, _) => await RunProfileActionAsync("select", _selectedProfileName ?? "");
        Grid.SetColumn(_useProfileButton, 2); detailHeader.Children.Add(_useProfileButton); details.Children.Add(detailHeader);
        _managedProfileError = new TextBlock { Name = "ProfileDetailsError", Foreground = DangerBrush, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        details.Children.Add(_managedProfileError);
        _managedProfileFacts = new StackPanel { Name = "ProfileFacts" }; details.Children.Add(_managedProfileFacts);
        var actionRow = new Grid { Name = "ProfileActions", ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _managedDataActions.Clear();
        foreach (var (label, action) in new[] { ("Copy", "copy"), ("Rename", "rename"), ("Export", "export") })
        {
            var button = ProfileCommand("Profile" + label, label + "…");
            button.Click += async (_, _) =>
            {
                if (action == "export") { await TransferProfileAsync(export: true); }
                else { await PromptProfileActionAsync(action); }
            };
            actions.Children.Add(button); _managedDataActions.Add(button);
        }
        actionRow.Children.Add(actions);
        var delete = ProfileCommand("ProfileDelete", "Delete…"); delete.Foreground = DangerBrush;
        delete.Click += async (_, _) => await RunProfileActionAsync("delete", "");
        Grid.SetColumn(delete, 1); actionRow.Children.Add(delete); _managedDataActions.Add(delete); details.Children.Add(actionRow);
        var detailPane = ProfilePane(details); detailPane.Name = "ProfileDetails"; detailPane.Padding = new Thickness(12);
        AutomationProperties.SetName(detailPane, "Selected profile details");
        Grid.SetColumn(detailPane, 2); split.Children.Add(detailPane); Grid.SetRow(split, 2); content.Children.Add(split);
        var status = new TextBlock { Name = "ProfileManagementStatus", FontSize = 11, MinHeight = 20, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = _profileStatus });
        Grid.SetRow(status, 4); content.Children.Add(status);
        var card = new Border
        {
            Name = "ProfileManagementCard",
            Background = SurfaceBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(16, 11, 12, 17),
            BoxShadow = Elevation,
            Child = content,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var scroll = new ScrollViewer { Content = card, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2); page.Children.Add(scroll);
        RefreshProfileList();
        return page;
    }

    private static Grid ProfileListColumns(string name, string device, string status, bool heading = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,58,44"), Margin = new Thickness(8, 0) };
        foreach (var (text, column) in new[] { (name, 0), (device, 1), (status, 2) })
        {
            var cell = new TextBlock
            {
                Text = text,
                FontSize = heading || column == 1 ? 12 : column == 2 ? 11 : 14,
                Foreground = heading || column == 1 ? SecondaryBrush : column == 2 ? AccentBrush : TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = column == 2 && !heading ? TextAlignment.Right : TextAlignment.Left,
            };
            ToolTip.SetTip(cell, text); Grid.SetColumn(cell, column); row.Children.Add(cell);
        }
        return row;
    }

    private static string ProfileDeviceLabel(ProfileSettings settings) => settings.DeviceModel == DeviceModel.AxeFxIII ? "Axe-Fx III" : settings.DeviceModel.ToString();

    private ProfileSettings ReadManagedProfile(string name)
    {
        if (name != _settings.ActiveProfile) { return _profileStore!.LoadProfile(name); }
        var profile = ProfileSettings.From(_settings);
        // Inspect the live controls without saving or mutating active settings.
        profile.MidiChannel = _channelCombo.SelectedIndex;
        profile.DisplayOffset = _offsetCombo.SelectedIndex;
        profile.MaxDisplayedPreset = EffectiveMaximum;
        return profile;
    }

    private void RefreshManagedProfiles()
    {
        if (_managedProfiles is null) { return; }
        if (_selectedProfileName is null || !_settings.Profiles.Contains(_selectedProfileName))
        {
            _selectedProfileName = _settings.Profiles.Contains(_settings.ActiveProfile) ? _settings.ActiveProfile : _settings.Profiles.FirstOrDefault();
        }
        _managedProfiles.Items.Clear();
        foreach (string name in _settings.Profiles)
        {
            string device;
            try { device = ProfileDeviceLabel(ReadManagedProfile(name)); }
            catch (Exception) { device = "—"; }
            var content = new Grid { Height = 26, Background = Brushes.Transparent };
            content.Children.Add(ProfileListColumns(name, device, name == _settings.ActiveProfile ? "Active" : ""));
            content.Children.Add(new Border { Name = "ProfileSelectionAccent", Width = 4, Background = AccentBrush, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = name == _selectedProfileName, IsHitTestVisible = false });
            content.Children.Add(new Border { Height = 1, Background = ThemeBrush("RowSeparatorBrush"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false });
            var item = new ListBoxItem { Tag = name, Content = content, Height = 26, MinHeight = 0, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, name + (name == _settings.ActiveProfile ? ", Active" : ""));
            ToolTip.SetTip(item, name); _managedProfiles.Items.Add(item);
        }
        _managedProfiles.SelectedItem = _managedProfiles.Items.OfType<ListBoxItem>().FirstOrDefault(item => (string)item.Tag! == _selectedProfileName);
        _managedProfileCount.Text = $"{_settings.Profiles.Count} {(_settings.Profiles.Count == 1 ? "profile" : "profiles")}";
        UpdateManagedProfileSelection(); RefreshManagedProfileDetails();
    }

    private void UpdateManagedProfileSelection()
    {
        foreach (var item in _managedProfiles!.Items.OfType<ListBoxItem>())
        {
            var content = (Grid)item.Content!;
            bool selected = (string)item.Tag! == _selectedProfileName;
            content.Background = selected ? ThemeBrush("SelectedBrush") : Brushes.Transparent;
            ((Border)content.Children[1]).IsVisible = selected;
        }
    }

    private void RefreshManagedProfileDetails()
    {
        _managedProfileLabel.Text = _selectedProfileName ?? "No profile selected";
        ToolTip.SetTip(_managedProfileLabel, _selectedProfileName);
        _managedProfileFacts.Children.Clear(); _managedProfileError.IsVisible = false;
        bool readable = false;
        try
        {
            if (_selectedProfileName is string name)
            {
                var profile = ReadManagedProfile(name);
                var favorites = name == _settings.ActiveProfile ? _favorites : _profileStore!.LoadFavorites(name);
                AddProfileFact("Device", ProfileDeviceLabel(profile));
                AddProfileFact("Device name", profile.DeviceName is null ? "Not recorded" : string.IsNullOrWhiteSpace(profile.DeviceName) ? "Unnamed device" : profile.DeviceName);
                AddProfileFact("Favorites", favorites.Count(favorite => !favorite.IsEmpty).ToString());
                AddProfileFact("Cached preset names", profile.PresetNameCache.Values.Count(value => !string.IsNullOrWhiteSpace(value)).ToString());
                AddProfileFact("Cached scene names", profile.SceneNameCaches.Values.Sum(cache => cache.Values.Sum(entry => entry.Names.Count(value => !string.IsNullOrWhiteSpace(value)))).ToString());
                AddProfileFact("Preset mapping", $"MIDI {(profile.MidiChannel == 0 ? "Omni" : profile.MidiChannel)} · Offset {profile.DisplayOffset} · Max preset {profile.MaxDisplayedPreset} · Scene CC {profile.SceneCc}");
                readable = true;
            }
        }
        catch (Exception ex)
        {
            _managedProfileError.Text = $"Cannot read '{_selectedProfileName}': {ex.Message}";
            _managedProfileError.IsVisible = true;
        }
        bool active = _selectedProfileName == _settings.ActiveProfile;
        _useProfileButton.Content = active ? "Active profile" : "Use profile";
        _useProfileButton.IsEnabled = readable && !active;
        foreach (var button in _managedDataActions)
        {
            button.IsEnabled = readable && (button.Name != "ProfileDelete" || _settings.Profiles.Count > 1);
        }
    }

    private void AddProfileFact(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), MinHeight = 25 };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = SecondaryBrush, VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = value, FontSize = 14, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1); row.Children.Add(text);
        _managedProfileFacts.Children.Add(new Border { BorderBrush = ThemeBrush("RowSeparatorBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
    }

    private async Task PromptProfileActionAsync(string action)
    {
        if (_changingProfile || _profileStore is null) { return; }
        string? target = _selectedProfileName;
        string? name;
        _changingProfile = true;
        try { name = await PromptProfileNameAsync(action, action == "rename" ? target ?? "" : ""); }
        catch (Exception ex) { _profileStatus.Text = ex.Message; return; }
        finally { _changingProfile = false; }
        if (name is not null) { await RunProfileActionAsync(action, name, target); }
    }

    private bool IsManagedProfileReadable(string name)
    {
        try { _profileStore!.LoadProfile(name); _profileStore.LoadFavorites(name); return true; }
        catch (Exception) { return false; }
    }

    private async Task<bool> ShowProfileConfirmationAsync(string title, string message, string action)
    {
        var result = new TaskCompletionSource<bool>();
        var confirm = ProfileCommand("ProfileConfirm", action); confirm.Height = confirm.MinHeight = 32;
        var cancel = ProfileCommand("ProfileConfirmCancel", "Cancel"); cancel.Height = cancel.MinHeight = 32;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = SurfaceBrush,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, FontSize = 14, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { confirm, cancel } },
                },
            },
        };
        confirm.Click += (_, _) => { result.TrySetResult(true); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) => result.TrySetResult(false);
        dialog.Opened += (_, _) => cancel.Focus();
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
        await dialog.ShowDialog(this);
        return await result.Task;
    }

    private async Task<string?> PromptProfileNameAsync(string action, string initial)
    {
        if (_profileNamePromptOverride is not null) { return await _profileNamePromptOverride(action, initial); }
        var result = new TaskCompletionSource<string?>();
        var input = new TextBox { Name = "ProfileName", Text = initial, Height = 32, MinHeight = 32, Padding = new Thickness(8, 4) };
        AutomationProperties.SetName(input, "Profile name");
        var error = new TextBlock { Name = "ProfileNameError", FontSize = 12, Foreground = DangerBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        string label = action switch { "create" => "Create", "copy" => "Copy", "rename" => "Rename", _ => "Import" };
        var save = ProfileCommand("ProfileNameSave", label); save.Height = save.MinHeight = 32;
        var cancel = ProfileCommand("ProfileNameCancel", "Cancel"); cancel.Height = cancel.MinHeight = 32;
        var dialog = new Window
        {
            Name = "ProfileNameDialog",
            Title = label + " profile",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = SurfaceBrush,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = action == "import" ? "Profile name (optional)" : "Profile name", FontSize = 12, Foreground = SecondaryBrush }, input, error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { save, cancel } },
                },
            },
        };
        void Submit()
        {
            try
            {
                string name = input.Text ?? "";
                if (action != "import" || !string.IsNullOrWhiteSpace(name)) { ProfileStore.ValidateName(name); }
                if (action != "import" && _profileStore!.ListProfiles().Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase) && (action != "rename" || existing != _selectedProfileName)))
                {
                    throw new InvalidOperationException("A profile with that name already exists.");
                }
                result.TrySetResult(name); dialog.Close();
            }
            catch (Exception ex) { error.Text = ex.Message; error.IsVisible = true; }
        }
        save.Click += (_, _) => Submit(); cancel.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) => result.TrySetResult(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Submit(); e.Handled = true; }
        };
        await dialog.ShowDialog(this);
        return await result.Task;
    }
}
