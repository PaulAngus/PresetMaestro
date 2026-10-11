using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PresetMaestro.Midi;

namespace PresetSender;

public sealed class SenderWindow : Window
{
    private static readonly string[] AnyInputChannel = ["Any channel"];
    private static readonly string[] AllOutputChannels = ["All channels"];
    private sealed record InputChoice(string? Id, string Name) { public override string ToString() => Name; }
    private sealed record OutputChoice(int Index, string Name, int Occurrence, bool IsVirtual = false) { public override string ToString() => Name; }
    private readonly SenderState _state = new();
    private readonly SenderSettings _settings;
    private readonly ISenderMidiBackend _backend;
    private readonly bool _discoverPorts;
    private readonly bool _offline;
    private readonly ComboBox _input = new(), _output = new(), _channel = new(), _inputChannel = new(), _numbering = new(), _command = new();
    private readonly Button _connect = new() { Content = "Connect" }, _refresh = new() { Content = "Refresh ports" };
    private readonly Button _send = new() { Content = "Send", Name = "SendProgram" }, _learn = new() { Content = "Learn" };
    private readonly Expander _connectionExpander = new() { Header = "MIDI connection", IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _display = new() { FontSize = 52, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Name = "ProgramDisplay" };
    private readonly TextBlock _status = Text("Disconnected. Select an output, then Connect."), _last = Text("No program sent yet."), _mappingStatus = Text("No MIDI controls learned.");
    private readonly CheckBox _forward = new() { Content = "Forward incoming program changes" };
    private readonly StackPanel _sender = new() { Spacing = 10, Focusable = true, Name = "SenderKeypad" };
    private IMidiInputPort? _inputPort;
    private IMidiOutputPort? _outputPort;
    private string? _learning;
    private bool _busy, _closed;
    private readonly Action<int>? _testSend;
    private readonly Control _keyboardTarget;

    public SenderWindow() : this(false, null) { }
    internal SenderWindow(bool offline, Action<int>? testSend, ISenderMidiBackend? backend = null)
    {
        _offline = offline;
        _backend = backend ?? new NativeMidiBackend();
        _discoverPorts = !offline || backend is not null;
        _testSend = testSend;
        _settings = offline ? new() : SenderSettings.Load();
        _state.NumberingOffset = _settings.Offset;
        _state.OutputChannel = _settings.OutputChannel;
        _state.InputChannel = _settings.InputChannel;
        _state.ForwardProgramChanges = _settings.ForwardProgramChanges;
        _state.Bindings.AddRange(_settings.Bindings);
        this.Bind(BackgroundProperty, this.GetResourceObservable("SenderBackground"));
        this.Bind(ForegroundProperty, this.GetResourceObservable("SenderText"));
        Title = "Preset Sender";
        Icon = new WindowIcon(BrandAssets.Logo);
        Width = 460; Height = 740; MinWidth = 360; MinHeight = 520;
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var logo = new Image { Source = BrandAssets.Logo, Width = 36, Height = 36, IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(logo, AccessibilityView.Raw);
        heading.Children.Add(logo);
        heading.Children.Add(new TextBlock { Text = "Preset Sender", FontSize = 24, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(heading);
        _input.Name = "MidiInput"; _output.Name = "MidiOutput";
        _command.Name = "LearnCommand"; _learn.Name = "LearnMidi"; _connect.Name = "ConnectMidi"; _channel.Name = "OutputChannel";
        var connection = new StackPanel { Spacing = 12 };
        connection.Children.Add(Field("MIDI input (optional)", _input));
        connection.Children.Add(Field("MIDI output", _output));
        _connectionExpander.Content = connection; body.Children.Add(_connectionExpander);
        var ports = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        ports.Children.Add(_connect); ports.Children.Add(_refresh); body.Children.Add(ports);
        _channel.ItemsSource = AllOutputChannels.Concat(Enumerable.Range(1, 16).Select(i => $"Channel {i}")).ToArray(); _channel.SelectedIndex = _state.OutputChannel;
        _numbering.ItemsSource = new[] { "0–127 (zero-based)", "1–128 (one-based)" }; _numbering.SelectedIndex = _state.NumberingOffset;
        var options = new StackPanel { Spacing = 12 };
        options.Children.Add(Field("Output channel", _channel));
        options.Children.Add(Field("Preset numbering", _numbering));
        var entryOptions = new StackPanel { Spacing = 12 };
        entryOptions.Children.Add(options);
        entryOptions.Children.Add(Text("All channels sends the same program change on channels 1–16. A receiver listening to all channels may receive it 16 times."));
        _sender.Children.Add(_display);
        _sender.Children.Add(Text("Type digits here; Enter sends. Escape clears.", HorizontalAlignment.Center));
        var keypad = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        int[] keys = [7, 8, 9, 4, 5, 6, 1, 2, 3];
        for (int i = 0; i < keys.Length; i++)
        {
            int digit = keys[i];
            var button = new Button { Content = digit.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 40 };
            AutomationProperties.SetName(button, $"Digit {digit}");
            button.Click += (_, _) => Execute(digit.ToString(CultureInfo.InvariantCulture));
            button.Margin = new Thickness(4); Grid.SetRow(button, i / 3); Grid.SetColumn(button, i % 3); keypad.Children.Add(button);
        }
        var clear = new Button { Content = "Clear", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 40 };
        clear.Click += (_, _) => Execute("Clear");
        var zero = new Button { Content = "0", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, MinHeight = 40 };
        _keyboardTarget = zero;
        AutomationProperties.SetName(zero, "Digit 0"); zero.Click += (_, _) => Execute("0");
        _send.HorizontalAlignment = HorizontalAlignment.Stretch; _send.HorizontalContentAlignment = HorizontalAlignment.Center; _send.MinHeight = 40;
        _send.Classes.Add("accent"); _send.Click += (_, _) => Execute("Send");
        clear.Margin = zero.Margin = _send.Margin = new Thickness(4); Grid.SetRow(clear, 3); Grid.SetRow(zero, 3); Grid.SetRow(_send, 3); Grid.SetColumn(zero, 1); Grid.SetColumn(_send, 2);
        keypad.Children.Add(clear); keypad.Children.Add(zero); keypad.Children.Add(_send);
        _sender.Children.Add(keypad); _sender.Children.Add(_last); body.Children.Add(_sender);
        body.Children.Add(new Expander { Header = "Program options", Content = entryOptions, HorizontalAlignment = HorizontalAlignment.Stretch });
        AutomationProperties.SetName(_display, "Pending preset number");
        _sender.AddHandler(KeyDownEvent, SenderKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var mapping = new StackPanel { Spacing = 12 };
        _inputChannel.ItemsSource = AnyInputChannel.Concat(Enumerable.Range(1, 16).Select(i => $"Channel {i}")).ToArray(); _inputChannel.SelectedIndex = _state.InputChannel;
        mapping.Children.Add(Field("Input channel", _inputChannel)); mapping.Children.Add(_forward);
        _forward.IsChecked = _state.ForwardProgramChanges;
        _command.ItemsSource = Enumerable.Range(0, 10).Select(i => i.ToString(CultureInfo.InvariantCulture)).Concat(["Send", "Clear"]).ToArray(); _command.SelectedIndex = 0;
        mapping.Children.Add(Field("Command to learn", _command));
        var learnActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var remove = new Button { Content = "Remove mapping" };
        learnActions.Children.Add(_learn); learnActions.Children.Add(remove); mapping.Children.Add(learnActions);
        mapping.Children.Add(Text("Choose a command, click Learn, then send a note, CC, or program change. Release note/CC buttons between presses. Learning consumes that press."));
        mapping.Children.Add(_mappingStatus);
        body.Children.Add(new Expander { Header = "MIDI controls", Content = mapping, HorizontalAlignment = HorizontalAlignment.Stretch });
        body.Children.Add(Text(_backend.SupportsVirtualOutput
            ? "To send to another app, choose Preset Sender (virtual device), then Connect. Select Preset Sender as the receiving app’s MIDI input. Requires Windows 11 25H2 or later; uses the preview Windows MIDI Services API."
            : "For another app on this computer, select a virtual MIDI output and use its matching input in the receiving app."));
        Content = new DockPanel
        {
            Children =
            {
                StatusFooter(),
                new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
            }
        };
        _connect.Click += async (_, _) => await ToggleConnectionAsync();
        _refresh.Click += async (_, _) => await RefreshPortsAsync();
        _channel.SelectionChanged += (_, _) => { if (_channel.SelectedIndex >= 0) { _state.OutputChannel = _channel.SelectedIndex; } UpdateDisplay(); };
        _numbering.SelectionChanged += (_, _) => { _state.NumberingOffset = _numbering.SelectedIndex; _state.Clear(); UpdateDisplay(); };
        _inputChannel.SelectionChanged += (_, _) => { _state.InputChannel = _inputChannel.SelectedIndex; _state.ResetInput(); };
        _forward.IsCheckedChanged += (_, _) => _state.ForwardProgramChanges = _forward.IsChecked == true;
        _learn.Click += (_, _) =>
        {
            if (_learning is not null) { CancelLearn(); return; }
            if (_inputPort is null) { _status.Text = "Connect a MIDI input before learning controls."; return; }
            _learning = (string?)_command.SelectedItem;
            _state.ResetInput(); _learn.Content = "Cancel learn";
            _mappingStatus.Text = $"Waiting for a MIDI note, CC, or program change for {_learning}.";
        };
        remove.Click += (_, _) => { CancelLearn(); _state.Bindings.RemoveAll(b => b.Command == (string?)_command.SelectedItem); UpdateMappings(); };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _learning is not null)
            {
                CancelLearn(); _keyboardTarget?.Focus(); e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += async (_, _) => { await RefreshPortsAsync(); _keyboardTarget.Focus(); };
        Closed += (_, _) => { _closed = true; Disconnect(); SaveSettings(); };
        UpdateDisplay(); UpdateMappings();
    }

    private Border StatusFooter()
    {
        var footer = new Border { Padding = new Thickness(20, 10), Child = _status, BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Bind(Border.BorderBrushProperty, this.GetResourceObservable("SystemControlForegroundBaseMediumLowBrush"));
        DockPanel.SetDock(footer, Dock.Bottom);
        AutomationProperties.SetName(_status, "MIDI connection and send status");
        return footer;
    }

    private static TextBlock Text(string value, HorizontalAlignment alignment = HorizontalAlignment.Stretch) => new() { Text = value, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = alignment };
    private static StackPanel Field(string label, Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(control, label);
        return new StackPanel { Spacing = 4, Children = { Text(label), control } };
    }

    private void SenderKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        int digit = e.Key >= Key.D0 && e.Key <= Key.D9 ? e.Key - Key.D0 : e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ? e.Key - Key.NumPad0 : -1;
        if (digit >= 0)
        {
            Execute(digit.ToString(CultureInfo.InvariantCulture));
        }
        else if (e.Key == Key.Enter)
        {
            Execute("Send");
        }
        else if (e.Key == Key.Escape) { CancelLearn(); Execute("Clear"); }
        else if (e.Key == Key.Back) { _state.Backspace(); UpdateDisplay(); }
        else
        {
            return;
        }

        e.Handled = true;
    }

    internal void Execute(string command)
    {
        if (command.Length == 1 && char.IsAsciiDigit(command[0]))
        {
            if (!_state.Digit(command[0] - '0'))
            {
                _status.Text = "Entry already has 3 digits. Clear or Backspace to edit.";
            }
        }
        else if (command == "Clear")
        {
            _state.Clear();
        }
        else if (command == "Send")
        {
            if (_state.TryGetProgram(out int program))
            {
                Send(program);
            }
            else
            {
                _status.Text = $"Enter a preset from {_state.NumberingOffset} to {127 + _state.NumberingOffset}.";
            }
        }
        UpdateDisplay();
    }

    private void Send(int program)
    {
        if (_outputPort is null && _testSend is null) { _status.Text = "Connect a MIDI output before sending."; return; }
        int sentCount = 0;
        try
        {
            int firstChannel = _state.OutputChannel == 0 ? 1 : _state.OutputChannel;
            int lastChannel = _state.OutputChannel == 0 ? 16 : _state.OutputChannel;
            for (int channel = firstChannel; channel <= lastChannel; channel++)
            {
                int message = MidiMessageEncoding.ProgramChange(program, channel);
                if (_testSend is not null) { _testSend(message); }
                else { _outputPort!.Send(message); }
                sentCount++;
            }

            _state.Sent(program);
            string channels = _state.OutputChannel == 0 ? "All channels (1–16)" : $"Channel {_state.OutputChannel}";
            _last.Text = $"Last sent: {_state.Display(program)} · MIDI PC {program} · {channels}";
            _status.Text = $"Sent preset {_state.Display(program)}.";
        }
        catch (Exception ex)
        {
            Disconnect();
            string partial = sentCount > 0 ? " Some channels may already have received the change." : "";
            _status.Text = $"Send failed: {ex.Message}{partial} Reconnect the MIDI ports.";
        }
    }

    private void Receive(object? sender, MidiShortMessageEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed || !ReferenceEquals(sender, _inputPort))
        {
            return;
        }

        string? command = _state.Receive(e.RawMessage, out var pressed);
        if (_learning is not null)
        {
            if (pressed is null)
            {
                return;
            }

            _state.Bindings.RemoveAll(b => b.Command == _learning || (b.Kind == pressed.Kind && b.Channel == pressed.Channel && b.Number == pressed.Number));
            _state.Bindings.Add(pressed with { Command = _learning });
            CancelLearn(); return;
        }
        if (command?.StartsWith("PC:", StringComparison.Ordinal) == true) { Send(int.Parse(command.AsSpan(3), CultureInfo.InvariantCulture)); UpdateDisplay(); }
        else if (command is not null)
        {
            Execute(command);
        }
    });

    private void InputError(object? sender, string error) => Dispatcher.UIThread.Post(() =>
    {
        if (!ReferenceEquals(sender, _inputPort))
        {
            return;
        }

        Disconnect(); _status.Text = $"MIDI input error: {error} Reconnect the MIDI ports.";
    });

    private async Task RefreshPortsAsync()
    {
        if (_busy || _outputPort is not null)
        {
            return;
        }

        SetBusy(true);
        string? inputId = _input.SelectedItem is InputChoice selectedInput ? selectedInput.Id : _settings.InputId;
        string? outputName = (_output.SelectedItem as OutputChoice)?.Name ?? _settings.OutputName;
        int occurrence = (_output.SelectedItem as OutputChoice)?.Occurrence ?? _settings.OutputOccurrence;
        bool virtualOutput = (_output.SelectedItem as OutputChoice)?.IsVirtual ?? _settings.VirtualOutput ?? (_backend.SupportsVirtualOutput && outputName is null);
        try
        {
            var ports = await Task.Run(() => !_discoverPorts
                ? (Inputs: Array.Empty<(string Id, string Name)>(), Outputs: Array.Empty<(int Index, string Name)>())
                : (Inputs: _backend.GetInputDevices().ToArray(), Outputs: _backend.GetOutputDevices().ToArray()));
            if (_closed)
            {
                return;
            }

            var inputs = new[] { new InputChoice(null, "None — computer keyboard only") }.Concat(ports.Inputs.Select(p => new InputChoice(p.Id, p.Name))).ToArray();
            var outputs = ports.Outputs.Select(p => new OutputChoice(p.Index, p.Name, ports.Outputs.TakeWhile(q => q.Index != p.Index).Count(q => q.Name == p.Name))).ToList();
            if (_backend.SupportsVirtualOutput) { outputs.Insert(0, new OutputChoice(-1, "Preset Sender (virtual device)", 0, true)); }
            _input.ItemsSource = inputs; _input.SelectedItem = inputs.FirstOrDefault(p => p.Id == inputId) ?? inputs[0];
            _output.ItemsSource = outputs; _output.SelectedItem = outputs.FirstOrDefault(p => virtualOutput ? p.IsVirtual : !p.IsVirtual && p.Name == outputName && p.Occurrence == occurrence);
            if (_output.SelectedItem is null && outputs.Count > 0 && outputName is null && !virtualOutput)
            {
                _output.SelectedIndex = 0;
            }

            _status.Text = outputs.Count == 0 ? "No MIDI outputs found. Connect hardware or create a virtual MIDI port, then Refresh ports." : "Disconnected. Select an output, then Connect.";
        }
        catch (Exception ex) { _status.Text = $"Could not list MIDI ports: {ex.Message}"; }
        finally { SetBusy(false); }
    }

    private async Task ToggleConnectionAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_outputPort is not null) { Disconnect(); _status.Text = "Disconnected."; return; }
        if (_output.SelectedItem is not OutputChoice output) { _status.Text = "Select a MIDI output first."; return; }
        string? inputId = (_input.SelectedItem as InputChoice)?.Id;
        SetBusy(true);
        IMidiOutputPort? openedOutput = null;
        IMidiInputPort? openedInput = null;
        try
        {
            await Task.Run(() =>
            {
                openedOutput = output.IsVirtual ? _backend.OpenVirtualOutput() : _backend.OpenOutput(output.Index);
                if (inputId is not null)
                {
                    openedInput = _backend.OpenInput(inputId);
                }
            });
            if (_closed)
            {
                return;
            }

            _outputPort = openedOutput; _inputPort = openedInput;
            if (_inputPort is not null)
            {
                _inputPort.MessageReceived += Receive; _inputPort.ErrorReceived += InputError; _inputPort.Start();
            }
            openedOutput = null; openedInput = null;
            _connect.Content = "Disconnect";
            _connectionExpander.IsExpanded = false;
            _status.Text = output.IsVirtual ? "Preset Sender is available as a MIDI input in other apps. Select it there to receive program changes." : $"Connected to {output.Name}.";
            _keyboardTarget?.Focus(); SaveSettings();
        }
        catch (Exception ex)
        {
            // Locals retain ownership until input startup has succeeded.
            if (_inputPort is not null) { _inputPort.MessageReceived -= Receive; _inputPort.ErrorReceived -= InputError; }
            _inputPort = null; _outputPort = null;
            _status.Text = $"Connection failed: {ex.Message}";
        }
        finally
        {
            try { openedInput?.Dispose(); } catch (Exception) { }
            try { openedOutput?.Dispose(); } catch (Exception) { }
            SetBusy(false);
            UpdateDisplay();
        }
    }

    private void Disconnect()
    {
        var input = _inputPort; var output = _outputPort;
        _inputPort = null; _outputPort = null;
        if (input is not null)
        {
            input.MessageReceived -= Receive; input.ErrorReceived -= InputError;
            try { input.Stop(); } catch (Exception) { }
            try { input.Dispose(); } catch (Exception) { }
        }
        try { output?.Dispose(); } catch (Exception) { }
        _state.ResetInput(); CancelLearn();
        _connectionExpander.IsExpanded = true;
        _connect.Content = "Connect"; SetBusy(false); UpdateDisplay();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; _connect.IsEnabled = !busy;
        _input.IsEnabled = _output.IsEnabled = _refresh.IsEnabled = !busy && _outputPort is null;
    }

    private void UpdateDisplay()
    {
        _display.Text = _state.Entry.PadLeft(SenderState.EntryLength, '–');
        _send.IsEnabled = _state.TryGetProgram(out _) && (_outputPort is not null || _testSend is not null);
    }
    private void CancelLearn() { _learning = null; _learn.Content = "Learn"; UpdateMappings(); }
    private void UpdateMappings() => _mappingStatus.Text = _state.Bindings.Count == 0 ? "No MIDI controls learned." : string.Join(Environment.NewLine, _state.Bindings.OrderBy(b => b.Command, StringComparer.Ordinal).Select(b => $"{b.Command}: {(b.Kind == 0x90 ? "Note" : b.Kind == 0xb0 ? "CC" : "PC")} {b.Number}, channel {b.Channel}"));

    private void SaveSettings()
    {
        if (_offline)
        {
            return;
        }

        if (_input.SelectedItem is InputChoice selectedInput) { _settings.InputId = selectedInput.Id; }
        if (_output.SelectedItem is OutputChoice selectedOutput)
        {
            _settings.OutputName = selectedOutput.Name;
            _settings.OutputOccurrence = selectedOutput.Occurrence;
            _settings.VirtualOutput = selectedOutput.IsVirtual;
        }
        _settings.Offset = _state.NumberingOffset;
        _settings.OutputChannel = _state.OutputChannel; _settings.InputChannel = _state.InputChannel;
        _settings.ForwardProgramChanges = _state.ForwardProgramChanges; _settings.Bindings = _state.Bindings;
        try { _settings.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = $"Could not save settings: {ex.Message}"; }
    }
}

