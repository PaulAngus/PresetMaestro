using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
#if FRACTAL_INDEX
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
#endif

namespace PresetMaestro;

public partial class MainWindow
{
    private Window? _connectionSyncDialog;
    private Border? _connectionSyncStatusPanel;
    private Border? _connectionSyncNextPanel;
    private TextBlock? _connectionSyncDevice, _connectionSyncProfile;
    private TextBlock? _connectionSyncStatusTitle, _connectionSyncNext;
    private TextBlock? _connectionSyncNextHeading;
    private TextBlock? _connectionSyncStatus;
    private ProgressBar? _connectionSyncProgress;
    private Button? _connectionSyncNames, _connectionSyncScenes, _connectionSyncCancel, _connectionSyncDone;
    private Button? _connectionSyncManageProfiles;
    private Expander? _connectionSyncOtherOptions;
#if FRACTAL_INDEX
    private Button? _connectionSyncLibrary, _connectionSyncResume, _connectionSyncCheck, _connectionSyncFullCheck;
    private Button? _connectionSyncManageLibraries;
    private Button? _connectionSyncShowChecks;
    private Button? _connectionSyncRestart;
    private TextBlock? _connectionSyncReadErrors;
    private string? _connectionSyncCheckedContext, _connectionSyncMatchedContext;
    private string? _connectionSyncDeviceCheckContext;
    private bool _showConnectionSyncChecks;
    private string? _initialConnectionSyncContext;
    private SyncDialogOutcome? _indexConnectionSyncCompletion;
    private TextBlock? _connectionSyncCompareNote;
    private const string QuickMatchDescription = "Checks preset names first, then samples preset content and scenes.";
    private const string FullMatchDescription = "Checks preset names first, then every preset and scene. May take several minutes.";
    private Grid? _connectionSyncCheckChoices;
#endif
    private bool _syncingFavoriteScenes;
    private readonly Dictionary<string, TextBlock> _connectionSyncDescriptions = [];
    private enum SyncStatusTone { Neutral, Activity, Success, Attention }
    private sealed record SyncDialogOutcome(string Context, string Title, string Detail, string Next, SyncStatusTone Tone);
    private SyncDialogOutcome? _connectionSyncOutcome;

    private string ConnectionSyncContext => $"{_connectionGeneration}:{_profileGeneration}"
#if FRACTAL_INDEX
        + $":{_indexProfile.SelectedDeviceId}"
#endif
        ;

    private void SetConnectionSyncOutcome(string title, string detail, string next, bool needsAttention = false)
    {
        _connectionSyncOutcome = new(ConnectionSyncContext, title, detail, next, needsAttention ? SyncStatusTone.Attention : SyncStatusTone.Success);
#if FRACTAL_INDEX
        _indexConnectionSyncCompletion = null;
        if (_indexChecking)
        {
            _libraryCheckOutcome = _connectionSyncOutcome;
            _connectionSyncCheckedContext = ConnectionSyncContext;
            _connectionSyncMatchedContext = needsAttention ? null : ConnectionSyncContext;
            _showConnectionSyncChecks = false;
        }
#endif
    }

    private void OpenConnectionSyncOptions(bool initialConnection = false)
    {
        if (_detectedDevice is null || !_midi.InputOpen || !_midi.OutputOpen) { return; }
#if FRACTAL_INDEX
        if (initialConnection) { _initialConnectionSyncContext = ConnectionSyncContext; }
#endif
        if (_connectionSyncDialog is not null) { _connectionSyncDialog.Activate(); return; }
        var originatingControl = FocusManager?.GetFocusedElement() as Control;
        var dialog = new Window
        {
            Name = "ConnectionSyncDialog",
            Title = "Sync with connected device",
            Width = Math.Min(650, Math.Max(400, Bounds.Width - 40)),
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            FontSize = 14,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
        };
        _connectionSyncDialog = dialog;
        _connectionSyncDescriptions.Clear();
#if FRACTAL_INDEX
        _showConnectionSyncChecks = _connectionSyncCheckedContext != ConnectionSyncContext;
#endif
        var body = new StackPanel { Margin = new(20, 20, 20, 12), Spacing = 12 };
        var identity = new StackPanel { Name = "ConnectionSyncHeader", Spacing = 6 };
        _connectionSyncDevice = new TextBlock { Name = "ConnectionSyncDevice", FontSize = 14, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        _connectionSyncProfile = new TextBlock { Name = "ConnectionSyncProfile", FontSize = 14, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap };
        identity.Children.Add(_connectionSyncDevice);
        identity.Children.Add(_connectionSyncProfile);
        body.Children.Add(identity);
        var status = new StackPanel { Spacing = 8 };
        _connectionSyncStatusTitle = new TextBlock { Name = "ConnectionSyncStatusTitle", FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_connectionSyncStatusTitle, AutomationLiveSetting.Polite);
        _connectionSyncStatus = new TextBlock { Name = "ConnectionSyncStatus", TextWrapping = TextWrapping.Wrap, Foreground = TextBrush, FontSize = 14 };
        _connectionSyncProgress = new ProgressBar { Name = "ConnectionSyncProgress", Height = 12, Foreground = AccentBrush };
        status.Children.Add(_connectionSyncStatusTitle); status.Children.Add(_connectionSyncStatus);
        status.Children.Add(_connectionSyncProgress);
        AutomationProperties.SetName(_connectionSyncProgress, "Device read progress");
#if FRACTAL_INDEX
        var primaryActions = new WrapPanel { Name = "ConnectionSyncActions", Orientation = Orientation.Horizontal };
        _connectionSyncLibrary = IndexButton("Sync device", "ConnectionSyncLibrary");
        _connectionSyncLibrary.Background = AccentBrush; _connectionSyncLibrary.Foreground = Brushes.White;
        _connectionSyncLibrary.MinHeight = 36; _connectionSyncLibrary.FontSize = 14;
        _connectionSyncLibrary.Click += async (_, _) => await SyncIndexAsync();
        _connectionSyncResume = IndexButton("Resume sync", "ConnectionSyncResume");
        _connectionSyncResume.Background = AccentBrush; _connectionSyncResume.Foreground = Brushes.White;
        _connectionSyncResume.MinHeight = 36; _connectionSyncResume.FontSize = 14;
        _connectionSyncResume.Click += async (_, _) => await SyncIndexAsync(resume: true);
        primaryActions.Children.Add(_connectionSyncLibrary); primaryActions.Children.Add(_connectionSyncResume);
        status.Children.Add(primaryActions);
#endif
        _connectionSyncStatusPanel = new Border { Name = "ConnectionSyncStatusPanel", Padding = new(16), BorderThickness = new(4, 0, 0, 0), Child = status };
        body.Children.Add(_connectionSyncStatusPanel);
#if FRACTAL_INDEX
        body.Children.Add(BuildLibraryMatchDetails());
#endif
        _connectionSyncNextHeading = new TextBlock { Name = "ConnectionSyncNextHeading", Text = "Next step", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush };
        _connectionSyncNext = new TextBlock { Name = "ConnectionSyncNext", TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = TextBrush };
        var nextStep = new StackPanel { Spacing = 6 };
        nextStep.Children.Add(_connectionSyncNextHeading); nextStep.Children.Add(_connectionSyncNext);
        _connectionSyncNextPanel = new Border
        {
            Name = "ConnectionSyncNextPanel",
            Child = nextStep,
            Padding = new(12),
            CornerRadius = new(6),
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new(1),
        };
        body.Children.Add(_connectionSyncNextPanel);
#if FRACTAL_INDEX
        body.Children.Add(BuildConnectionLibrarySetup());
        _connectionSyncCheckChoices = CreateConnectionLibraryCheckChoices();
        body.Children.Add(_connectionSyncCheckChoices);
        _connectionSyncCompareNote = new TextBlock
        {
            Name = "ConnectionSyncCompareNote",
            Text = "A successful check also refreshes all preset names. Preset content, scenes and amps are refreshed by Sync device below.",
            FontSize = 14,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(_connectionSyncCompareNote);
#endif
        var rows = new StackPanel { Spacing = 0 };
        _connectionSyncOtherOptions = new Expander { Name = "ConnectionSyncOtherOptions", Header = "Other sync options", Content = rows, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(_connectionSyncOtherOptions);
#if FRACTAL_INDEX
        _connectionSyncReadErrors = new TextBlock { Name = "ConnectionSyncReadErrors", TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new(0, 8), Foreground = TextBrush };
        rows.Children.Add(_connectionSyncReadErrors);
#endif
        Button AddOption(string title, string description, string label, string name)
        {
            var row = new Grid { ColumnDefinitions = new("*,16,Auto"), Margin = new(0, 10) };
            var copy = new StackPanel { Spacing = 6 };
            copy.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
            var descriptionText = new TextBlock { Name = name + "Description", Text = description, FontSize = 14, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
            _connectionSyncDescriptions[descriptionText.Name] = descriptionText;
            copy.Children.Add(descriptionText);
            row.Children.Add(copy);
            var button = new Button { Name = name, Content = label, MinWidth = 105, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(button, 2); row.Children.Add(button);
            rows.Children.Add(new Border { Child = row, BorderBrush = UiBorderBrush, BorderThickness = new(0, 0, 0, 1) });
            return button;
        }
        _connectionSyncNames = AddOption("Preset names only", "Refresh the Favorites preset picker after renaming or moving presets. Also refreshed automatically after a successful device check.", "Sync names", "ConnectionSyncNames");
        _connectionSyncNames.Click += async (_, _) => { await SyncPresetNamesAsync(); RefreshConnectionSyncOptions(); };
        _connectionSyncScenes = AddOption("Favorite scene names only", "Refresh this profile’s favorite labels after renaming scenes. Useful when you do not need a full device sync.", "Sync scenes", "ConnectionSyncScenes");
        _connectionSyncScenes.Click += async (_, _) => await SyncFavoriteSceneNamesAsync();
#if FRACTAL_INDEX
        _connectionSyncRestart = AddOption("Read all slots again", "Read the connected device again. Your previous complete saved scan is kept until you accept a complete replacement.", "Start over", "ConnectionSyncRestart");
        _connectionSyncRestart.Click += async (_, _) => await SyncIndexAsync();
#endif
        var management = new WrapPanel { Name = "ConnectionSyncManagement", Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 0) };
        _connectionSyncManageProfiles = new Button { Content = "Manage profiles", Name = "ConnectionManageProfiles", Margin = new(0, 0, 8, 0) };
        _connectionSyncManageProfiles.Click += (_, _) => OpenSyncManagement(dialog, "ManageProfiles"); management.Children.Add(_connectionSyncManageProfiles);
#if FRACTAL_INDEX
        _connectionSyncManageLibraries = new Button { Content = "Manage devices", Name = "ConnectionManageLibraries", Margin = new(0, 0, 8, 0) };
        _connectionSyncManageLibraries.Click += (_, _) => OpenSyncManagement(dialog, "ManageLibraries"); management.Children.Add(_connectionSyncManageLibraries);
#endif
        rows.Children.Add(management);
        var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new(20, 14) };
#if FRACTAL_INDEX
        _connectionSyncShowChecks = new Button { Name = "ConnectionSyncShowChecks", Content = "Check device again", Background = Brushes.Transparent, BorderThickness = new(0), Foreground = AccentBrush, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        _connectionSyncShowChecks.Click += (_, _) => { _showConnectionSyncChecks = true; RefreshConnectionSyncOptions(); };
        footer.Children.Add(_connectionSyncShowChecks);
#endif
        _connectionSyncCancel = new Button { Content = "Cancel read", Name = "ConnectionSyncCancel", Margin = new(0, 0, 8, 0) };
        Grid.SetColumn(_connectionSyncCancel, 1);
        _connectionSyncCancel.Click += (_, _) => _presetNamesCts?.Cancel(); footer.Children.Add(_connectionSyncCancel);
        _connectionSyncDone = new Button { Content = "Skip for now", Name = "ConnectionSyncDone" };
        Grid.SetColumn(_connectionSyncDone, 2);
        _connectionSyncDone.Click += (_, _) =>
        {
#if FRACTAL_INDEX
            if (_connectionLibrarySetupRequired) { Disconnect(); return; }
#endif
            dialog.Close();
        };
        footer.Children.Add(_connectionSyncDone);
        var layout = new Grid { RowDefinitions = new("*,Auto"), MaxHeight = Math.Max(300, Math.Min(720, Bounds.Height - 60)) };
        layout.Children.Add(new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var footerPanel = new Border { Child = footer, Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new(0, 1, 0, 0) };
        Grid.SetRow(footerPanel, 1); layout.Children.Add(footerPanel);
        dialog.Content = layout;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; } };
#if FRACTAL_INDEX
        dialog.Closing += (_, _) => { if (_connectionLibrarySetupRequired && _detectedDevice is not null) { Disconnect(); } };
#endif
        dialog.Closed += (_, _) =>
        {
            if (_connectionSyncDialog != dialog) { return; }
            _connectionSyncDialog = null;
            originatingControl?.Focus();
#if FRACTAL_INDEX
            _initialConnectionSyncContext = null;
#endif
        };
        RefreshConnectionSyncOptions();
        _ = dialog.ShowDialog(this);
    }

#if FRACTAL_INDEX
    private void CompleteInitialConnectionSync()
    {
        if (_initialConnectionSyncContext != ConnectionSyncContext || _connectionSyncDialog is null ||
            _presetNamesCts is not null || _connectionCts is not null || _changingProfile ||
            _connectionSyncOutcome is not { Tone: SyncStatusTone.Success } outcome || outcome.Context != ConnectionSyncContext ||
            _connectionSyncMatchedContext != ConnectionSyncContext || _indexCache?.Committed is null || _indexError is not null)
        { return; }
        string detail = _libraryMatch is { } match && outcome.Title != "Device synced"
            ? $"{match.Matched} of {match.Compared} {(match.IsSample ? "sampled" : "populated")} presets and their scenes matched. " +
                (CanReadDeviceNames ? "All preset names were refreshed. " : "") + "You’re browsing the saved device data."
            : "Presets, scenes, amps and this profile’s preset and scene names were refreshed.";
        _indexConnectionSyncCompletion = outcome with { Detail = detail, Next = "Select a preset to inspect its scenes and amp channels." };
        _connectionSyncDialog.Close();
        _presetIndexNavigation!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RefreshIndexConnectionSyncCompletion();
    }

    private Grid CreateConnectionLibraryCheckChoices()
    {
        var choices = new Grid { Name = "ConnectionSyncCheckChoices", ColumnDefinitions = new("*,14,*") };
        Button AddChoice(string badge, string title, string scope, string scenes, string description, string label, string name, bool recommended, int column)
        {
            var content = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto") };
            var badgeText = new TextBlock { Text = badge, FontSize = 13, FontWeight = recommended ? FontWeight.SemiBold : FontWeight.Normal, Foreground = recommended ? AccentBrush : SecondaryBrush };
            content.Children.Add(new Border { Child = badgeText, Background = recommended ? SurfaceBrush : Brushes.Transparent, CornerRadius = new(4), Padding = recommended ? new(8, 3) : new(0, 3), HorizontalAlignment = HorizontalAlignment.Left });
            var heading = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new(0, 14, 0, 14) };
            Grid.SetRow(heading, 1); content.Children.Add(heading);
            var scopeText = new TextBlock { Text = scope, FontSize = 18, FontWeight = FontWeight.SemiBold, Margin = new(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(scopeText, 2); content.Children.Add(scopeText);
            var sceneText = new TextBlock { Text = scenes, FontSize = 14, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(sceneText, 3); content.Children.Add(sceneText);
            var descriptionText = new TextBlock { Name = name + "Description", Text = description, FontSize = 14, Foreground = SecondaryBrush, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12) };
            _connectionSyncDescriptions[descriptionText.Name] = descriptionText;
            Grid.SetRow(descriptionText, 4); content.Children.Add(descriptionText);
            var button = new Button { Name = name, Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (recommended) { button.Background = AccentBrush; button.Foreground = Brushes.White; }
            Grid.SetRow(button, 5); content.Children.Add(button);
            var card = new Border
            {
                Name = name + "Card",
                Child = content,
                Padding = new(16),
                CornerRadius = new(7),
                BorderThickness = new(1),
                BorderBrush = recommended ? AccentBrush : UiBorderBrush,
                Background = recommended ? ThemeBrush("SelectedBrush") : SurfaceBrush,
            };
            Grid.SetColumn(card, column); choices.Children.Add(card);
            return button;
        }
        _connectionSyncCheck = AddChoice("Recommended", "Quick check", "Up to 16 random presets", "All saved scenes in those presets.", QuickMatchDescription, "Start quick check", "ConnectionSyncCheck", true, 0);
        _connectionSyncCheck.Click += async (_, _) => await CheckAssignedLibraryAsync();
        _connectionSyncFullCheck = AddChoice("More thorough", "Full check", "Every preset slot", "All saved scenes across the device.", FullMatchDescription, "Start full check", "ConnectionSyncFullCheck", false, 2);
        _connectionSyncFullCheck.Click += async (_, _) => await CheckAssignedLibraryAsync(full: true);
        return choices;
    }
#endif

    private void OpenSyncManagement(Window dialog, string name)
    {
        dialog.Close();
        this.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NavConfig").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        this.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void RefreshConnectionSyncOptions()
    {
        if (_connectionSyncDialog is null) { return; }
        bool busy = _connectionCts is not null || _presetNamesCts is not null || _changingProfile;
        bool ready = _detectedDevice is not null && _midi.InputOpen && _midi.OutputOpen && !busy;
        bool checking = false;
#if FRACTAL_INDEX
        busy |= _connectionLibraryChoiceBusy;
        ready &= !_connectionLibrarySetupRequired && !_connectionLibraryChoiceBusy;
        RefreshConnectionLibrarySetup(busy);
        SetSyncOptionVisible(_connectionSyncNames!, !_connectionLibrarySetupRequired);
        SetSyncOptionVisible(_connectionSyncScenes!, !_connectionLibrarySetupRequired);
        if (_connectionSyncManageProfiles!.Parent is WrapPanel management) { management.IsVisible = !_connectionLibrarySetupRequired; }
        checking = _indexChecking;
        _connectionSyncLibrary!.IsEnabled = CanSyncIndex;
        _connectionSyncResume!.IsEnabled = _indexResumeButton?.IsEnabled == true;
        bool chooseDestination = _connectionLibrarySetupRequired &&
            (_connectionLibraryVariant is null || _connectionDefaultLibrary is not null && _connectionLibraryCandidate is null);
        bool incomplete = !chooseDestination && _indexCache?.LastAttempt is { Status: not "Complete" };
        _connectionSyncCheck!.IsEnabled = CanCheckIndex;
        _connectionSyncFullCheck!.IsEnabled = _connectionSyncCheck.IsEnabled;
        bool canCompare = !incomplete && !_connectionLibrarySetupRequired && _indexCache?.Committed is not null && _indexCache.Device.Variant.ToDeviceModel() == _detectedDevice?.Model;
        _connectionSyncCheckChoices!.IsVisible = canCompare && _showConnectionSyncChecks && !busy;
        _connectionSyncCompareNote!.IsVisible = _connectionSyncCheckChoices.IsVisible;
        _connectionSyncLibrary.IsVisible = !busy && !chooseDestination && !incomplete && !_connectionSyncCheckChoices.IsVisible;
        _connectionSyncResume.IsVisible = !busy && incomplete;
        _connectionSyncRestart!.IsEnabled = _connectionSyncLibrary.IsEnabled;
        _connectionSyncRestart.Content = incomplete ? "Start over" : "Sync device";
        SetSyncOptionVisible(_connectionSyncRestart, incomplete || _connectionSyncCheckChoices.IsVisible);
        _connectionSyncOtherOptions!.IsVisible = !busy && !chooseDestination && (!_connectionLibrarySetupRequired || incomplete);
        _connectionSyncReadErrors!.IsVisible = incomplete && _indexCache!.LastAttempt!.Errors.Count > 0;
        _connectionSyncReadErrors.Text = incomplete ? string.Join("\n", _indexCache!.LastAttempt!.Errors.OrderBy(e => e.Key).Take(8)
            .Select(e => $"Preset {e.Key + _settings.DisplayOffset:D3}: " + (e.Value.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                ? "The device did not reply before the read timed out." : e.Value))) : "";
        _connectionSyncShowChecks!.IsVisible = canCompare && !_connectionSyncCheckChoices.IsVisible && !busy;
        _connectionSyncManageLibraries!.IsEnabled = !busy && !_connectionLibrarySetupRequired;
        SetSyncOptionDescription("ConnectionSyncCheckDescription", _indexCache?.Committed is null
            ? "Update device data first to save presets and scenes for comparison."
            : CanReadDeviceNames ? QuickMatchDescription : "Compare a sample of presets with the saved device data.");
        SetSyncOptionDescription("ConnectionSyncFullCheckDescription", _indexCache?.Committed is null
            ? "Update device data first to save presets and scenes for comparison."
            : CanReadDeviceNames ? FullMatchDescription : "Checks every preset and scene. May take several minutes.");
        _connectionSyncCompareNote.Text = CanReadDeviceNames
            ? "A successful check also refreshes all preset names. Preset content, scenes and amps are refreshed by Sync device below."
            : "Checks compare saved data. Use Sync device below to refresh presets, scenes and amps.";
        ToolTip.SetTip(_connectionSyncLibrary, _indexCache is null ? "Assign a device using Manage devices first." :
            _detectedDevice?.Model != _indexCache.Device.Variant.ToDeviceModel() ? "The connected and selected device models differ." : null);
#endif
        _connectionSyncNames!.IsEnabled = ready && CanReadDeviceNames;
        _connectionSyncScenes!.IsEnabled = ready && CanReadDeviceNames && _favorites.Any(f => !f.IsEmpty);
        SetSyncOptionDescription("ConnectionSyncScenesDescription", !_favorites.Any(f => !f.IsEmpty)
            ? "No favorites in this profile yet. Add favorites to sync their saved scene names."
            : "Refresh this profile’s favorite labels after renaming scenes. Useful when you do not need a full device sync.");
        ToolTip.SetTip(_connectionSyncScenes, !CanReadDeviceNames ? UnsupportedNameReads : !_favorites.Any(f => !f.IsEmpty) ? "This profile has no favorites to read." : null);
        ToolTip.SetTip(_connectionSyncNames, CanReadDeviceNames ? null : UnsupportedNameReads);
        SetSyncOptionDescription("ConnectionSyncNamesDescription", $"{_settings.PresetNameCache.Count} preset names cached in this profile. Refresh names after renaming or moving presets; a successful device check also refreshes them.");
        _connectionSyncCancel!.IsVisible = _presetNamesCts is not null;
        _connectionSyncDone!.IsVisible = !busy;
        _connectionSyncDone!.Content = _presetNamesCts is not null || _connectionSyncOutcome is { } outcome && outcome.Context == ConnectionSyncContext ? "Done" : "Skip for now";
#if FRACTAL_INDEX
        if (_connectionLibrarySetupRequired)
        {
            _connectionSyncDone.Content = "Disconnect";
            _connectionSyncLibrary!.Background = AccentBrush;
            _connectionSyncLibrary.Foreground = Brushes.White;
        }
        _connectionSyncCancel.Content = _indexScanning ? "Stop sync" : "Cancel read";
        _connectionSyncManageProfiles!.IsEnabled = !busy && !_connectionLibrarySetupRequired;
        bool matched = !busy && _connectionSyncMatchedContext == ConnectionSyncContext;
        if (incomplete && !_connectionLibrarySetupRequired && _indexCache?.Committed is not null) { _connectionSyncDone.Content = "Use previous data"; }
        if (matched) { _connectionSyncDone.Content = "Use saved data"; }
        _connectionSyncDone.Background = matched ? AccentBrush : SurfaceBrush;
        _connectionSyncDone.Foreground = matched ? Brushes.White : TextBrush;
#endif
#if !FRACTAL_INDEX
        _connectionSyncManageProfiles!.IsEnabled = !busy;
        _connectionSyncOtherOptions!.IsExpanded = true;
        _connectionSyncOtherOptions.IsVisible = !busy;
#endif
        RefreshConnectionSyncIdentity(checking);
        RefreshConnectionSyncStatus(checking);
    }

#if FRACTAL_INDEX
    private bool ConnectionDeviceCheckRunning => _indexChecking || _indexScanning;
#else
    private static bool ConnectionDeviceCheckRunning => false;
#endif

    private bool HasCompletedConnectionDeviceCheck =>
#if FRACTAL_INDEX
        _connectionSyncDeviceCheckContext == ConnectionSyncContext;
#else
        _detectedDevice is not null;
#endif

    private void RefreshConnectionSyncIdentity(bool checking)
    {
        // Reading MIDI identity alone does not complete the library check. Reveal
        // reported details only after a comparison or a complete library sync.
        checking |= ConnectionDeviceCheckRunning;
        bool showReportedDevice = !checking && HasCompletedConnectionDeviceCheck;
        _connectionSyncDevice!.Text = checking ? "Checking device…"
            : _detectedDevice is { } device ? showReportedDevice ? "Device reports: " + device.Label : "Device not checked"
            : "Not connected";
        _connectionSyncProfile!.Text = $"Profile: {_settings.ActiveProfile}";
        if (showReportedDevice && _detectedDevice is { } detected)
        { _connectionSyncProfile.Text += $" · Firmware: {detected.Firmware ?? "Unavailable"}"; }
        AutomationProperties.SetName(_connectionSyncDevice, _connectionSyncDevice.Text);
        AutomationProperties.SetName(_connectionSyncProfile, _connectionSyncProfile.Text);
    }

    private void RefreshConnectionSyncStatus(bool checking)
    {
        string title = "Connected — choose what to sync";
        string detail = "Device information has been read. Your saved data is available.";
        string next = "Choose a sync below to refresh saved data, or Skip for now to continue.";
        var tone = SyncStatusTone.Neutral;
        bool choicePrompt = false;
        bool running = _presetNamesCts is not null || _connectionCts is not null;
        bool indeterminate = _connectionCts is not null;
        bool libraryFlow = false;
        double maximum = _syncReadTotal;
        double value = _syncReadCompleted;
        if (checking)
        {
#if FRACTAL_INDEX
            title = _indexCheckingNames ? "Checking preset names first…" : _indexCheckingFull ? "Running full device check…" : "Running quick device check…";
            maximum = _indexCheckingNames ? 512 : Math.Max(1, _libraryMatchProgress?.Total ?? 1);
            value = _indexCheckingNames ? _syncReadCompleted : _libraryMatchProgress?.Checked ?? 0;
            detail = _indexCheckingNames ? $"{value:0} of 512 names read. An obvious mismatch stops the longer check."
                : $"{value:0} of {_libraryMatchProgress?.Total ?? 0} {(_indexCheckingFull ? "preset slots" : "random presets")} checked, including saved scenes.";
#endif
            next = "Please wait. Sync options will be available when the check finishes. Cancel read stops this check.";
            tone = SyncStatusTone.Activity;
        }
        else if (_syncingPresetNames || _syncingFavoriteScenes)
        {
            title = _syncingFavoriteScenes ? "Syncing favorite scene names…" : "Syncing preset names…";
            detail = _syncingFavoriteScenes ? $"{value:0} of {maximum:0} favorite presets read." : $"{value:0} of {maximum:0} preset slots checked.";
            next = "Please wait. Completed reads are kept if you cancel. The refresh actions will be available when this finishes.";
            tone = SyncStatusTone.Activity;
        }
#if FRACTAL_INDEX
        else if (_indexScanning)
        {
            title = $"Syncing “{_indexCache?.Device.Name}”…";
            detail = (_indexProgressText?.Text ?? "Reading presets, scenes and amps.") +
                "\nThis may take several minutes. Each preset can take a few seconds. Stop sync keeps completed reads.";
            next = "";
            libraryFlow = true;
            maximum = _indexProgress?.Maximum ?? 1;
            value = _indexProgress?.Value ?? 0;
            indeterminate = _indexProgress?.IsIndeterminate == true || _indexResumeChecking && value == 0;
            tone = SyncStatusTone.Activity;
        }
#endif
        else if (_connectionCts is not null)
        {
            title = "Reading device information…";
            detail = "Preparing the connection.";
            next = "Please wait for the sync options to become available.";
            tone = SyncStatusTone.Activity;
        }
#if FRACTAL_INDEX
        else if (!(_connectionLibrarySetupRequired &&
            (_connectionLibraryVariant is null || _connectionDefaultLibrary is not null && _connectionLibraryCandidate is null)) &&
            _indexCache is { LastAttempt: { Status: not "Complete" } partial } recovery)
        {
            int total = FractalDeviceDefinition.For(recovery.Device).PresetSlots;
            int remaining = Math.Max(0, total - partial.Presets.Count);
            title = remaining == 1 ? "Sync incomplete — 1 slot remaining" : $"Sync incomplete — {remaining} slots remaining";
            detail = $"{partial.Presets.Count} of {total} slots saved to “{recovery.Device.Name}”. Resume checks the saved reads, then reads missing presets and rechecks empty slots." +
                (recovery.Committed is null ? "" : " Your previous complete saved scan is kept until the sync finishes and you accept the update.");
            next = "";
            tone = SyncStatusTone.Attention;
            libraryFlow = true;
        }
#endif
        else if (_connectionSyncOutcome is { } outcome && outcome.Context == ConnectionSyncContext)
        {
            title = outcome.Title; detail = outcome.Detail; next = outcome.Next; tone = outcome.Tone;
        }
#if FRACTAL_INDEX
        else if (_connectionLibrarySetupRequired)
        {
            bool chooseTarget = _connectionDefaultLibrary is not null && _connectionLibraryCandidate is null;
            title = chooseTarget ? "Choose where to save this device" : "Ready to sync device";
            detail = _connectionLibrarySetupError is { } setupError ? "Device setup could not finish: " + setupError :
                chooseTarget ? "Choose where to save this device’s presets, scenes and amps." :
                $"Read all preset slots, scenes and amps into “{_indexCache?.Device.Name ?? "Default"}”. This may take several minutes.";
            next = "";
            tone = _connectionLibrarySetupError is null ? SyncStatusTone.Neutral : SyncStatusTone.Attention;
            libraryFlow = true;
        }
        else if (_indexCache is null)
        {
            detail = "Device information has been read. This profile has no assigned device.";
            next = "Sync names below, or use Manage devices to assign a device for presets, scenes and amps.";
        }
        else if (_indexCache.Device.Variant.ToDeviceModel() != _detectedDevice?.Model)
        {
            title = "Selected device model differs";
            detail = $"“{_indexCache.Device.Name}” does not use the connected device’s model. Saved device data has not been refreshed.";
            next = "Use Manage devices to select the intended device for this profile.";
            tone = SyncStatusTone.Attention;
        }
        else if (_indexCache.Committed is null)
        {
            detail = "Device information has been read. This device has no complete saved scan to compare.";
            next = _indexCache.LastAttempt is { Status: not "Complete" }
                ? "Choose Resume to finish the saved scan, or Skip for now to continue with saved data."
                : "Choose Sync device to read presets, scenes and amp data for the first time. This can take several minutes.";
        }
        else
        {
            choicePrompt = _showConnectionSyncChecks;
            title = choicePrompt ? "Check that you’re using the right device" : "Refresh saved data";
            detail = $"Compare this {_detectedDevice!.ModelLabel} with “{_indexCache.Device.Name}” — the presets, scenes and amp data saved in PresetMaestro.";
            next = "";
        }
#endif
#if FRACTAL_INDEX
        libraryFlow |= _connectionLibrarySetupRequired;
#endif
        if (!CanReadDeviceNames && !running)
        {
            detail += " " + UnsupportedNameReads;
        }
        _connectionSyncStatusTitle!.Text = title;
        _connectionSyncStatus!.Text = detail;
        _connectionSyncNext!.Text = next.StartsWith("Next: ", StringComparison.Ordinal) ? next[6..] : next;
        _connectionSyncNextHeading!.Text = running ? "While this runs" : "Next step";
        _connectionSyncNextPanel!.IsVisible = !libraryFlow && !choicePrompt && !string.IsNullOrWhiteSpace(next);
        _connectionSyncStatusTitle.FontSize = choicePrompt ? 20 : 18;
        _connectionSyncStatus.Foreground = choicePrompt ? SecondaryBrush : TextBrush;
        _connectionSyncStatusPanel!.Padding = choicePrompt ? new(0) : running ? new(16) : new(12);
        _connectionSyncStatusPanel.BorderThickness = choicePrompt ? new(0) : new(4, 0, 0, 0);
        IBrush accent = tone switch
        {
            SyncStatusTone.Success => ThemeBrush("SendSuccessForegroundBrush"),
            SyncStatusTone.Attention => ThemeBrush("SendWarningForegroundBrush"),
            _ => AccentBrush,
        };
        _connectionSyncStatusTitle.Foreground = tone is SyncStatusTone.Activity or SyncStatusTone.Neutral ? TextBrush : accent;
        _connectionSyncStatusPanel!.BorderBrush = accent;
        _connectionSyncStatusPanel.Background = choicePrompt ? Brushes.Transparent : tone switch
        {
            SyncStatusTone.Activity => ThemeBrush("SelectedBrush"),
            SyncStatusTone.Success => ThemeBrush("SendSuccessBackgroundBrush"),
            SyncStatusTone.Attention => ThemeBrush("SendWarningBackgroundBrush"),
            _ => InsetBrush,
        };
        _connectionSyncProgress!.IsVisible = running;
        _connectionSyncProgress.IsIndeterminate = indeterminate;
        _connectionSyncProgress.Maximum = maximum;
        _connectionSyncProgress.Value = value;
#if FRACTAL_INDEX
        RefreshLibraryMatchDetails(running);
#endif
    }

    private void SetSyncOptionDescription(string name, string text)
    {
        if (_connectionSyncDescriptions.TryGetValue(name, out var label)) { label.Text = text; }
    }

    private static void SetSyncOptionVisible(Button button, bool visible)
    {
        if (button.Parent is Grid row && row.Parent is Border border) { border.IsVisible = visible; }
    }

    internal async Task SyncFavoriteSceneNamesAsync()
    {
        if (!CanSyncPresetNames || _syncingFavoriteScenes) { return; }
        var slots = _favorites.Where(f => !f.IsEmpty).Select(f => f.Preset - _settings.DisplayOffset).Where(s => s is >= 0 and <= 511).Distinct().Order().ToArray();
        if (slots.Length == 0) { return; }
        using var cancellation = new CancellationTokenSource();
        _connectionSyncOutcome = null;
        _presetNamesCts = cancellation; _syncingFavoriteScenes = true;
        _sceneCts?.Cancel(); _scenePollCts?.Cancel(); _favoriteSceneCts?.Cancel();
        string key = _sceneCacheKey;
        _syncReadTotal = slots.Length; _syncReadCompleted = 0;
        UpdatePresetSyncButtons();
        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var result = await _queryStoredScenesAsync(slots[i], cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (result.Slot != slots[i]) { throw new InvalidDataException("The device returned a different preset."); }
                CacheScenes(result, "stored", key); _saveSettings(_settings);
                _syncReadCompleted = i + 1;
                RefreshConnectionSyncOptions();
            }
            SetConnectionSyncOutcome("Favorite scene names synced", $"Scene names refreshed for {slots.Length} presets used by this profile’s favorites.", "Next: Choose Done to continue. Use Sync device if you also changed preset content or amps.");
        }
        catch (OperationCanceledException)
        {
            SetConnectionSyncOutcome("Scene-name sync cancelled", $"{_syncReadCompleted} of {slots.Length} presets refreshed. Completed reads were saved.", "Next: Choose Sync scenes to retry, or Done to use the saved names.", needsAttention: true);
        }
        catch (Exception ex)
        {
            SetConnectionSyncOutcome("Scene-name sync stopped", $"{_syncReadCompleted} of {slots.Length} presets refreshed. Completed reads were saved. {ex.Message}", "Next: Check the MIDI connection, then choose Sync scenes to retry.", needsAttention: true);
        }
        finally
        {
            _presetNamesCts = null; _syncingFavoriteScenes = false;
            PopulateFavoriteScenes(); UpdatePresetSyncButtons();
        }
    }
}
