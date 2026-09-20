using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace PresetMaestro.Prototypes;

public sealed class PrototypeWindow : Window
{
    private readonly ContentControl _screenHost = new();
    private readonly List<Button> _navButtons = [];
    private readonly ComboBox _variantSelector = new() { Width = 235 };
    private int _variant;
    private int _screen;
    private readonly string? _capturePath;

    private static readonly string[] VariantNames =
    [
        "Option 1 - Modern Professional Audio", "Option 2 - Hardware / Rack Controller",
        "Option 3 - Ultra-Clean Dark", "Option 4 - Modern Studio Console",
        "Option 5 - Contemporary Desktop", "Option 6 - Best Design"
    ];

    public PrototypeWindow()
    {
        _variant = ReadArgument("--variant", 0);
        _screen = ReadArgument("--screen", 0);
        _capturePath = ReadArgument("--capture");
        Width = 1280;
        Height = 820;
        MinWidth = 1080;
        MinHeight = 700;
        Title = "Preset Maestro - Visual Prototypes";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Build();
        _variantSelector.SelectedIndex = _variant;
        ApplyVariant(_variant);
        ShowScreen(_screen);
        if (_capturePath is not null)
            Opened += (_, _) => Dispatcher.UIThread.Post(CaptureAndClose, DispatcherPriority.Render);
    }

    private void Build()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("64,*") };
        root.Children.Add(BuildHeader());
        Grid.SetRow(_screenHost, 1);
        root.Children.Add(_screenHost);
        Content = root;
    }

    private Control BuildHeader()
    {
        var header = new Border { Padding = new Thickness(24, 0), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brush("BorderBrush") };
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,250") };
        layout.Children.Add(new TextBlock { Text = "device DIRECT", FontSize = 17, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        nav.Children.Add(NavButton("Preset Sender", 0));
        nav.Children.Add(NavButton("Favorites", 1));
        nav.Children.Add(NavButton("Config", 2));
        Grid.SetColumn(nav, 1);
        layout.Children.Add(nav);
        _variantSelector.ItemsSource = VariantNames;
        _variantSelector.SelectionChanged += (_, _) => ApplyVariant(Math.Max(0, _variantSelector.SelectedIndex));
        _variantSelector.VerticalAlignment = VerticalAlignment.Center;
        _variantSelector.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_variantSelector, 2);
        layout.Children.Add(_variantSelector);
        header.Child = layout;
        return header;
    }

    private Button NavButton(string text, int index)
    {
        var button = new Button { Content = text, MinHeight = 38, FontWeight = FontWeight.SemiBold };
        button.Click += (_, _) => ShowScreen(index);
        _navButtons.Add(button);
        return button;
    }

    private void ApplyVariant(int variant)
    {
        _variant = variant;
        Application.Current!.RequestedThemeVariant = variant == 4 ? ThemeVariant.Light : ThemeVariant.Dark;
        var palette = variant switch
        {
            0 => ("#121518", "#1B2025", "#101316", "#34414D", "#3B9CFF", "#58D68D", "#F5B942", "#EF6262", "#F2F6F8", "#94A2AD", "#59656F", 7d, "Segoe UI Variable"),
            1 => ("#111416", "#202529", "#090B0D", "#465058", "#55B7FF", "#79D69A", "#F3B84B", "#EC6B6B", "#EAF0F2", "#98A5AC", "#58636A", 3d, "Bahnschrift"),
            2 => ("#0C0E10", "#15181B", "#101214", "#22282D", "#79B8FF", "#65D39A", "#E8C469", "#F17878", "#F7F9FA", "#A9B1B7", "#626A70", 10d, "Segoe UI Variable"),
            3 => ("#1A1715", "#28221E", "#15110F", "#51463B", "#D8A65D", "#79C69A", "#E4B356", "#E76F64", "#FBF4ED", "#BAAEA2", "#796F67", 4d, "Bahnschrift"),
            4 => ("#E9EDF0", "#FFFFFF", "#F4F6F8", "#CDD5DC", "#1969B0", "#278A59", "#C88719", "#C84444", "#17232D", "#5F6B75", "#9AA5AE", 9d, "Segoe UI Variable"),
            _ => ("#11191B", "#1A282B", "#0D1315", "#385258", "#41C3B0", "#88D498", "#F0B35D", "#EC6C68", "#EAF5F4", "#9BAFAD", "#58706E", 6d, "Segoe UI Variable")
        };
        var resources = Application.Current!.Resources;
        resources["AppBrush"] = Brush(palette.Item1);
        resources["SurfaceBrush"] = Brush(palette.Item2);
        resources["InsetBrush"] = Brush(palette.Item3);
        resources["BorderBrush"] = Brush(palette.Item4);
        resources["AccentBrush"] = Brush(palette.Item5);
        resources["SuccessBrush"] = Brush(palette.Item6);
        resources["WarningBrush"] = Brush(palette.Item7);
        resources["DangerBrush"] = Brush(palette.Item8);
        resources["TextBrush"] = Brush(palette.Item9);
        resources["SecondaryBrush"] = Brush(palette.Item10);
        resources["DisabledBrush"] = Brush(palette.Item11);
        resources["HoverBrush"] = Brush(variant == 4 ? "#E5EEF5" : "#2A343B");
        resources["Radius"] = new CornerRadius(palette.Item12);
        resources["ButtonHeight"] = 40d;
        resources["FieldHeight"] = 38d;
        resources["AppFont"] = new FontFamily(palette.Item13);
        resources["SurfaceShadow"] = variant == 4
            ? new BoxShadows(new BoxShadow
            {
                OffsetY = 1,
                Blur = 3,
                Color = Color.Parse("#180E1A25"),
            })
            : default(BoxShadows);
        if (Content is Grid root) root.Background = Brush("AppBrush");
        ShowScreen(_screen);
    }

    private void ShowScreen(int screen)
    {
        _screen = screen;
        for (var index = 0; index < _navButtons.Count; index++)
        {
            _navButtons[index].Background = index == screen ? Brush("AccentBrush") : Brush("SurfaceBrush");
            _navButtons[index].Foreground = index == screen ? Brush("AppBrush") : Brush("TextBrush");
        }
        _screenHost.Content = screen switch { 0 => SenderScreen(), 1 => FavoritesScreen(), _ => ConfigScreen() };
    }

    private Control SenderScreen()
    {
        var page = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("390,18,*"), RowDefinitions = new RowDefinitions("*,210") };
        var left = new StackPanel { Spacing = 16 };
        left.Children.Add(SectionLabel("CURRENT PRESET", "device connected | MIDI channel 1"));
        left.Children.Add(PresetDisplay());
        left.Children.Add(Keypad());
        left.Children.Add(ModeAndAutoSend());
        page.Children.Add(left);
        var diagnostics = Diagnostics(); Grid.SetColumn(diagnostics, 2); page.Children.Add(diagnostics);
        var log = Log(); Grid.SetColumn(log, 2); Grid.SetRow(log, 1); page.Children.Add(log);
        return page;
    }

    private Control PresetDisplay()
    {
        var display = new Border { Height = 150, Background = Brush("InsetBrush"), BorderBrush = Brush("AccentBrush"), BorderThickness = new Thickness(_variant == 2 ? 0 : 1), CornerRadius = Radius(), Padding = new Thickness(20), BoxShadow = Shadow() };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(new TextBlock { Text = _variant == 1 ? "device PRESET" : "ACTIVE PRESET", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("SecondaryBrush") });
        var connected = Status("CONNECTED", "SuccessBrush"); Grid.SetColumn(connected, 1); content.Children.Add(connected);
        var number = new TextBlock { Text = "001", FontSize = _variant == 1 ? 76 : 72, FontFamily = new FontFamily(_variant == 1 ? "Cascadia Mono" : "Bahnschrift"), FontWeight = FontWeight.Bold, Foreground = Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center, LetterSpacing = 2 };
        Grid.SetRow(number, 1); Grid.SetColumnSpan(number, 2); content.Children.Add(number);
        display.Child = content; return display;
    }

    private Control Keypad()
    {
        var deck = new Border { Background = Brush("InsetBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = Radius(), Padding = new Thickness(8), BoxShadow = Shadow() };
        var stack = new StackPanel { Spacing = 7 };
        stack.Children.Add(new TextBlock { Text = "PRESET ENTRY", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brush("SecondaryBrush"), Margin = new Thickness(5, 1, 0, 0) });
        var grid = new UniformGrid { Rows = 5, Columns = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var text in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "CLR", "0", "SEND", "PREV", "LAST", "NEXT" })
            grid.Children.Add(TactileKey(text));
        stack.Children.Add(grid);
        deck.Child = stack;
        return deck;
    }

    private Control TactileKey(string text)
    {
        var isSend = text == "SEND";
        var isCommand = text is "CLR" or "PREV" or "LAST" or "NEXT";
        var housing = new Border
        {
            Background = isSend ? Brush("AccentBrush") : Brush("BorderBrush"),
            CornerRadius = new CornerRadius(Math.Max(3, Radius().TopLeft - 2)),
            Padding = new Thickness(1, 1, 1, _variant == 4 ? 2 : 4),
            Margin = new Thickness(3),
            BoxShadow = Shadow(),
        };
        var face = new Border
        {
            Background = isSend ? Brush("AccentBrush") : Brush("SurfaceBrush"),
            CornerRadius = new CornerRadius(Math.Max(3, Radius().TopLeft - 3)),
        };
        var surface = new Grid { MinHeight = 52 };
        surface.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = isSend ? Brush("AppBrush") : text == "CLR" ? Brush("DangerBrush") : Brush("TextBrush"),
            FontFamily = new FontFamily(isCommand ? "Segoe UI Variable" : "Bahnschrift"),
            FontSize = isCommand ? 12 : isSend ? 16 : 20,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        surface.Children.Add(new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(Math.Max(3, Radius().TopLeft - 3)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        });
        face.Child = surface;
        housing.Child = face;
        return housing;
    }

    private Control ModeAndAutoSend()
    {
        var panel = Card(); var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = "ENTRY MODE", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("SecondaryBrush") });
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        modes.Children.Add(Pill("Presets", true)); modes.Children.Add(Pill("Favorites", false)); stack.Children.Add(modes);
        var auto = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90") };
        auto.Children.Add(new CheckBox { Content = "Auto-send after 3 digits", IsChecked = true, VerticalAlignment = VerticalAlignment.Center });
        var delay = new NumericUpDown { Value = 35, Minimum = 10, Maximum = 2000, FormatString = "0 ms" }; Grid.SetColumn(delay, 1); auto.Children.Add(delay); stack.Children.Add(auto);
        panel.Child = stack; return panel;
    }

    private Control Diagnostics()
    {
        var card = Card("Signal Diagnostics", "Live translation preview");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        var rows = new[] { ("Last Rx note", "C#4 / 61"), ("Entered", "001"), ("device display", "001"), ("MIDI preset", "000"), ("Bank select CC#0", "000"), ("Program change", "000"), ("Scene", "1"), ("MIDI channel", "1"), ("Translation", "VALID") };
        for (var row = 0; row < rows.Length; row++) { var label = new TextBlock { Text = rows[row].Item1, Foreground = Brush("SecondaryBrush"), Margin = new Thickness(0, 7) }; var value = new TextBlock { Text = rows[row].Item2, FontFamily = new FontFamily("Cascadia Mono"), FontWeight = FontWeight.SemiBold, Foreground = row == 8 ? Brush("SuccessBrush") : Brush("TextBrush"), Margin = new Thickness(0, 7) }; Grid.SetRow(label, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1); grid.Children.Add(label); grid.Children.Add(value); }
        var test = new Button { Content = "Test Translation", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) }; Grid.SetRow(test, rows.Length - 1); Grid.SetColumn(test, 1); grid.Children.Add(test);
        SetCardContent(card, grid); return card;
    }

    private Control Log()
    {
        var card = Card("MIDI Log", "Device activity");
        var stack = new StackPanel { Spacing = 7 };
        foreach (var entry in new[] { "14:22:08  RX   NoteOn C#4 velocity 127", "14:22:09  TX   Bank 000 | Program Change 000", "14:22:09  TX   Scene CC#34 = 1", "14:22:09  OK   preset 001 selected" }) stack.Children.Add(new TextBlock { Text = entry, FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12, Foreground = entry.Contains("OK") ? Brush("SuccessBrush") : Brush("SecondaryBrush") });
        SetCardContent(card, stack); return card;
    }

    private Control FavoritesScreen()
    {
        var page = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("210,16,*,16,300") };
        page.Children.Add(Categories());
        var library = Library(); Grid.SetColumn(library, 2); page.Children.Add(library);
        var editor = FavoriteEditor(); Grid.SetColumn(editor, 4); page.Children.Add(editor);
        return page;
    }

    private Control Categories()
    {
        var card = Card("Collections", "12 favorites"); var stack = new StackPanel { Spacing = 6 };
        foreach (var item in new[] { "All Favorites        12", "Ambient               3", "Live Set              4", "Rhythm                3", "Studio                2" }) stack.Children.Add(Pill(item, item.StartsWith("All"), true));
        stack.Children.Add(new Separator { Margin = new Thickness(0, 12) }); stack.Children.Add(new Button { Content = "+ New Category", HorizontalAlignment = HorizontalAlignment.Stretch }); SetCardContent(card, stack); return card;
    }

    private Control Library()
    {
        var card = Card("Favorites", "Preset library"); var layout = new Grid { RowDefinitions = new RowDefinitions("44,36,*,44") };
        var search = new Grid { ColumnDefinitions = new ColumnDefinitions("*,120") };
        search.Children.Add(new TextBox { Watermark = "Search names, categories, or tags", Padding = new Thickness(12, 0) }); var add = new Button { Content = "+ New Favorite", Background = Brush("AccentBrush"), Foreground = Brush("AppBrush") }; Grid.SetColumn(add, 1); search.Children.Add(add); layout.Children.Add(search);
        var headings = FavoriteRow("SLOT", "NAME", "TAGS", "PRESET", "SCENE", true); Grid.SetRow(headings, 1); layout.Children.Add(headings);
        var list = new StackPanel { Spacing = 3 };
        foreach (var favorite in new[] { ("001", "Glass Horizon", "ambient, clean", "001", "1"), ("002", "Rectifier Rhythm", "live, high gain", "118", "2"), ("003", "Satch Lead", "lead, delay", "247", "4"), ("004", "Acoustic Bloom", "clean, studio", "032", "1"), ("005", "Midnight Drive", "live, crunch", "076", "3"), ("006", "Stereo Pad", "ambient, shimmer", "301", "2") }) list.Children.Add(FavoriteRow(favorite.Item1, favorite.Item2, favorite.Item3, favorite.Item4, favorite.Item5, favorite.Item1 == "002"));
        Grid.SetRow(list, 2); layout.Children.Add(list); var send = new Button { Content = "Send Selected to device", Background = Brush("AccentBrush"), Foreground = Brush("AppBrush"), HorizontalAlignment = HorizontalAlignment.Left, Width = 200 }; Grid.SetRow(send, 3); layout.Children.Add(send); SetCardContent(card, layout); return card;
    }

    private Control FavoriteRow(string slot, string name, string tags, string preset, string scene, bool selected)
    {
        var border = new Border { Padding = new Thickness(10, 9), Background = selected ? Brush("HoverBrush") : Brushes.Transparent, CornerRadius = new CornerRadius(4) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,150,70,60") };
        string[] values = [slot, name, tags, preset, scene];
        for (var index = 0; index < values.Length; index++) { var text = new TextBlock { Text = values[index], FontWeight = selected && index == 1 ? FontWeight.SemiBold : FontWeight.Normal, FontSize = index == 2 ? 12 : 13, Foreground = index == 2 ? Brush("SecondaryBrush") : Brush("TextBrush") }; Grid.SetColumn(text, index); grid.Children.Add(text); }
        border.Child = grid; return border;
    }

    private Control FavoriteEditor()
    {
        var card = Card("Edit Favorite", "Slot 002"); var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(Field("Name", new TextBox { Text = "Rectifier Rhythm" })); stack.Children.Add(Field("Category", new ComboBox { ItemsSource = new[] { "Live Set", "Ambient", "Rhythm", "Studio" }, SelectedIndex = 0 })); stack.Children.Add(Field("Tags", new TextBox { Text = "live, high gain" }));
        var numbers = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") }; numbers.Children.Add(Field("Slot", Number(2))); var preset = Field("Preset", Number(118)); Grid.SetColumn(preset, 1); numbers.Children.Add(preset); var scene = Field("Scene", Number(2)); Grid.SetColumn(scene, 2); numbers.Children.Add(scene); stack.Children.Add(numbers);
        stack.Children.Add(new Separator { Margin = new Thickness(0, 4) }); var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(new Button { Content = "Save", Background = Brush("AccentBrush"), Foreground = Brush("AppBrush") }); actions.Children.Add(new Button { Content = "Cancel" }); stack.Children.Add(actions); stack.Children.Add(new Button { Content = "Delete Favorite", Foreground = Brush("DangerBrush"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }); SetCardContent(card, stack); return card;
    }

    private Control ConfigScreen()
    {
        var page = new Grid { Margin = new Thickness(24), ColumnDefinitions = new ColumnDefinitions("1.2*,16,*"), RowDefinitions = new RowDefinitions("*,220") };
        var connection = Connection(); page.Children.Add(connection); var mapping = Mapping(); Grid.SetColumn(mapping, 2); page.Children.Add(mapping); var appearance = Appearance(); Grid.SetRow(appearance, 1); Grid.SetColumnSpan(appearance, 3); page.Children.Add(appearance); return page;
    }

    private Control Connection()
    {
        var card = Card("MIDI Connection", "Hardware and routing"); var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Status("device CONNECTED", "SuccessBrush")); stack.Children.Add(Field("MIDI In", new ComboBox { ItemsSource = new[] { "MIDI In", "USB MIDI Device" }, SelectedIndex = 0 })); stack.Children.Add(Field("MIDI Out", new ComboBox { ItemsSource = new[] { "MIDI Out", "Microsoft GS Wavetable" }, SelectedIndex = 0 }));
        var refresh = new Button { Content = "Refresh Devices", HorizontalAlignment = HorizontalAlignment.Left }; stack.Children.Add(refresh); var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; action.Children.Add(new Button { Content = "Connected", IsEnabled = false }); action.Children.Add(new Button { Content = "Disconnect", Foreground = Brush("DangerBrush") }); stack.Children.Add(action);
        var debug = new CheckBox { Content = "Debug mode (bypass channel filter)", Margin = new Thickness(0, 6) }; stack.Children.Add(debug); SetCardContent(card, stack); return card;
    }

    private Control Mapping()
    {
        var card = Card("Preset Mapping", "Translation settings"); var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(Field("MIDI Channel", new ComboBox { ItemsSource = Enumerable.Range(1, 16).Select(value => $"Channel {value}").ToArray(), SelectedIndex = 0 })); stack.Children.Add(Field("Display Offset", new ComboBox { ItemsSource = new[] { "0 (device mapping disabled)", "1 (display starts at 001)" }, SelectedIndex = 0 })); stack.Children.Add(Field("Max Preset", Number(512))); stack.Children.Add(Field("Scene CC#", Number(34)));
        stack.Children.Add(new Border { Background = Brush("InsetBrush"), Padding = new Thickness(14), CornerRadius = new CornerRadius(5), Child = new TextBlock { Text = "Program Change mapping must be disabled on the device when Display Offset is 0.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryBrush") } }); SetCardContent(card, stack); return card;
    }

    private Control Appearance()
    {
        var card = Card("Appearance", "Prototype display preferences"); var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,300") };
        layout.Children.Add(new TextBlock { Text = "The selected visual direction is previewed live above. The production application will reuse these tokens, templates, control sizes, and interaction states.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryBrush"), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 });
        var selector = new StackPanel { Spacing = 7 }; selector.Children.Add(new TextBlock { Text = "COLOR MODE", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("SecondaryBrush") }); var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; modes.Children.Add(Pill("Dark", _variant != 4)); modes.Children.Add(Pill("Light", _variant == 4)); selector.Children.Add(modes); Grid.SetColumn(selector, 1); layout.Children.Add(selector); SetCardContent(card, layout); return card;
    }

    private Border Card(string? title = null, string? subtitle = null)
    {
        var card = new Border { Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(_variant == 2 ? 0 : 1), CornerRadius = Radius(), Padding = new Thickness(18), BoxShadow = Shadow() };
        if (title is null) return card;
        var stack = new StackPanel { Spacing = 16 }; var heading = new StackPanel { Spacing = 2 }; heading.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold }); if (subtitle is not null) heading.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Foreground = Brush("SecondaryBrush") }); var body = new ContentControl(); stack.Children.Add(heading); stack.Children.Add(body); card.Child = stack; card.Tag = body; return card;
    }

    private static void SetCardContent(Border card, Control content) => ((ContentControl)card.Tag!).Content = content;

    private Control SectionLabel(string title, string detail) => new StackPanel { Spacing = 3, Children = { new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brush("SecondaryBrush") }, new TextBlock { Text = detail, FontSize = 13, Foreground = Brush("SuccessBrush") } } };
    private Border Pill(string text, bool active, bool wide = false) => new() { Background = active ? Brush("AccentBrush") : Brush("InsetBrush"), CornerRadius = new CornerRadius(999), Padding = new Thickness(12, 7), HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left, Child = new TextBlock { Text = text, Foreground = active ? Brush("AppBrush") : Brush("TextBrush"), FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal } };
    private Border Status(string text, string color) => new() { Background = Brush("InsetBrush"), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 5), Child = new TextBlock { Text = "●  " + text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush(color) } };
    private Control Field(string label, Control field) => new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 8, 0), Children = { new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("SecondaryBrush") }, field } };
    private NumericUpDown Number(int value) => new() { Value = value, Minimum = 0, Maximum = 999, FormatString = "0" };
    private CornerRadius Radius() => (CornerRadius)Application.Current!.Resources["Radius"]!;
    private static BoxShadows Shadow() => (BoxShadows)Application.Current!.Resources["SurfaceShadow"]!;
    private static IBrush Brush(string value) => value.StartsWith('#')
        ? new SolidColorBrush(Color.Parse(value))
        : (IBrush)Application.Current!.Resources[value]!;

    private void CaptureAndClose()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(_capturePath!)!);
        using var stream = File.Create(_capturePath!);
        bitmap.Save(stream);
        Close();
    }

    private static int ReadArgument(string name, int fallback)
    {
        var value = ReadArgument(name);
        return int.TryParse(value, out var parsed) ? Math.Clamp(parsed, 0, 5) : fallback;
    }

    private static string? ReadArgument(string name) => Environment.GetCommandLineArgs()
        .FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?
        .Split('=', 2)[1];
}
