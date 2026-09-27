using Avalonia.Controls;
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

    private Control BuildProfileCard(Action showProfiles)
    {
        var card = ApprovedCard("Profiles", "Favorites, preset mapping and cached names", compact: true);
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
        var rescan = new Button { Name = "ProfileRescan", Content = "Rescan", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(rescan, "Find profile file pairs in " + _profileStore?.DirectoryPath);
        rescan.Click += async (_, _) => await RunProfileActionAsync("rescan", "");
        Grid.SetColumn(rescan, 1);
        selection.Children.Add(rescan);
        stack.Children.Add(CompactField("Active profile", selection));
        var manage = new Button { Name = "ManageProfiles", Content = "Manage Profiles", HorizontalAlignment = HorizontalAlignment.Left };
        manage.Click += (_, _) => showProfiles();
        stack.Children.Add(manage);
        _profileStatus = new TextBlock { Name = "ProfileStatus", Text = _profileStore?.StartupMessage ?? "Ready.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush };
        stack.Children.Add(_profileStatus);
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
        try
        {
            if (action == "rescan")
            {
                SaveSettingsFromUI();
                _saveFavorites(_favorites);
                var result = _profileStore.Rescan(_settings);
                _profileStatus.Text = $"Rescan complete: {result.Added} new profiles; {result.Skipped} incomplete or unreadable pairs skipped.";
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

                if (!await ConfirmProfileAsync("Delete Profile?", $"Delete profile '{target}' and its favorites, mapping and cached names?", "Delete profile"))
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
            // Persist the selected profile before replacing any in-memory data.
            var oldProfile = ProfileSettings.From(_settings);
            profile.ApplyTo(_settings);
            _settings.ActiveProfile = name;
            try { _saveSettings(_settings); }
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
            _presetNamesProgress.Value = 0;
            _presetNamesStatus.Text = CanReadDeviceNames
                ? $"{_settings.PresetNameCache.Count} cached preset names in this profile." : UnsupportedNameReads;
            if (CanReadDeviceNames && _midi.InputOpen && _midi.OutputOpen)
            {
                _scenePoll.Start();
            }

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
        }
        catch (Exception ex) { _profileStatus.Text = ex.Message; }
        finally
        {
            RefreshProfileList();
            UpdateActiveProfileIndicator();
            _changingProfile = false;
            UpdatePresetSyncButtons();
        }
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
            string? importedName = null;
            if (!export)
            {
                importedName = await PromptProfileNameAsync("import", "");
                if (importedName is null) { return; }
            }
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
                string name = _profileStore.Import(path, importedName);
                _selectedProfileName = name;
                _profileStatus.Text = $"Imported {name}.";
            }
        }
        catch (Exception ex) { _profileStatus.Text = ex.Message; }
        finally
        {
            RefreshProfileList();
            _changingProfile = false;
        }
    }

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
