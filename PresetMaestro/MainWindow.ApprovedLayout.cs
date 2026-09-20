using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace PresetMaestro;

public partial class MainWindow
{
    private static IBrush AppBrush => ThemeBrush("AppBrush");
    private static IBrush SurfaceBrush => ThemeBrush("SurfaceBrush");
    private static IBrush InsetBrush => ThemeBrush("InsetBrush");
    private static IBrush UiBorderBrush => ThemeBrush("UiBorderBrush");
    private static IBrush AccentBrush => ThemeBrush("AccentBrush");
    private static IBrush TextBrush => ThemeBrush("TextBrush");
    private static IBrush SecondaryBrush => ThemeBrush("SecondaryBrush");
    private static IBrush SuccessBrush => ThemeBrush("SuccessBrush");
    private static IBrush DangerBrush => ThemeBrush("DangerBrush");
    private static IBrush TagBrush => ThemeBrush("TagBrush");
    private static IBrush TagTextBrush => ThemeBrush("TagTextBrush");
    private static IBrush TagRemoveBrush => ThemeBrush("TagRemoveBrush");
    private static readonly BoxShadows Elevation = new(new BoxShadow { OffsetY = 1, Blur = 3, Color = Color.Parse("#180E1A25") });
    private static readonly Bitmap AppLogo = LoadAppLogo();

    private static Bitmap LoadAppLogo()
    {
        using Stream iconStream = AssetLoader.Open(new Uri("avares://PresetMaestro/preset_maestro.ico"));
        return new Bitmap(iconStream);
    }

    private static void ApplyThemePalette()
    {
        bool light = Application.Current!.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light;
        var palette = light
            ? ("#E9EDF0", "#FFFFFF", "#F4F6F8", "#CDD5DC", "#1969B0", "#17232D", "#5F6B75", "#278A59", "#C84444")
            : ("#11151B", "#181D24", "#11161D", "#303844", "#2F80ED", "#F2F5F8", "#A7B0BC", "#2F80ED", "#EC6C68");
        var resources = Application.Current.Resources;
        resources["AppBrush"] = Brush(palette.Item1);
        resources["SurfaceBrush"] = Brush(palette.Item2);
        resources["InsetBrush"] = Brush(palette.Item3);
        resources["UiBorderBrush"] = Brush(palette.Item4);
        resources["AccentBrush"] = Brush(palette.Item5);
        resources["TextBrush"] = Brush(palette.Item6);
        resources["SecondaryBrush"] = Brush(palette.Item7);
        resources["SuccessBrush"] = Brush(palette.Item8);
        resources["DangerBrush"] = Brush(palette.Item9);
        resources["HoverBrush"] = Brush(light ? "#F3F7FB" : "#202936");
        resources["SelectedBrush"] = Brush(light ? "#EAF4FE" : "#244D87");
        resources["BadgeBrush"] = Brush(light ? "#EDF1F5" : "#3B4653");
        resources["SelectedBadgeBrush"] = Brush(light ? "#DCEEFF" : "#2F80ED");
        resources["RowSeparatorBrush"] = Brush(light ? "#E7EDF3" : "#2A313B");
        resources["TagBrush"] = Brush(light ? "#EAF1FA" : "#252E3A");
        resources["TagTextBrush"] = Brush(light ? "#536B8C" : "#C4D1E3");
        resources["TagRemoveBrush"] = Brush(light ? "#7F91AA" : "#8493A8");
    }

    private void BuildLayout()
    {
        Background = AppBrush;
        var sender = BuildApprovedSender();
        var favorites = BuildApprovedFavorites();
        AttachFavoritePresetPicker();
        AttachFavoriteScenePicker();
        var diagnostics = BuildApprovedDiagnosticsPage();
        var host = new ContentControl();
        var config = BuildApprovedConfig(() => { _currentPage = AppPage.Diagnostics; host.Content = diagnostics; });
        host.Content = _currentPage switch
        {
            AppPage.Favorites => favorites,
            AppPage.Config => config,
            AppPage.Diagnostics => diagnostics,
            _ => sender,
        };
        SetMode(_mode);
        var root = new Grid { RowDefinitions = new RowDefinitions("64,*") };
        root.Children.Add(ApprovedHeader(host, sender, favorites, config));
        Grid.SetRow(host, 1);
        root.Children.Add(host);
        var deleteSlotDialog = BuildDeleteSlotDialog();
        Grid.SetRowSpan(deleteSlotDialog, 2);
        root.Children.Add(deleteSlotDialog);
        Content = root;
        SetStatus(_statusText, _statusKind);
    }

    private Control ApprovedHeader(ContentControl host, Control sender, Control favorites, Control config)
    {
        var header = new Border { Padding = new Thickness(24, 0), Background = AppBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(0, 0, 0, 1) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var identity = new StackPanel { Name = "AppIdentity", Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(new Image { Name = "AppLogo", Source = AppLogo, Width = 32, Height = 32, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        identity.Children.Add(new TextBlock { Name = "AppTitle", Text = "Preset Maestro", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(identity);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        Button Nav(string label, Control page, AppPage pageKind, EntryMode? entryMode = null)
        {
            bool active = _currentPage == pageKind;
            double width = label switch { "Preset Sender" => 132, "Favorites" => 96, "Config" => 76, _ => 0 };
            var button = new Button { Name = $"Nav{label.Replace(" ", string.Empty)}", Content = label, Width = width, Height = 38, MinHeight = 38, Padding = new Thickness(12, 0), FontSize = 14, FontWeight = FontWeight.Medium, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(0), Background = active ? AccentBrush : Brushes.Transparent, Foreground = active ? Brushes.White : TextBrush };
            button.Click += (_, _) =>
            {
                _currentPage = pageKind;
                host.Content = page;
                if (entryMode.HasValue)
                {
                    SetMode(entryMode.Value);
                }

                foreach (var child in nav.Children.OfType<Button>()) { child.Background = Brushes.Transparent; child.Foreground = TextBrush; }
                button.Background = AccentBrush;
                button.Foreground = Brushes.White;
            };
            return button;
        }
        nav.Children.Add(Nav("Preset Sender", sender, AppPage.PresetSender, EntryMode.Preset));
        nav.Children.Add(Nav("Favorites", favorites, AppPage.Favorites, EntryMode.Favorite));
        nav.Children.Add(Nav("Config", config, AppPage.Config));
        var navigation = new Border { Height = 46, Padding = new Thickness(3), Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = nav };
        Grid.SetColumn(navigation, 1); grid.Children.Add(navigation);
        _headerStatusLabel = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var state = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Child = _headerStatusLabel };
        Grid.SetColumn(state, 2); grid.Children.Add(state); header.Child = grid; return header;
    }

    private Control BuildApprovedSender()
    {
        var page = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("390,*") };
        var left = new StackPanel { Spacing = 16 };
        _senderStatusLabel = new TextBlock { Foreground = SecondaryBrush };
        left.Children.Add(_senderStatusLabel);
        left.Children.Add(ApprovedDisplay()); left.Children.Add(ApprovedKeypad()); page.Children.Add(left);
        return page;
    }

    private Control BuildApprovedDiagnosticsPage()
    {
        var page = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var diagnostics = ApprovedDiagnostics();
        page.Children.Add(diagnostics);
        var log = ApprovedLog();
        Grid.SetColumn(log, 2);
        page.Children.Add(log);
        return page;
    }

    private Control ApprovedDisplay()
    {
        _displayLabel = new TextBlock { Text = "---", FontFamily = new FontFamily("Bahnschrift"), FontSize = 56, FontWeight = FontWeight.Bold, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, LetterSpacing = 2 };
        _currentPresetNameLabel = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = SecondaryBrush };
        _currentSceneNameLabel = new TextBlock { FontSize = 13, Foreground = SecondaryBrush };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _activePresetStatusLabel = new TextBlock { FontSize = 11, FontWeight = FontWeight.Bold };
        Grid.SetColumn(_activePresetStatusLabel, 1);
        grid.Children.Add(_activePresetStatusLabel);
        Grid.SetRow(_displayLabel, 1); Grid.SetColumnSpan(_displayLabel, 2); grid.Children.Add(_displayLabel);
        Grid.SetRow(_currentPresetNameLabel, 2); Grid.SetColumnSpan(_currentPresetNameLabel, 2); grid.Children.Add(_currentPresetNameLabel);
        Grid.SetRow(_currentSceneNameLabel, 3); Grid.SetColumnSpan(_currentSceneNameLabel, 2); grid.Children.Add(_currentSceneNameLabel);
        return new Border { Height = 170, Background = InsetBrush, BorderBrush = AccentBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(20), BoxShadow = Elevation, Child = grid };
    }

    private Control ApprovedKeypad()
    {
        var deck = new Border { Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8), BoxShadow = Elevation };
        var stack = new StackPanel { Spacing = 7 }; stack.Children.Add(new TextBlock { Text = "PRESET ENTRY", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush, Margin = new Thickness(5, 1, 0, 0) });
        var grid = new UniformGrid { Rows = 5, Columns = 3 };
        foreach (var (label, action) in new (string, Action)[] { ("1", () => HandleDigit(1)), ("2", () => HandleDigit(2)), ("3", () => HandleDigit(3)), ("4", () => HandleDigit(4)), ("5", () => HandleDigit(5)), ("6", () => HandleDigit(6)), ("7", () => HandleDigit(7)), ("8", () => HandleDigit(8)), ("9", () => HandleDigit(9)), ("CLR", HandleClear), ("0", () => HandleDigit(0)), ("SEND", HandleSend), ("PREV", HandlePrev), ("LAST", HandleLast), ("NEXT", HandleNext) })
        {
            grid.Children.Add(ApprovedKey(label, action));
        }

        stack.Children.Add(grid); deck.Child = stack; return deck;
    }

    private static Control ApprovedKey(string label, Action action)
    {
        bool send = label == "SEND"; bool command = label is "CLR" or "PREV" or "LAST" or "NEXT";
        var housing = new Border { Background = send ? AccentBrush : UiBorderBrush, CornerRadius = new CornerRadius(7), Padding = new Thickness(1, 1, 1, 2), Margin = new Thickness(3), BoxShadow = Elevation };
        var face = new Border { Background = send ? AccentBrush : SurfaceBrush, CornerRadius = new CornerRadius(6) };
        var grid = new Grid { MinHeight = 52 };
        grid.Children.Add(new TextBlock { Text = label, Foreground = send ? Brushes.White : label == "CLR" ? DangerBrush : TextBrush, FontFamily = new FontFamily(command ? "Segoe UI Variable" : "Bahnschrift"), FontSize = command ? 12 : send ? 16 : 20, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var hit = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        hit.Click += (_, _) => action(); grid.Children.Add(hit); face.Child = grid; housing.Child = face; return housing;
    }

    private Control ApprovedEntryOptions()
    {
        var card = ApprovedCard(); var stack = new StackPanel { Spacing = 12 }; stack.Children.Add(new TextBlock { Text = "ENTRY OPTIONS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush });
        var auto = new Grid { ColumnDefinitions = new ColumnDefinitions("*,130,Auto") }; _autoSendCheck = new CheckBox { Content = "Auto-send after 3 digits", VerticalAlignment = VerticalAlignment.Center }; _autoSendCheck.IsCheckedChanged += (_, _) => _settings.AutoSend = _autoSendCheck.IsChecked == true; auto.Children.Add(_autoSendCheck); _autoSendDelaySpinner = new NumericUpDown { Minimum = 10, Maximum = 2000, Value = 35, FormatString = "0" }; _autoSendDelaySpinner.ValueChanged += (_, _) => { _settings.AutoSendDelayMs = (int)(_autoSendDelaySpinner.Value ?? 35); _autoSendTimer.Interval = TimeSpan.FromMilliseconds(_settings.AutoSendDelayMs); }; Grid.SetColumn(_autoSendDelaySpinner, 1); auto.Children.Add(_autoSendDelaySpinner); var unit = new TextBlock { Text = "ms", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = SecondaryBrush }; Grid.SetColumn(unit, 2); auto.Children.Add(unit); stack.Children.Add(auto);
        var entryOptions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        _keyboardEntryCheck = new CheckBox { Content = "Enable keyboard entry" }; _keyboardEntryCheck.IsCheckedChanged += (_, _) => _settings.KeyboardEntryEnabled = _keyboardEntryCheck.IsChecked == true; entryOptions.Children.Add(_keyboardEntryCheck);
        _midiEntryCheck = new CheckBox { Content = "Enable MIDI note entry" }; _midiEntryCheck.IsCheckedChanged += (_, _) => _settings.MidiEntryEnabled = _midiEntryCheck.IsChecked == true; Grid.SetColumn(_midiEntryCheck, 1); entryOptions.Children.Add(_midiEntryCheck);
        stack.Children.Add(entryOptions);
        card.Child = stack; return card;
    }

    private Control ApprovedDiagnostics()
    {
        var card = ApprovedCard("Signal Diagnostics", "Live translation preview"); card.Height = 500; var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
        (string, TextBlock)[] rows = [("Last Rx note", _diagLastRxLabel = Value()), ("Entered", _diagEnteredLabel = Value()), ("Favorite", _diagFavoriteLabel = Value()), ("device display", _diagDeviceDisplayLabel = Value()), ("MIDI preset", _diagMidiPresetLabel = Value()), ("Bank select CC#0", _diagBankLabel = Value()), ("Program change", _diagPcLabel = Value()), ("Scene", _diagSceneLabel = Value()), ("MIDI channel", _diagChannelLabel = Value())];
        for (int row = 0; row < rows.Length; row++) { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var label = new TextBlock { Text = rows[row].Item1, Foreground = SecondaryBrush, Margin = new Thickness(0, 7) }; Grid.SetRow(label, row); Grid.SetRow(rows[row].Item2, row); Grid.SetColumn(rows[row].Item2, 1); grid.Children.Add(label); grid.Children.Add(rows[row].Item2); }
        var test = new Button { Content = "Test Translation", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) }; test.Click += OnTestTranslation; Grid.SetRow(test, rows.Length); Grid.SetColumn(test, 1); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.Children.Add(test); SetApprovedCardContent(card, grid); return card;
    }

    private Control ApprovedLog()
    {
        var card = ApprovedCard("MIDI Log", "Device activity");
        card.Height = 500;
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        _logTextBox = CreateSelectableLog("Cascadia Mono", 12);
        layout.Children.Add(_logTextBox);
        var clear = new Button { Name = "ClearMidiLog", Content = "Clear Log", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        clear.Click += (_, _) => ClearLog();
        Grid.SetRow(clear, 1);
        layout.Children.Add(clear);
        SetApprovedCardContent(card, layout);
        return card;
    }

    private Control BuildApprovedFavorites()
    {
        _favoritesPage = new Grid { Margin = new Thickness(20), ColumnDefinitions = new ColumnDefinitions("252,12,*") };
        _favCategoryTree = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemContainerTheme = CompactListItemTheme() };
        _favCategoryTree.SelectionChanged += OnFavCategorySelectionChanged;
        _favCategoryTree.AddHandler(InputElement.PointerPressedEvent, OnFavCategoryPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        _favCategoryTree.AddHandler(InputElement.PointerMovedEvent, OnFavCategoryPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        _favCategoryTree.AddHandler(InputElement.PointerReleasedEvent, OnFavCategoryPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _favCategoryTree.AddHandler(InputElement.PointerCaptureLostEvent, OnFavCategoryPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        var categories = ApprovedCard("Collections", "Favorites", titleFontWeight: FontWeight.Bold);
        categories.Padding = new Thickness(16, 17);
        SetApprovedCardContent(categories, _favCategoryTree); _favoritesPage.Children.Add(categories);
        var library = BuildApprovedFavoriteLibrary(); Grid.SetColumn(library, 2); _favoritesPage.Children.Add(library); BuildApprovedFavoriteEditor(); return _favoritesPage;
    }

    private Control BuildApprovedFavoriteLibrary()
    {
        _favoritesDisplayLabel = new TextBlock { Text = "---", FontFamily = new FontFamily("Bahnschrift"), FontSize = 24, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var card = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16, 11, 12, 17), BoxShadow = Elevation };
        var content = new StackPanel { Spacing = 11 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = "Favorites",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = TextBrush,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0)
        });
        _favNewBtn = new Button
        {
            Name = "FavoriteAdd",
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new PathIcon
                    {
                        Name = "FavoriteAddGlyph",
                        Data = Geometry.Parse("M7,0 H9 V7 H16 V9 H9 V16 H7 V9 H0 V7 H7 Z"),
                        Width = 12,
                        Height = 12,
                        Foreground = AccentBrush,
                    },
                    new TextBlock
                    {
                        Text = "New",
                        Foreground = AccentBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
            Background = InsetBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(10, 0),
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _favNewBtn.Click += OnFavNew;
        header.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        actions.Children.Add(_favNewBtn);
        actions.Children.Add(new Border { Width = 84, Height = 36, Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Child = _favoritesDisplayLabel });
        _favDetailsToggleIcon = new PathIcon
        {
            Width = 16,
            Height = 16,
            Foreground = SecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _favDetailsToggleBtn = new Button
        {
            Name = "FavoriteDetailsToggle",
            Content = _favDetailsToggleIcon,
            Width = 32,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(0),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        UpdateFavoriteDetailsTogglePresentation();
        _favDetailsToggleBtn.Click += (_, _) =>
        {
            _showFavoriteDetailsForAll = !_showFavoriteDetailsForAll;
            UpdateFavoriteDetailsTogglePresentation();
            UpdateFavoriteRowStates();
        };
        actions.Children.Add(_favDetailsToggleBtn);
        var clear = new Button { Name = "FavoriteClearCommand", Content = "CLR", Width = 50, Height = 32, MinHeight = 32, Padding = new Thickness(8, 0), FontSize = 12, Foreground = DangerBrush, FontWeight = FontWeight.SemiBold };
        clear.Click += (_, _) => HandleClear();
        actions.Children.Add(clear);
        var send = new Button { Name = "FavoriteSendCommand", Content = "SEND", Width = 58, Height = 32, MinHeight = 32, Padding = new Thickness(8, 0), FontSize = 12, Background = AccentBrush, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
        send.Click += OnFavSend;
        actions.Children.Add(send);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        content.Children.Add(header);
        var layout = new Grid { RowDefinitions = new RowDefinitions("40,32,*") };
        _favSearchBox = new TextBox { Watermark = "Search name / category / tag...", MinHeight = 34, Padding = new Thickness(14, 0, 36, 0), CornerRadius = new CornerRadius(5) };
        _favSearchBox.TextChanged += OnFavFilterChanged;
        var search = new Grid(); search.Children.Add(_favSearchBox); search.Children.Add(new PathIcon { Data = Geometry.Parse("M9.5,3 C5.91,3 3,5.91 3,9.5 C3,13.09 5.91,16 9.5,16 C10.9,16 12.2,15.55 13.25,14.78 L18.47,20 L20,18.47 L14.78,13.25 C15.55,12.2 16,10.9 16,9.5 C16,5.91 13.09,3 9.5,3 Z"), Width = 17, Height = 17, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false }); layout.Children.Add(search);
        var headings = BuildFavColumnGrid(FavColumns.Select((column, index) => { var button = new Button { Content = column.Header, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(11, 0), Foreground = SecondaryBrush, FontSize = 11, FontWeight = FontWeight.Normal, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 0 }; button.Click += (_, _) => OnFavColumnClick(index); return (Control)button; }).ToArray()); Grid.SetRow(headings, 1); layout.Children.Add(headings);
        _favListBox = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemContainerTheme = CompactListItemTheme() }; _favListBox.SelectionChanged += OnFavListSelectionChanged; _favListBox.DoubleTapped += OnFavListDoubleClick; _favListBox.AddHandler(InputElement.PointerPressedEvent, OnFavListPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true); _favListBox.AddHandler(InputElement.PointerMovedEvent, OnFavListPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true); _favListBox.AddHandler(InputElement.PointerReleasedEvent, OnFavListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true); _favListBox.AddHandler(InputElement.PointerCaptureLostEvent, OnFavListPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true); Grid.SetRow(_favListBox, 2); layout.Children.Add(_favListBox);
        _favSendBtn = new Button { IsVisible = false }; _favSendBtn.Click += OnFavSend;
        content.Children.Add(layout);
        card.Child = content;
        return card;
    }

    private Control BuildApprovedFavoriteEditor()
    {
        _favSyncPresetsButton = new Button
        {
            Name = "FavoritePresetSync",
            Content = "↻ Sync",
            MinHeight = 38,
            Background = SurfaceBrush,
            Foreground = TextBrush,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(_favSyncPresetsButton, "Sync presets");
        _favSyncPresetsButton.Click += async (_, _) => await SyncPresetNamesAsync();

        var newFavorite = new Button
        {
            Name = "FavoriteNew",
            Content = "New Favorite",
            Background = AccentBrush,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            MinHeight = 38,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        newFavorite.Click += OnFavNew;

        _favEditorCard = ApprovedCard();
        _favEditorCard.Name = "FavoriteEditorCard";
        _favEditorCard.IsVisible = false;
        var cardLayout = new Grid { RowDefinitions = new RowDefinitions("Auto,16,*") };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(newFavorite);
        Grid.SetColumn(_favSyncPresetsButton, 1);
        heading.Children.Add(_favSyncPresetsButton);
        var body = new ContentControl();
        cardLayout.Children.Add(heading);
        Grid.SetRow(body, 2);
        cardLayout.Children.Add(body);
        _favEditorCard.Child = cardLayout;
        _favEditorCard.Tag = body;
        var container = new Panel();
        _favEditorPlaceholder = new TextBlock { Text = "Select a favorite to edit, or create a new favorite.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush };
        container.Children.Add(_favEditorPlaceholder);
        var stack = new StackPanel { Spacing = 12 };
        _favEditorTitle = new TextBlock { Text = "New Favorite", FontSize = 16, FontWeight = FontWeight.Bold };
        stack.Children.Add(_favEditorTitle);
        _favNameBox = new TextBox { Name = "FavoriteName" };
        _favCategoryBox = new AutoCompleteBox { Name = "FavoriteCategory", FilterMode = AutoCompleteFilterMode.Contains };
        _favTagsPanel = new WrapPanel { Orientation = Orientation.Horizontal, ItemHeight = 24, ItemWidth = double.NaN, VerticalAlignment = VerticalAlignment.Center };
        _favTagInput = new TextBox { Name = "FavoriteTagInput", Watermark = "Add tag...", BorderThickness = new Thickness(0), Background = Brushes.Transparent, MinWidth = 70, MinHeight = 24, Padding = new Thickness(2, 0) };
        _favTagInput.GotFocus += (_, _) => _favTagsEditor.BorderBrush = AccentBrush;
        _favTagInput.LostFocus += (_, _) => _favTagsEditor.BorderBrush = UiBorderBrush;
        _favTagInput.KeyDown += OnFavTagInputKeyDown;
        _favTagInput.TextChanged += OnFavTagInputTextChanged;
        _favTagsPanel.Children.Add(_favTagInput);
        _favTagsEditor = new Border { Background = InsetBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 5), MinHeight = 38, Child = _favTagsPanel };
        _favTagsEditor.PointerPressed += (_, _) => _favTagInput.Focus();
        _favPresetSpinner = new NumericUpDown { Name = "FavoritePreset", Minimum = 0, Maximum = 512, FormatString = "0" };
        _favSceneSpinner = new NumericUpDown { Name = "FavoriteScene", Minimum = 1, Maximum = 8, FormatString = "0" };
        foreach (var field in new (string, Control)[] { ("Name", _favNameBox), ("Category", _favCategoryBox), ("Tags", _favTagsEditor) })
        {
            stack.Children.Add(ApprovedField(field.Item1, field.Item2));
        }

        // The displayed values are transformed into picker buttons after the editor is built.
        var presetField = (StackPanel)ApprovedField("Preset", _favPresetSpinner);
        var sceneField = (StackPanel)ApprovedField("Scene", _favSceneSpinner);
        _favPickerActions = new StackPanel { Spacing = 8 };
        stack.Children.Add(presetField);
        stack.Children.Add(sceneField);
        stack.Children.Add(_favPickerActions);

        var actions = new Grid { Name = "FavoriteActions", ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*") };
        _favSaveBtn = new Button { Name = "FavoriteSave", Content = "Save", Background = AccentBrush, Foreground = Brushes.White, MinHeight = 38, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch };
        _favSaveBtn.Click += OnFavSave;
        _favCancelBtn = new Button { Name = "FavoriteCancel", Content = "Cancel", MinHeight = 38, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch };
        _favCancelBtn.Click += OnFavCancel;
        _favDeleteBtn = new Button { Name = "FavoriteDelete", Content = "Delete", Background = DangerBrush, Foreground = Brushes.White, MinHeight = 38, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Stretch };
        _favDeleteBtn.Click += OnFavDelete;
        actions.Children.Add(_favSaveBtn);
        Grid.SetColumn(_favCancelBtn, 2);
        actions.Children.Add(_favCancelBtn);
        Grid.SetColumn(_favDeleteBtn, 4);
        actions.Children.Add(_favDeleteBtn);

        stack.Children.Add(actions);
        _favEditorPanel = new Panel { IsVisible = false, Children = { stack } };
        container.Children.Add(_favEditorPanel);
        SetApprovedCardContent(_favEditorCard, container);
        UpdatePresetSyncButtons();
        return _favEditorCard;
    }

    private Control BuildApprovedConfig(Action showDiagnostics)
    {
        var page = new Grid { Margin = new Thickness(24) };
        var connection = new StackPanel { Spacing = 16, Children = { BuildApprovedConnection(showDiagnostics), BuildPresetNameSyncCard(), ApprovedEntryOptions() } };
        var mapping = new StackPanel { Spacing = 16, Children = { BuildApprovedMapping(), BuildApprovedAppearance() } };
        page.Children.Add(connection); page.Children.Add(mapping);

        void Arrange()
        {
            bool compact = page.Bounds.Width < 980;
            page.ColumnDefinitions.Clear(); page.RowDefinitions.Clear();
            if (compact)
            {
                page.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                page.RowDefinitions.Add(new RowDefinition(new GridLength(16)));
                page.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                page.RowDefinitions.Add(new RowDefinition(new GridLength(16)));
                page.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Grid.SetColumn(connection, 0); Grid.SetRow(connection, 0);
                Grid.SetColumn(mapping, 0); Grid.SetRow(mapping, 2);
            }
            else
            {
                page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.2, GridUnitType.Star)));
                page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
                page.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                page.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
                Grid.SetColumn(connection, 0); Grid.SetRow(connection, 0);
                Grid.SetColumn(mapping, 2); Grid.SetRow(mapping, 0);
            }
        }

        page.SizeChanged += (_, _) => Arrange();
        Arrange();
        return new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private Control BuildApprovedConnection(Action showDiagnostics)
    {
        var card = ApprovedCard("MIDI Connection", "Hardware and routing"); var stack = new StackPanel { Spacing = 14 }; _statusLabel = new TextBlock { Text = "●  Not connected", Foreground = SecondaryBrush, FontWeight = FontWeight.Bold }; stack.Children.Add(_statusLabel); _inputPortCombo = new ComboBox(); _inputPortCombo.SelectionChanged += (_, _) => RefreshThruInputOptions(); _outputPortCombo = new ComboBox(); _outputPortCombo.SelectionChanged += (_, _) => RefreshThruInputOptions(); _thruInputPanel = new StackPanel { Spacing = 4 }; var ports = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; var routing = new StackPanel { Spacing = 14, Children = { ApprovedField("MIDI In", _inputPortCombo), ApprovedField("MIDI Out", _outputPortCombo) } }; ports.Children.Add(routing); var thru = ApprovedField("Thru In(s)", _thruInputPanel); Grid.SetColumn(thru, 2); ports.Children.Add(thru); stack.Children.Add(ports); var refresh = new Button { Content = "Refresh Devices", HorizontalAlignment = HorizontalAlignment.Left }; refresh.Click += (_, _) => RefreshPortLists(); stack.Children.Add(refresh); var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; _connectButton = new Button { Content = "Connect", Background = AccentBrush, Foreground = Brushes.White }; _connectButton.Click += (_, _) => Connect(); _disconnectButton = new Button { Content = "Disconnect", Foreground = DangerBrush }; _disconnectButton.Click += (_, _) => Disconnect(); buttons.Children.Add(_connectButton); buttons.Children.Add(_disconnectButton); stack.Children.Add(buttons); var debugOptions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; _debugCheck = new CheckBox { Content = "Debug mode (bypass channel filter)" }; _debugCheck.IsCheckedChanged += (_, _) => _settings.DebugMode = _debugCheck.IsChecked == true; debugOptions.Children.Add(_debugCheck); var settingsActions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right }; var diagnostics = new Button { Name = "OpenDiagnostics", Content = "Diagnostics", HorizontalAlignment = HorizontalAlignment.Stretch }; diagnostics.Click += (_, _) => showDiagnostics(); settingsActions.Children.Add(diagnostics); var openSettings = new Button { Name = "OpenSettingsJson", Content = "Open Settings JSON", HorizontalAlignment = HorizontalAlignment.Stretch }; openSettings.Click += (_, _) => OpenSettingsFile(); settingsActions.Children.Add(openSettings); Grid.SetColumn(settingsActions, 1); debugOptions.Children.Add(settingsActions); stack.Children.Add(debugOptions); SetApprovedCardContent(card, stack); return card;
    }

    private Control BuildApprovedMapping()
    {
        var card = ApprovedCard("Preset Mapping", "Translation settings"); var stack = new StackPanel { Spacing = 14 }; _channelCombo = new ComboBox(); _channelCombo.Items.Add("Omni"); for (int channel = 1; channel <= 16; channel++)
        {
            _channelCombo.Items.Add(channel.ToString());
        }

        _channelCombo.SelectionChanged += (_, _) => { if (_channelCombo.SelectedIndex >= 0) { _settings.MidiChannel = _channelCombo.SelectedIndex; } }; _offsetCombo = new ComboBox { ItemsSource = new[] { "0 (device mapping disabled)", "1 (display starts at 001)" } }; _offsetCombo.SelectionChanged += OnOffsetChanged; _maxPresetSpinner = new NumericUpDown { Minimum = 1, Maximum = 512, FormatString = "0" }; _maxPresetSpinner.ValueChanged += (_, _) => _settings.MaxDisplayedPreset = (int)(_maxPresetSpinner.Value ?? 511); _sceneCcSpinner = new NumericUpDown { Minimum = 0, Maximum = 127, FormatString = "0" }; _sceneCcSpinner.ValueChanged += (_, _) => _settings.SceneCc = (int)(_sceneCcSpinner.Value ?? 34); stack.Children.Add(ApprovedField("MIDI Channel", _channelCombo)); stack.Children.Add(ApprovedField("Display Offset", _offsetCombo)); stack.Children.Add(ApprovedField("Max Preset", _maxPresetSpinner)); stack.Children.Add(ApprovedField("Scene CC#", _sceneCcSpinner)); stack.Children.Add(new Border { Background = InsetBrush, Padding = new Thickness(14), CornerRadius = new CornerRadius(5), Child = new TextBlock { Text = "Program Change mapping must be disabled on the device when Display Offset is 0.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush } }); SetApprovedCardContent(card, stack); return card;
    }

    private Control BuildApprovedAppearance()
    {
        bool isLight = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        var card = ApprovedCard(); var stack = new StackPanel { Spacing = 12 }; stack.Children.Add(new TextBlock { Text = "APPEARANCE", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush }); var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; _darkThemeRadio = new RadioButton { Content = "Dark", IsChecked = !isLight }; _lightThemeRadio = new RadioButton { Content = "Light", IsChecked = isLight }; _darkThemeRadio.IsCheckedChanged += (_, _) => { if (_darkThemeRadio.IsChecked == true) { _lightThemeRadio.IsChecked = false; _settings.Theme = "Dark"; SetTheme("Dark"); } }; _lightThemeRadio.IsCheckedChanged += (_, _) => { if (_lightThemeRadio.IsChecked == true) { _darkThemeRadio.IsChecked = false; _settings.Theme = "Light"; SetTheme("Light"); } }; row.Children.Add(_darkThemeRadio); row.Children.Add(_lightThemeRadio); stack.Children.Add(row); card.Child = stack; return card;
    }

    private static Border ApprovedCard(string? title = null, string? subtitle = null, Control? headerRight = null, FontWeight? titleFontWeight = null)
    {
        var card = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(18), BoxShadow = Elevation };
        if (title is null)
        {
            return card;
        }

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,16,*") }; var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; var titleStack = new StackPanel { Spacing = 2 }; titleStack.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = titleFontWeight ?? FontWeight.Bold, Foreground = TextBrush }); if (subtitle is not null)
        {
            titleStack.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = SecondaryBrush });
        }

        heading.Children.Add(titleStack); if (headerRight is not null) { Grid.SetColumn(headerRight, 1); heading.Children.Add(headerRight); }
        var body = new ContentControl(); layout.Children.Add(heading); Grid.SetRow(body, 2); layout.Children.Add(body); card.Child = layout; card.Tag = body; return card;
    }
    private static void SetApprovedCardContent(Border card, Control content) => ((ContentControl)card.Tag!).Content = content;
    private static ControlTheme CompactListItemTheme() => new(typeof(ListBoxItem)) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, 0d), new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch), new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent) } };
    private static Control ApprovedField(string label, Control input) => new StackPanel { Spacing = 5, Children = { new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush }, input } };
    private static TextBlock Value() => new() { Text = "---", FontFamily = new FontFamily("Cascadia Mono"), FontWeight = FontWeight.SemiBold, Foreground = TextBrush, Margin = new Thickness(0, 7) };
    private static IBrush ThemeBrush(string key) => (IBrush)Application.Current!.Resources[key]!;
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
