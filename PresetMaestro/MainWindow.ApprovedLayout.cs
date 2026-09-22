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
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;

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
    private TextBlock _deviceCapacityLabel = null!;

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
        var host = new ContentControl();
        Control config = null!;
        var diagnostics = BuildApprovedDiagnosticsPage(() =>
        {
            _currentPage = AppPage.Config;
            host.Content = config;
        });
        config = BuildApprovedConfig(() => { _currentPage = AppPage.Diagnostics; host.Content = diagnostics; });
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
        _headerStatusLabel = new TextBlock { Name = "HeaderConnectionStatus", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _connectionDot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Brushes.ForestGreen, VerticalAlignment = VerticalAlignment.Center, IsVisible = _statusKind == StatusKind.ConnectedBoth };
        var statusContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        statusContent.Children.Add(_connectionDot);
        statusContent.Children.Add(_headerStatusLabel);
        var state = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Child = statusContent };
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

    private Control BuildApprovedDiagnosticsPage(Action showConfig)
    {
        var page = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,16,*") };
        var back = new Button { Name = "BackToConfig", Content = "← Back", HorizontalAlignment = HorizontalAlignment.Left };
        back.Click += (_, _) => showConfig();
        page.Children.Add(back);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var diagnostics = ApprovedDiagnostics();
        content.Children.Add(diagnostics);
        var log = ApprovedLog();
        Grid.SetColumn(log, 2);
        content.Children.Add(log);
        Grid.SetRow(content, 2);
        page.Children.Add(content);
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
        var card = ApprovedCard("Entry Options", "Keyboard and MIDI entry behaviour");
        card.Name = "EntryOptionsCard";
        var stack = new StackPanel { Spacing = 14 };

        var auto = new Grid { Name = "AutoSendOptions" };
        _autoSendCheck = new CheckBox { Name = "AutoSend", Content = "Auto-send after 3 digits", VerticalAlignment = VerticalAlignment.Bottom };
        _autoSendCheck.IsCheckedChanged += (_, _) => _settings.AutoSend = _autoSendCheck.IsChecked == true;
        auto.Children.Add(_autoSendCheck);
        _autoSendDelaySpinner = new NumericUpDown { Name = "AutoSendDelay", Minimum = 10, Maximum = 2000, Value = 35, FormatString = "0", MinWidth = 110 };
        _autoSendDelaySpinner.ValueChanged += (_, _) => { _settings.AutoSendDelayMs = (int)(_autoSendDelaySpinner.Value ?? 35); _autoSendTimer.Interval = TimeSpan.FromMilliseconds(_settings.AutoSendDelayMs); };
        var delay = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        delay.Children.Add(_autoSendDelaySpinner);
        var unit = new TextBlock { Text = "ms", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = SecondaryBrush };
        Grid.SetColumn(unit, 1);
        delay.Children.Add(unit);
        var delayField = ApprovedField("Delay", delay);
        auto.Children.Add(delayField);
        stack.Children.Add(auto);

        stack.Children.Add(new Border { Height = 1, Background = UiBorderBrush });
        stack.Children.Add(new TextBlock { Text = "ENTRY SOURCES", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush });
        var entrySources = new Grid { Name = "EntrySources" };
        _keyboardEntryCheck = new CheckBox { Name = "KeyboardEntry", Content = "Keyboard entry" };
        _keyboardEntryCheck.IsCheckedChanged += (_, _) => _settings.KeyboardEntryEnabled = _keyboardEntryCheck.IsChecked == true;
        entrySources.Children.Add(_keyboardEntryCheck);
        _midiEntryCheck = new CheckBox { Name = "MidiNoteEntry", Content = "MIDI note entry" };
        _midiEntryCheck.IsCheckedChanged += (_, _) => _settings.MidiEntryEnabled = _midiEntryCheck.IsChecked == true;
        entrySources.Children.Add(_midiEntryCheck);
        stack.Children.Add(entrySources);

        void ArrangeEntryOptions(double width)
        {
            bool compact = width < 480;
            auto.ColumnDefinitions.Clear();
            auto.RowDefinitions.Clear();
            entrySources.ColumnDefinitions.Clear();
            entrySources.RowDefinitions.Clear();
            if (compact)
            {
                auto.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                auto.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
                auto.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Grid.SetColumn(_autoSendCheck, 0); Grid.SetRow(_autoSendCheck, 0);
                Grid.SetColumn(delayField, 0); Grid.SetRow(delayField, 2);
                entrySources.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                entrySources.RowDefinitions.Add(new RowDefinition(new GridLength(8)));
                entrySources.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                Grid.SetColumn(_keyboardEntryCheck, 0); Grid.SetRow(_keyboardEntryCheck, 0);
                Grid.SetColumn(_midiEntryCheck, 0); Grid.SetRow(_midiEntryCheck, 2);
            }
            else
            {
                auto.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                auto.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(16)));
                auto.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(174)));
                Grid.SetColumn(_autoSendCheck, 0); Grid.SetRow(_autoSendCheck, 0);
                Grid.SetColumn(delayField, 2); Grid.SetRow(delayField, 0);
                entrySources.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                entrySources.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                Grid.SetColumn(_keyboardEntryCheck, 0); Grid.SetRow(_keyboardEntryCheck, 0);
                Grid.SetColumn(_midiEntryCheck, 1); Grid.SetRow(_midiEntryCheck, 0);
            }
        }

        stack.SizeChanged += (_, _) => ArrangeEntryOptions(stack.Bounds.Width);
        ArrangeEntryOptions(600);
        SetApprovedCardContent(card, stack);
        return card;
    }

    private Control ApprovedDiagnostics()
    {
        var card = ApprovedCard("Signal Diagnostics", "Live translation preview"); card.Height = 500; var stack = new StackPanel { Spacing = 14 }; var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
        (string, TextBlock)[] rows = [("Last Rx note", _diagLastRxLabel = Value()), ("Entered", _diagEnteredLabel = Value()), ("Favorite", _diagFavoriteLabel = Value()), ("device display", _diagDeviceDisplayLabel = Value()), ("MIDI preset", _diagMidiPresetLabel = Value()), ("Bank select CC#0", _diagBankLabel = Value()), ("Program change", _diagPcLabel = Value()), ("Scene", _diagSceneLabel = Value()), ("MIDI channel", _diagChannelLabel = Value())];
        for (int row = 0; row < rows.Length; row++) { grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); var label = new TextBlock { Text = rows[row].Item1, Foreground = SecondaryBrush, Margin = new Thickness(0, 7) }; Grid.SetRow(label, row); Grid.SetRow(rows[row].Item2, row); Grid.SetColumn(rows[row].Item2, 1); grid.Children.Add(label); grid.Children.Add(rows[row].Item2); }
        var test = new Button { Content = "Test Translation", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) }; test.Click += OnTestTranslation; Grid.SetRow(test, rows.Length); Grid.SetColumn(test, 1); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.Children.Add(test); stack.Children.Add(grid);
        stack.Children.Add(new Border { Height = 1, Background = UiBorderBrush });
        _debugCheck = new CheckBox { Name = "DebugMode", Content = "Debug mode (bypass channel filter)" };
        _debugCheck.IsCheckedChanged += (_, _) => _settings.DebugMode = _debugCheck.IsChecked == true;
        stack.Children.Add(_debugCheck);
        SetApprovedCardContent(card, stack); return card;
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
        var categories = ApprovedCard("Collections", titleFontWeight: FontWeight.Bold);
        categories.Padding = new Thickness(16, 17);
        SetApprovedCardContent(categories, _favCategoryTree); _favoritesPage.Children.Add(categories);
        var library = BuildApprovedFavoriteLibrary(); Grid.SetColumn(library, 2); _favoritesPage.Children.Add(library); BuildApprovedFavoriteEditor(); return _favoritesPage;
    }

    private Control BuildApprovedFavoriteLibrary()
    {
        _favoritesDisplayLabel = new TextBlock { Text = "---", FontFamily = new FontFamily("Bahnschrift"), FontSize = 24, FontWeight = FontWeight.Bold, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var card = new Border { Background = SurfaceBrush, BorderBrush = UiBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(16, 11, 12, 17), BoxShadow = Elevation };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,11,*") };
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
        var headerTitle = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(title, 0);
        headerTitle.Children.Add(title);
        _favoriteSentLabel = new TextBlock
        {
            Text = "SENT",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsVisible = false,
            Margin = new Thickness(12, 0, 20, 0),
        };
        Grid.SetColumn(_favoriteSentLabel, 1);
        headerTitle.Children.Add(_favoriteSentLabel);
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
        header.Children.Add(headerTitle);
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
        _favSearchBox = new TextBox { Watermark = "Search name / collection / tag...", MinHeight = 34, Padding = new Thickness(14, 0, 36, 0), CornerRadius = new CornerRadius(5) };
        _favSearchBox.TextChanged += OnFavFilterChanged;
        var search = new Grid(); search.Children.Add(_favSearchBox); search.Children.Add(new PathIcon { Data = Geometry.Parse("M9.5,3 C5.91,3 3,5.91 3,9.5 C3,13.09 5.91,16 9.5,16 C10.9,16 12.2,15.55 13.25,14.78 L18.47,20 L20,18.47 L14.78,13.25 C15.55,12.2 16,10.9 16,9.5 C16,5.91 13.09,3 9.5,3 Z"), Width = 17, Height = 17, Foreground = SecondaryBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false });
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,10,1.1*"), Margin = new Thickness(0, 0, 6, 0) };
        filters.Children.Add(search);
        _favTagFilterLabel = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.Normal,
            Foreground = SecondaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tagFilterContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        tagFilterContent.Children.Add(_favTagFilterLabel);
        var tagChevron = new PathIcon
        {
            Data = Geometry.Parse("M2,4 L7,9 L12,4"),
            Width = 12,
            Height = 10,
            Foreground = SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tagChevron, 1);
        tagFilterContent.Children.Add(tagChevron);
        _favTagFilter = new Button
        {
            Name = "FavoriteTagFilter",
            MinHeight = 34,
            Padding = new Thickness(12, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = tagFilterContent,
        };
        AutomationProperties.SetName(_favTagFilter, "Filter favorites by tag");
        _favTagOptionsPanel = new StackPanel { Name = "FavoriteTagOptions", Spacing = 0 };
        _favTagMatchAllButton = new ToggleButton
        {
            Name = "FavoriteTagMatchAll",
            Content = "All",
            MinHeight = 26,
            Height = 26,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(_favTagMatchAllButton, "Match all selected tags");
        _favTagMatchAllButton.Click += (_, _) => SetFavoriteTagMatchMode(matchAll: true);
        _favTagMatchAnyButton = new ToggleButton
        {
            Name = "FavoriteTagMatchAny",
            Content = "Any",
            MinHeight = 26,
            Height = 26,
            Padding = new Thickness(6, 0),
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(_favTagMatchAnyButton, "Match any selected tag");
        _favTagMatchAnyButton.Click += (_, _) => SetFavoriteTagMatchMode(matchAll: false);
        var matchMode = new Grid { ColumnDefinitions = new ColumnDefinitions("*,2,*") };
        matchMode.Children.Add(_favTagMatchAllButton);
        Grid.SetColumn(_favTagMatchAnyButton, 2);
        matchMode.Children.Add(_favTagMatchAnyButton);
        var popupContent = new StackPanel { Spacing = 4 };
        popupContent.Children.Add(matchMode);
        popupContent.Children.Add(new Border { Height = 1, Background = ThemeBrush("RowSeparatorBrush") });
        popupContent.Children.Add(_favTagOptionsPanel);
        _favTagFilterPopupBorder = new Border
        {
            Name = "FavoriteTagFilterPopup",
            MinWidth = 220,
            MaxHeight = 360,
            Padding = new Thickness(6),
            Background = SurfaceBrush,
            BorderBrush = UiBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            BoxShadow = Elevation,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = popupContent,
            },
        };
        AutomationProperties.SetName(_favTagFilterPopupBorder, "Favorite tag filter options");
        _favTagFilterPopupBorder.AddHandler(InputElement.KeyDownEvent, OnFavoriteTagPopupKeyDown, RoutingStrategies.Tunnel);
        _favTagFilterPopup = new Popup
        {
            PlacementTarget = _favTagFilter,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            IsLightDismissEnabled = true,
            WindowManagerAddShadowHint = true,
            Child = _favTagFilterPopupBorder,
        };
        _favTagFilter.Click += (_, _) =>
        {
            _favTagFilterPopupBorder.Width = Math.Max(220, _favTagFilter.Bounds.Width);
            _favTagFilterPopup.IsOpen = !_favTagFilterPopup.IsOpen;
            if (_favTagFilterPopup.IsOpen)
            {
                _favTagMatchAllButton.Focus();
            }
        };
        var tagFilterHost = new Grid();
        tagFilterHost.Children.Add(_favTagFilter);
        tagFilterHost.Children.Add(_favTagFilterPopup);
        UpdateFavoriteTagFilterPresentation();
        Grid.SetColumn(tagFilterHost, 2);
        filters.Children.Add(tagFilterHost);
        layout.Children.Add(filters);
        var headings = BuildFavColumnGrid(FavColumns.Select((column, index) => { var button = new Button { Content = column.Header, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(11, 0), Foreground = SecondaryBrush, FontSize = 11, FontWeight = FontWeight.Normal, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 0 }; button.Click += (_, _) => OnFavColumnClick(index); return (Control)button; }).ToArray());
        foreach (ColumnDefinition columnDefinition in headings.ColumnDefinitions)
        {
            columnDefinition.PropertyChanged += (_, change) =>
            {
                if (change.Property == ColumnDefinition.WidthProperty)
                {
                    SynchronizeFavoriteColumnWidths(headings);
                }
            };
        }
        for (int index = 0; index < FavColumns.Length - 1; index++)
        {
            int boundary = index;
            var visibleSeparator = new Border
            {
                Name = $"FavoriteHeaderColumnSeparator{boundary}",
                Width = 1,
                Background = ThemeBrush("RowSeparatorBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsHitTestVisible = false,
            };
            Grid.SetColumn(visibleSeparator, boundary);
            headings.Children.Add(visibleSeparator);
            var resizeHandle = new GridSplitter
            {
                Name = $"FavoriteColumnResize{boundary}",
                Width = 8,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.CurrentAndNext,
                ShowsPreview = false,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
            };
            AutomationProperties.SetName(resizeHandle, $"Resize {FavColumns[boundary].Header} column");
            Grid.SetColumn(resizeHandle, boundary);
            headings.Children.Add(resizeHandle);
        }
        Grid.SetRow(headings, 1); layout.Children.Add(headings);
        _favListBox = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemContainerTheme = CompactListItemTheme() }; _favListBox.SelectionChanged += OnFavListSelectionChanged; _favListBox.AddHandler(InputElement.PointerPressedEvent, OnFavListPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true); _favListBox.AddHandler(InputElement.PointerMovedEvent, OnFavListPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true); _favListBox.AddHandler(InputElement.PointerReleasedEvent, OnFavListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true); _favListBox.AddHandler(InputElement.PointerCaptureLostEvent, OnFavListPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        ScrollViewer.SetHorizontalScrollBarVisibility(_favListBox, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_favListBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetAllowAutoHide(_favListBox, false);
        _favListBox.Resources["ScrollBarSize"] = 6d;
        _favListBox.Styles.Add(new Style(selector => selector.OfType<ScrollBar>())
        {
            Setters = { new Setter(ScrollBar.WidthProperty, 6d), new Setter(ScrollBar.MinWidthProperty, 6d), new Setter(ScrollBar.BackgroundProperty, InsetBrush) },
        });
        _favListBox.Styles.Add(new Style(selector => selector.OfType<ScrollBar>().Template().OfType<Thumb>())
        {
            Setters = { new Setter(Thumb.WidthProperty, 6d), new Setter(Thumb.MinHeightProperty, 20d), new Setter(Thumb.CornerRadiusProperty, new CornerRadius(3)), new Setter(Thumb.BackgroundProperty, SecondaryBrush), new Setter(Thumb.OpacityProperty, 0.5d) },
        });
        foreach (string name in new[] { "PART_LineUpButton", "PART_LineDownButton" })
        {
            _favListBox.Styles.Add(new Style(selector => selector.OfType<ScrollBar>().Template().OfType<RepeatButton>().Name(name))
            {
                Setters = { new Setter(RepeatButton.IsVisibleProperty, false) },
            });
        }
        headings.Name = "FavoriteTableHeader";
        headings.HorizontalAlignment = HorizontalAlignment.Left;
        _favListBox.LayoutUpdated += (_, _) =>
        {
            var viewer = _favListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (viewer?.Viewport.Width > 0 && headings.Width != viewer.Viewport.Width)
            {
                headings.Width = viewer.Viewport.Width;
            }
        };
        var body = new Grid();
        body.Children.Add(_favListBox);
        _favEmptyResults = new TextBlock { Name = "FavoriteEmptyResults", Text = "No favorites match these filters.", Foreground = SecondaryBrush, Margin = new Thickness(12, 20), IsHitTestVisible = false, IsVisible = false };
        body.Children.Add(_favEmptyResults);
        Grid.SetRow(body, 2);
        layout.Children.Add(body);
        _favSendBtn = new Button { IsVisible = false }; _favSendBtn.Click += OnFavSend;
        Grid.SetRow(layout, 2);
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
        foreach (var field in new (string, Control)[] { ("Name", _favNameBox), ("Collection", _favCategoryBox), ("Tags", _favTagsEditor) })
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
        var page = new Grid { Name = "ConfigLayout", Margin = new Thickness(24) };
        var connection = new StackPanel { Name = "ConfigLeftColumn", Spacing = 16, Children = { BuildApprovedConnection(showDiagnostics), BuildPresetNameSyncCard(), ApprovedEntryOptions() } };
        var mapping = new StackPanel { Name = "ConfigRightColumn", Spacing = 16, Children = { BuildProfileCard(), BuildApprovedMapping(), BuildApprovedAppearance() } };
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
        var card = ApprovedCard("MIDI Connection", "Hardware and routing");
        card.Name = "MidiConnectionCard";
        var stack = new StackPanel { Spacing = 14 };
        _statusLabel = new TextBlock { Text = "○  Not connected", Foreground = SecondaryBrush, FontWeight = FontWeight.Bold };
        stack.Children.Add(_statusLabel);

        _inputPortCombo = new ComboBox { Name = "MidiInput", HorizontalAlignment = HorizontalAlignment.Stretch };
        _inputPortCombo.SelectionChanged += (_, _) => RefreshThruInputOptions();
        _outputPortCombo = new ComboBox { Name = "MidiOutput", HorizontalAlignment = HorizontalAlignment.Stretch };
        _outputPortCombo.SelectionChanged += (_, _) => RefreshThruInputOptions();
        _thruInputPanel = new StackPanel { Name = "ThruInputs", Spacing = 4 };
        var routing = new StackPanel { Spacing = 14, Children = { ApprovedField("MIDI In", _inputPortCombo), ApprovedField("MIDI Out", _outputPortCombo), ApprovedField("Thru In(s)", _thruInputPanel) } };

        var settingsActions = new StackPanel { Name = "ConnectionActions", Spacing = 8 };
        _connectButton = new Button { Name = "Connect", Content = "Connect", Background = AccentBrush, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Stretch };
        _connectButton.Click += (_, _) => Connect();
        _disconnectButton = new Button { Name = "Disconnect", Content = "Disconnect", Foreground = DangerBrush, HorizontalAlignment = HorizontalAlignment.Stretch };
        _disconnectButton.Click += (_, _) => Disconnect();
        var refresh = new Button { Name = "RefreshDevices", Content = "Refresh Devices", HorizontalAlignment = HorizontalAlignment.Stretch };
        refresh.Click += (_, _) => RefreshPortLists();
        var diagnostics = new Button { Name = "OpenDiagnostics", Content = "Diagnostics", HorizontalAlignment = HorizontalAlignment.Stretch };
        diagnostics.Click += (_, _) => showDiagnostics();
        settingsActions.Children.Add(_connectButton);
        settingsActions.Children.Add(_disconnectButton);
        settingsActions.Children.Add(refresh);
        settingsActions.Children.Add(diagnostics);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,0.72*") };
        columns.Children.Add(routing);
        var actions = ApprovedField("Actions", settingsActions);
        Grid.SetColumn(actions, 2);
        columns.Children.Add(actions);
        stack.Children.Add(columns);
        SetApprovedCardContent(card, stack);
        return card;
    }

    private Control BuildApprovedMapping()
    {
        var card = ApprovedCard("Preset Mapping", "Translation settings · detected limits are applied automatically"); var stack = new StackPanel { Spacing = 14 }; _channelCombo = new ComboBox { Name = "MidiChannel", MinWidth = 100 }; _channelCombo.Items.Add("Omni"); for (int channel = 1; channel <= 16; channel++)
        {
            _channelCombo.Items.Add(channel.ToString());
        }

        _channelCombo.SelectionChanged += (_, _) => { if (_channelCombo.SelectedIndex >= 0) { _settings.MidiChannel = _channelCombo.SelectedIndex; } };
        _offsetCombo = new ComboBox { Name = "DisplayOffset", ItemsSource = new[] { "0 (device mapping disabled)", "1 (display starts at 001)" } };
        _offsetCombo.SelectionChanged += OnOffsetChanged;
        _sceneCcSpinner = new NumericUpDown { Name = "SceneCc", Minimum = 0, Maximum = 127, FormatString = "0", MinWidth = 86, MaxWidth = 100 };
        _sceneCcSpinner.ValueChanged += (_, _) => _settings.SceneCc = (int)(_sceneCcSpinner.Value ?? 34);
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("126,12,*,12,100") };
        var channelField = ApprovedField("MIDI Channel", _channelCombo);
        var offset = ApprovedField("Display Offset", _offsetCombo);
        var scene = ApprovedField("Scene CC#", _sceneCcSpinner);
        fields.Children.Add(channelField);
        Grid.SetColumn(offset, 2); fields.Children.Add(offset);
        Grid.SetColumn(scene, 4); fields.Children.Add(scene);
        stack.Children.Add(fields);
        _deviceCapacityLabel = new TextBlock { Name = "DeviceCapacity", Foreground = SecondaryBrush };
        stack.Children.Add(new Border { Background = InsetBrush, Padding = new Thickness(12, 9), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Child = _deviceCapacityLabel });
        stack.Children.Add(new Border { Background = InsetBrush, Padding = new Thickness(14), CornerRadius = new CornerRadius(5), Child = new TextBlock { Text = "Program Change mapping must be disabled on the device when Display Offset is 0.", TextWrapping = TextWrapping.Wrap, Foreground = SecondaryBrush } }); SetApprovedCardContent(card, stack); return card;
    }

    private Control BuildApprovedAppearance()
    {
        bool isLight = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        var card = ApprovedCard("Appearance", "Application theme"); var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; _darkThemeRadio = new RadioButton { Content = "Dark", IsChecked = !isLight }; _lightThemeRadio = new RadioButton { Content = "Light", IsChecked = isLight }; _darkThemeRadio.IsCheckedChanged += (_, _) => { if (_darkThemeRadio.IsChecked == true) { _lightThemeRadio.IsChecked = false; _settings.Theme = "Dark"; SetTheme("Dark"); } }; _lightThemeRadio.IsCheckedChanged += (_, _) => { if (_lightThemeRadio.IsChecked == true) { _darkThemeRadio.IsChecked = false; _settings.Theme = "Light"; SetTheme("Light"); } }; row.Children.Add(_darkThemeRadio); row.Children.Add(_lightThemeRadio); SetApprovedCardContent(card, row); return card;
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
