using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private readonly ProfileStore? _profileStore;
    private readonly Func<bool, Task<string?>>? _profileFilePickerOverride;
    private ComboBox _profileCombo = null!;
    private TextBlock _profileStatus = null!;
    private bool _refreshingProfiles;
    private bool _changingProfile;
    private int _profileGeneration;
    private bool _profileFileScanning;
    private readonly List<Button> _profileScanButtons = [];
    private readonly List<Control> _profileScanCommands = [];
    internal Func<Task<(List<string> Names, int Skipped)>>? ProfileFileScanner { get; set; }
    internal Task? ProfileScanTask { get; private set; }
    private const string ProfileScanPurpose = "Find saved profiles on this PC from matching settings and favorites files. To read device presets, use Sync device.";

    private Button ProfileScanButton(string name)
    {
        var button = ProfileCommand(name, "Scan profile files");
        ToolTip.SetTip(button, $"Look for matching <name>-settings.json and <name>-favorites.json files in {_profileStore?.DirectoryPath}");
        AutomationProperties.SetHelpText(button, ProfileScanPurpose);
        button.Click += async (_, _) => await (ProfileScanTask = RunProfileActionAsync("rescan", ""));
        _profileScanButtons.Add(button);
        return button;
    }

    private Control ProfileScanDescription(string name, bool showFolder = false)
    {
        var description = new StackPanel { Spacing = 4 };
        description.Children.Add(new TextBlock { Name = name, Text = ProfileScanPurpose, FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap });
        if (showFolder)
        {
            description.Children.Add(new TextBlock { Name = name + "Folder", Text = "Folder: " + _profileStore?.DirectoryPath, FontSize = 12, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap });
        }
        return description;
    }

    private Control BuildProfileCard(Action showProfiles)
    {
        _profileScanButtons.Clear();
        _profileScanCommands.Clear();
        var card = ApprovedCard("Profiles", "Favorites, device assignment and cached names", compact: true);
        var stack = new StackPanel { Spacing = 8, IsEnabled = _profileStore is not null };
        _profileCombo = new ComboBox { Name = "ProfileSelector", HorizontalAlignment = HorizontalAlignment.Stretch };
        _profileCombo.SelectionChanged += async (_, _) =>
        {
            if (!_refreshingProfiles && _profileCombo.SelectedItem is string name && name != _settings.ActiveProfile)
            {
                await RunProfileActionAsync("select", name);
            }
        };
        var selection = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        selection.Children.Add(_profileCombo);
        var rescan = ProfileScanButton("ProfileRescan");
        rescan.Height = rescan.MinHeight = 32;
        rescan.Margin = new Avalonia.Thickness(8, 0, 0, 0);
        Grid.SetColumn(rescan, 1);
        selection.Children.Add(rescan);
        stack.Children.Add(CompactField("Active profile", selection));
        stack.Children.Add(ProfileScanDescription("ProfileScanPurpose"));
        var manage = new Button { Name = "ManageProfiles", Content = "Manage Profiles", HorizontalAlignment = HorizontalAlignment.Left };
        manage.Click += (_, _) => showProfiles();
        stack.Children.Add(manage);
        _profileStatus = new TextBlock { Name = "ProfileStatus", Text = _profileStore?.StartupMessage ?? "Ready.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush };
        stack.Children.Add(_profileStatus);
        AddIndexConfiguration(stack);
        SetApprovedCardContent(card, stack);
        RefreshProfileList();
        return card;
    }

    private void RefreshProfileList()
    {
        _refreshingProfiles = true;
        try
        {
            _settings.Profiles = _profileStore?.ListProfiles() ?? [_settings.ActiveProfile];
            _profileCombo.ItemsSource = _settings.Profiles;
            _profileCombo.SelectedItem = _settings.ActiveProfile;
            RefreshManagedProfiles();
        }
        finally { _refreshingProfiles = false; }
    }

    private Task<bool> ConfirmProfileAsync(string title, string message, string action) =>
        _confirmOverride is not null ? _confirmOverride(title, message, action, "Cancel") : ShowProfileConfirmationAsync(title, message, action);

    private async Task RunProfileActionAsync(string action, string name, string? targetProfile = null)
    {
        if (_profileStore is null || _changingProfile)
        {
            return;
        }

        _changingProfile = true;
        Dictionary<Control, bool>? scanControls = null;
        try
        {
            if (action == "rescan")
            {
                SaveSettingsFromUI();
                _saveFavorites(_favorites);
                _profileFileScanning = true;
                scanControls = _profileScanCommands.Concat(_profileScanButtons).Concat(_managedDataActions)
                    .Append(_profileCombo).Append(_useProfileButton).Append(_darkThemeRadio).Append(_lightThemeRadio)
                    .Distinct().ToDictionary(control => control, control => control.IsEnabled);
                _profileNoticeOrigin = FocusManager?.GetFocusedElement();
                _profileStatus.Text = "Scanning saved profile files…";
                ShowProfileNotice("Scanning saved profile files…", "Looking for matching settings and favorites files in " + _profileStore.DirectoryPath, checking: true);
                foreach (var control in scanControls.Keys) { control.IsEnabled = false; }
                foreach (var button in _profileScanButtons) { button.Content = "Scanning…"; }
                _profileNoticeProgress.IsVisible = false;
                using var progressDelay = Avalonia.Threading.DispatcherTimer.RunOnce(
                    () => _profileNoticeProgress.IsVisible = true, TimeSpan.FromSeconds(1));
                AutomationProperties.SetName(_profileNoticeProgress, "Scanning saved profile files");
                UpdatePresetSyncButtons();
                var scan = await (ProfileFileScanner?.Invoke() ?? _profileStore.ScanProfileFilesAsync());
                var result = _profileStore.ApplyProfileScan(_settings, scan);
                string summary = $"{scan.Names.Count} saved {(scan.Names.Count == 1 ? "profile" : "profiles")} found; " +
                    $"{result.Added} new {(result.Added == 1 ? "profile" : "profiles")}; " +
                    $"{result.Skipped} incomplete or unreadable {(result.Skipped == 1 ? "pair" : "pairs")} skipped.";
                _profileStatus.Text = "Profile scan complete: " + summary;
                ShowProfileNotice("Profile scan complete", summary);
                return;
            }
            if (_presetNamesCts is not null)
            {
                throw new InvalidOperationException("Wait for name synchronization to finish, or cancel it, before changing profiles.");
            }

            string previous = _settings.ActiveProfile;
            string target = targetProfile ?? _selectedProfileName ?? previous;
            bool targetsActive = target == previous;
            if (action == "select" && name == previous) { return; }
            if (action is "create" or "copy" or "rename")
            {
                ProfileStore.ValidateName(name);
            }

            if (action == "delete")
            {
                if (_profileStore.ListProfiles().Count <= 1)
                {
                    throw new InvalidOperationException("Keep at least one profile.");
                }

                if (!await ConfirmProfileAsync("Delete Profile?", $"Delete profile '{target}' and its favorites and cached names?", "Delete profile"))
                {
                    return;
                }
            }
            if ((action is "select" or "create" or "copy" || action == "delete" && targetsActive) && _favEditingId is not null &&
                !await ConfirmProfileAsync("Change Profile?", "Discard unsaved favorite edits and change profile?", "Change Profile"))
            {
                return;
            }

            if (action == "delete" && targetsActive)
            {
                name = _profileStore.ListProfiles().FirstOrDefault(profile => profile != previous && IsManagedProfileReadable(profile))
                    ?? throw new InvalidOperationException("No other readable profile is available. The active profile was preserved.");
            }
            if (action is "select" or "create" or "copy" || action == "delete" && targetsActive)
            {
                var candidate = action switch
                {
                    "create" => new ProfileSettings(),
                    "copy" => targetsActive ? ProfileSettings.From(_settings) : _profileStore.LoadProfile(target),
                    _ => _profileStore.LoadProfile(name),
                };
                if (action is "select" or "delete") { _profileStore.LoadFavorites(name); }
                if (!await ConfirmProfileDeviceMatchAsync(name, candidate))
                {
                    _profileStatus.Text = "Profile switch cancelled. Active profile: " + previous + ".";
                    ShowProfileNotice("Profile switch cancelled", $"'{previous}' is still active. Its favorites remain displayed.");
                    return;
                }
            }

            // A confirmation can yield to other UI events; recheck before touching storage.
            if (_presetNamesCts is not null)
            {
                throw new InvalidOperationException("Wait for name synchronization to finish before changing profiles.");
            }

            if (action is "select" or "create" or "copy" || targetsActive)
            {
                SaveSettingsFromUI();
                _saveFavorites(_favorites);
            }
            if (action == "rename")
            {
                _profileStore.Rename(target, name);
                if (targetsActive)
                {
                    _settings.ActiveProfile = name;
                    try { _saveSettings(_settings); }
                    catch { _settings.ActiveProfile = previous; _profileStore.Rename(name, target); throw; }
                }
                _selectedProfileName = name;
                _profileStatus.Text = $"Renamed {target} to {name}.";
                return;
            }
            if (action == "delete" && !targetsActive)
            {
                _profileStore.Delete(target);
                _selectedProfileName = previous;
                _profileStatus.Text = $"Deleted {target}.";
                return;
            }
            if (action == "create")
            {
                _profileStore.Create(name);
            }
            else if (action == "copy")
            {
                _profileStore.Create(name, targetsActive ? ProfileSettings.From(_settings) : _profileStore.LoadProfile(target),
                    targetsActive ? _favorites : _profileStore.LoadFavorites(target));
            }

            var profile = _profileStore.LoadProfile(name);
            var favorites = _profileStore.LoadFavorites(name);
            ActivateLoadedProfile(name, profile, favorites);

            if (action == "delete")
            {
                _profileStore.Delete(previous);
            }

            _selectedProfileName = name;
            _profileStatus.Text = action switch
            {
                "delete" => $"Deleted {previous}. Active profile: {name}.",
                "copy" => $"Copied {target} to {name}. Active profile: {name}.",
                _ => $"Active profile: {name}.",
            };
            int favoriteCount = favorites.Count(favorite => !favorite.IsEmpty);
            ShowProfileNotice($"Profile '{name}' is active", $"{ProfileFavoriteCount(favoriteCount)} {(favoriteCount == 1 ? "is" : "are")} ready in Favorites.");
        }
        catch (Exception ex)
        {
            _profileStatus.Text = ex.Message;
            ShowProfileNotice("Profile action could not finish", ex.Message,
                action: "Try again", command: () => RunProfileActionAsync(action, name, targetProfile));
        }
        finally
        {
            if (action == "rescan") { _profileFileScanning = false; }
            if (scanControls is not null)
            {
                foreach (var (control, enabled) in scanControls) { control.IsEnabled = enabled; }
                foreach (var button in _profileScanButtons) { button.Content = "Scan profile files"; }
            }
            RefreshProfileList();
            UpdateActiveProfileIndicator();
            RefreshIndexContext();
            _changingProfile = false;
            UpdatePresetSyncButtons();
        }
    }

    private void ActivateLoadedProfile(string name, ProfileSettings profile, List<Favorite> favorites, bool persist = true)
    {
        // Persist a switch before replacing favorites. An overwrite of the active profile
        // has already committed its pair and keeps the same machine-level active name.
        string previous = _settings.ActiveProfile;
        var oldProfile = ProfileSettings.From(_settings);
        profile.ApplyTo(_settings);
        _settings.ActiveProfile = name;
        try { if (persist) { _saveSettings(_settings); } }
        catch { oldProfile.ApplyTo(_settings); _settings.ActiveProfile = previous; throw; }

        _profileGeneration++;
        StopSceneTracking();
        _autoSendTimer.Stop();
        HideFavoriteEditor();
        _favorites.Clear();
        _favorites.AddRange(favorites);
        _enteredDigits = "";
        _currentPreset = _lastPreset = null;
        _currentFavoriteName = null;
        _currentFavoriteScene = null;
        ResetFavoriteSearch();
        _favFilterSelectionId = null;
        _favSearchBox.Text = "";
        ApplyProfileSettingsToUI();
        RefreshFavoritesList();
        UpdateDisplay();
        _syncReadCompleted = 0;
        if (CanReadDeviceNames && _midi.InputOpen && _midi.OutputOpen) { _scenePoll.Start(); }
    }

    private async Task TransferProfileAsync(bool export)
    {
        if (_profileStore is null || _changingProfile)
        {
            return;
        }

        _changingProfile = true;
        try
        {
            string target = _selectedProfileName ?? _settings.ActiveProfile;
            string? path = await PickProfileFileAsync(export);
            if (path is null)
            {
                return;
            }

            if (export)
            {
                if (target == _settings.ActiveProfile)
                {
                    if (_presetNamesCts is not null) { throw new InvalidOperationException("Wait for name synchronization to finish, or cancel it, before exporting the active profile."); }
                    SaveSettingsFromUI();
                    _saveFavorites(_favorites);
                }
                _profileStore.Export(target, path);
                _profileStatus.Text = $"Exported {target} to {Path.GetFileName(path)}.";
            }
            else
            {
                var data = ProfileStore.ReadImport(path);
                var choice = await PromptProfileImportAsync(data);
                if (choice is null) { return; }
                string name = _profileStore.ListProfiles().FirstOrDefault(existing => string.Equals(existing, choice.Name, StringComparison.OrdinalIgnoreCase)) ?? choice.Name;
                bool active = name == _settings.ActiveProfile;
                if (active)
                {
                    if (_presetNamesCts is not null) { throw new InvalidOperationException("Wait for name synchronization to finish, or cancel it, before overwriting the active profile."); }
                    if (_favEditingId is not null && !await ConfirmProfileAsync("Overwrite active profile?", "Discard unsaved favorite edits and replace the active profile?", "Overwrite")) { return; }
                    if (!await ConfirmProfileDeviceMatchAsync(name, data.Settings))
                    {
                        _profileStatus.Text = "Import cancelled. Active profile: " + name + ".";
                        ShowProfileNotice("Import cancelled", $"'{name}' and its favorites were preserved.");
                        return;
                    }
                    if (_presetNamesCts is not null) { throw new InvalidOperationException("Wait for name synchronization to finish before overwriting the active profile."); }
                    SaveSettingsFromUI();
                    _saveFavorites(_favorites);
                }
                name = _profileStore.Import(data, name, choice.Overwrite, choice.LibraryId, choice.KeepImportedLibrary);
                if (active) { ActivateLoadedProfile(name, _profileStore.LoadProfile(name), _profileStore.LoadFavorites(name), persist: false); }
                _selectedProfileName = name;
                int count = data.Favorites.Count(favorite => !favorite.IsEmpty);
                string libraryOutcome = "";
                DescribeProfileImportLibrary(_profileStore.LoadProfile(name), ref libraryOutcome);
                _profileStatus.Text = active
                    ? $"Imported {name}: {ProfileFavoriteCount(count)}. This profile is active."
                    : $"Imported {name}: {ProfileFavoriteCount(count)}. Choose Use profile to show them; '{_settings.ActiveProfile}' is still active.";
                ShowProfileNotice($"Imported profile '{name}'", active
                    ? $"{ProfileFavoriteCount(count)} {(count == 1 ? "is" : "are")} ready in Favorites. The previous profile was backed up." + libraryOutcome
                    : $"{ProfileFavoriteCount(count)} imported. Choose Use profile to show them; '{_settings.ActiveProfile}' is still active." + libraryOutcome,
                    action: active ? null : "Use profile", command: active ? null : () => RunProfileActionAsync("select", name));
            }
        }
        catch (Exception ex)
        {
            _profileStatus.Text = ex.Message;
            ShowProfileNotice(export ? "Export could not finish" : "Import could not finish", ex.Message,
                action: "Try again", command: () => TransferProfileAsync(export));
        }
        finally
        {
            RefreshProfileList();
            UpdateActiveProfileIndicator();
            RefreshIndexContext();
            _changingProfile = false;
            UpdatePresetSyncButtons();
        }
    }

    partial void DescribeProfileImportLibrary(ProfileSettings settings, ref string description);

    private async Task<string?> PickProfileFileAsync(bool export)
    {
        if (_profileFilePickerOverride is not null)
        {
            return await _profileFilePickerOverride(export);
        }

        if (export)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export profile",
                SuggestedFileName = (_selectedProfileName ?? _settings.ActiveProfile) + ".zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Profile ZIP") { Patterns = ["*.zip"] }],
            });
            return file is null ? null : file.TryGetLocalPath() ?? throw new IOException("Choose a local export file.");
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import profile ZIP or matching JSON pair",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Profile files") { Patterns = ["*.zip", "*-settings.json", "*-favorites.json"] }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new IOException("Choose a local profile file.");
    }
}
