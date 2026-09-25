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
    private TextBox _profileName = null!;
    private TextBlock _profileStatus = null!;
    private bool _refreshingProfiles;
    private bool _changingProfile;
    private int _profileGeneration;

    private Control BuildProfileCard()
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
        _profileName = new TextBox { Name = "ProfileName", Watermark = "New, copied or renamed profile name" };
        stack.Children.Add(_profileName);
        var actions = new Grid { Name = "ProfileActions", ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*"), RowDefinitions = new RowDefinitions("Auto,8,Auto") };
        foreach (var (label, action) in new[] { ("Create", "create"), ("Copy", "copy"), ("Rename", "rename"), ("Delete", "delete") })
        {
            var button = new Button { Name = "Profile" + label, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (action == "delete")
            {
                button.Foreground = DangerBrush;
            }

            button.Click += async (_, _) => await RunProfileActionAsync(action, _profileName.Text ?? "");
            actions.Children.Add(button);
        }
        var import = new Button { Name = "ProfileImport", Content = "Import…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var export = new Button { Name = "ProfileExport", Content = "Export…", HorizontalAlignment = HorizontalAlignment.Stretch };
        import.Click += async (_, _) => await TransferProfileAsync(export: false);
        export.Click += async (_, _) => await TransferProfileAsync(export: true);
        ToolTip.SetTip(import, "Import a profile ZIP or a matching JSON file pair. Optional new name above.");
        ToolTip.SetTip(export, "Export the active profile and its cached names as a ZIP file.");
        actions.Children.Add(import);
        actions.Children.Add(export);
        for (int index = 0; index < actions.Children.Count; index++)
        {
            Grid.SetColumn(actions.Children[index], index % 3 * 2);
            Grid.SetRow(actions.Children[index], index / 3 * 2);
        }
        stack.Children.Add(actions);
        _profileStatus = new TextBlock { Name = "ProfileStatus", Text = _profileStore?.StartupMessage, TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush };
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
        }
        finally { _refreshingProfiles = false; }
    }

    private Task<bool> ConfirmProfileAsync(string title, string message, string action) =>
        _confirmOverride is not null ? _confirmOverride(title, message, action, "Cancel") : ShowConfirmAsync(title, message, action, "Cancel");

    private async Task RunProfileActionAsync(string action, string name)
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

                if (!await ConfirmProfileAsync("Delete Profile?", $"Delete profile '{previous}' and its favorites, mapping and cached names?", "Delete Profile"))
                {
                    return;
                }
            }
            else if (action != "rename" && _favEditingId is not null &&
                !await ConfirmProfileAsync("Change Profile?", "Discard unsaved favorite edits and change profile?", "Change Profile"))
            {
                return;
            }

            // A confirmation can yield to other UI events; recheck before touching storage.
            if (_presetNamesCts is not null)
            {
                throw new InvalidOperationException("Wait for name synchronization to finish before changing profiles.");
            }

            SaveSettingsFromUI();
            _saveFavorites(_favorites);
            if (action == "rename")
            {
                _profileStore.Rename(previous, name);
                _settings.ActiveProfile = name;
                try { _saveSettings(_settings); }
                catch { _settings.ActiveProfile = previous; _profileStore.Rename(name, previous); throw; }
                _profileName.Text = "";
                _profileStatus.Text = $"Renamed profile to {name}.";
                return;
            }
            if (action == "create")
            {
                _profileStore.Create(name);
            }
            else if (action == "copy")
            {
                _profileStore.Create(name, ProfileSettings.From(_settings), _favorites);
            }

            if (action == "delete")
            {
                name = _profileStore.ListProfiles().First(profile => profile != previous);
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

            _profileName.Text = "";
            _profileStatus.Text = action switch
            {
                "delete" => $"Deleted {previous}. Active profile: {name}.",
                "copy" => $"Copied {previous} to {name}. Active profile: {name}.",
                _ => $"Active profile: {name}.",
            };
        }
        catch (Exception ex) { _profileStatus.Text = ex.Message; }
        finally
        {
            RefreshProfileList();
            UpdateActiveProfileIndicator();
            _changingProfile = false;
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
            string? path = await PickProfileFileAsync(export);
            if (path is null)
            {
                return;
            }

            if (export)
            {
                SaveSettingsFromUI();
                _saveFavorites(_favorites);
                _profileStore.Export(_settings.ActiveProfile, path);
                _profileStatus.Text = $"Exported {_settings.ActiveProfile} to {Path.GetFileName(path)}.";
            }
            else
            {
                string name = _profileStore.Import(path, _profileName.Text);
                _profileName.Text = "";
                _profileStatus.Text = $"Imported {name}. Select it above to use it.";
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
                SuggestedFileName = _settings.ActiveProfile + ".zip",
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
