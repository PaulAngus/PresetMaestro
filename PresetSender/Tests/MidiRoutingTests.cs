using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Midi;

namespace PresetSender.Tests;

public sealed class MidiRoutingTests
{
    [AvaloniaFact]
    public async Task VirtualDevicePublishesWithoutSendingAndBroadcastsOnEveryChannel()
    {
        var midi = new FakeMidi { SupportsVirtualOutput = true };
        var window = new SenderWindow(true, null, midi);
        window.Show();
        try
        {
            await WaitUntil(() => Find<ComboBox>(window, "MidiOutput").ItemCount == 2);
            Click(Find<Button>(window, "ConnectMidi"));
            await WaitUntil(() => Equals(Find<Button>(window, "ConnectMidi").Content, "Disconnect"));
            Assert.True(midi.OpenedVirtual);
            Assert.Empty(midi.Output.Messages);
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = true; }
            Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "OutputChannel").SelectedIndex = 0;
            window.Execute("4"); window.Execute("2"); window.Execute("Send");
            Assert.Equal(Enumerable.Range(0, 16).Select(channel => 0xc0 | channel | (42 << 8)), midi.Output.Messages);
            Assert.Equal("–––", Find<TextBlock>(window, "ProgramDisplay").Text);
            Click(Find<Button>(window, "ConnectMidi"));
            Assert.True(midi.Output.Disposed);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ForwardedProgramChangesUseAllChannelsAndInputFilter()
    {
        var midi = new FakeMidi { SupportsVirtualOutput = true };
        var window = new SenderWindow(true, null, midi);
        window.Show();
        try
        {
            await WaitUntil(() => Find<ComboBox>(window, "MidiOutput").ItemCount == 2);
            Find<ComboBox>(window, "MidiInput").SelectedIndex = 1;
            Click(Find<Button>(window, "ConnectMidi"));
            await WaitUntil(() => midi.Input.Started);
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = true; }
            Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "OutputChannel").SelectedIndex = 0;
            window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
            var inputChannel = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemCount == 17 && !Equals(c.Items[0], "All channels"));
            inputChannel.SelectedIndex = 3;
            midi.Input.Emit(0xc1 | (127 << 8)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Output.Messages);
            midi.Input.Emit(0xc2 | (127 << 8)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Enumerable.Range(0, 16).Select(channel => 0xc0 | channel | (127 << 8)), midi.Output.Messages);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PartialBroadcastFailureKeepsEntryAndClosesVirtualDevice()
    {
        var midi = new FakeMidi { SupportsVirtualOutput = true };
        midi.Output.FailAfter = 5;
        var window = new SenderWindow(true, null, midi);
        window.Show();
        try
        {
            await WaitUntil(() => Find<ComboBox>(window, "MidiOutput").ItemCount == 2);
            Click(Find<Button>(window, "ConnectMidi"));
            await WaitUntil(() => Equals(Find<Button>(window, "ConnectMidi").Content, "Disconnect"));
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = true; }
            Dispatcher.UIThread.RunJobs();
            Find<ComboBox>(window, "OutputChannel").SelectedIndex = 0;
            window.Execute("4"); window.Execute("2"); window.Execute("Send");
            Assert.Equal(5, midi.Output.Messages.Count);
            Assert.True(midi.Output.Disposed);
            Assert.Equal("–42", Find<TextBlock>(window, "ProgramDisplay").Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), c => c.Text?.Contains("Some channels may already", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LearnedProgramButtonsEnterTwoDigitsThenSendOnChosenChannel()
    {
        var midi = new FakeMidi();
        var window = new SenderWindow(true, null, midi);
        window.Show();
        try
        {
            await WaitUntil(() => Find<ComboBox>(window, "MidiOutput").ItemCount == 1);
            Find<ComboBox>(window, "MidiInput").SelectedIndex = 1;
            Click(Find<Button>(window, "ConnectMidi"));
            await WaitUntil(() => midi.Input.Started);
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = true; }
            Dispatcher.UIThread.RunJobs();
            var command = Find<ComboBox>(window, "LearnCommand");
            Learn(4, 10); Learn(2, 11); Learn(10, 12);
            Assert.Empty(midi.Output.Messages);
            Find<ComboBox>(window, "OutputChannel").SelectedIndex = 16;
            midi.Input.Emit(0xc0 | (10 << 8)); Dispatcher.UIThread.RunJobs();
            midi.Input.Emit(0xc0 | (11 << 8)); Dispatcher.UIThread.RunJobs();
            Assert.Empty(midi.Output.Messages);
            Assert.Equal("–42", Find<TextBlock>(window, "ProgramDisplay").Text);
            midi.Input.Emit(0xc0 | (12 << 8)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { 0xcf | (42 << 8) }, midi.Output.Messages);
            Click(Find<Button>(window, "ConnectMidi"));
            Assert.True(midi.Input.Disposed); Assert.True(midi.Output.Disposed);
            midi.Input.Emit(0xc0 | (12 << 8)); Dispatcher.UIThread.RunJobs();
            Assert.Single(midi.Output.Messages);

            void Learn(int index, int pc)
            {
                command.SelectedIndex = index;
                Click(Find<Button>(window, "LearnMidi"));
                midi.Input.Emit(0xc0 | (pc << 8)); Dispatcher.UIThread.RunJobs();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedInputStartupReleasesBothPortsAndKeepsEntry()
    {
        var midi = new FakeMidi { FailStart = true };
        var window = new SenderWindow(true, null, midi);
        window.Show();
        try
        {
            await WaitUntil(() => Find<ComboBox>(window, "MidiOutput").ItemCount == 1);
            Find<ComboBox>(window, "MidiInput").SelectedIndex = 1;
            window.Execute("4"); window.Execute("2");
            Click(Find<Button>(window, "ConnectMidi"));
            await WaitUntil(() => midi.Output.Disposed);
            Assert.True(midi.Input.Disposed);
            Assert.Empty(midi.Output.Messages);
            Assert.Equal("–42", Find<TextBlock>(window, "ProgramDisplay").Text);
            Assert.False(Find<Button>(window, "SendProgram").IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FailedSendDisconnectsWithoutClaimingSuccessOrLosingPendingNumber()
    {
        var midi = new FakeMidi { FailSend = true };
        var window = new SenderWindow(true, null, midi);
        window.Show();
        try
        {
            await WaitUntil(() => Find<ComboBox>(window, "MidiOutput").ItemCount == 1);
            Click(Find<Button>(window, "ConnectMidi"));
            await WaitUntil(() => Equals(Find<Button>(window, "ConnectMidi").Content, "Disconnect"));
            window.Execute("4"); window.Execute("2"); window.Execute("Send");
            Assert.True(midi.Output.Disposed); Assert.Empty(midi.Output.Messages);
            Assert.Equal("–42", Find<TextBlock>(window, "ProgramDisplay").Text);
            Assert.False(Find<Button>(window, "SendProgram").IsEnabled);
        }
        finally { window.Close(); }
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) { return; }
            await Task.Delay(10);
        }
        Assert.True(condition(), "Timed out waiting for MIDI setup.");
    }

    private sealed class FakeMidi : ISenderMidiBackend
    {
        public FakeInput Input { get; } = new();
        public FakeOutput Output { get; } = new();
        public bool FailStart { get => Input.FailStart; set => Input.FailStart = value; }
        public bool FailSend { get => Output.FailSend; set => Output.FailSend = value; }
        public IReadOnlyList<(string Id, string Name)> GetInputDevices() => [("controller", "Test controller")];
        public IReadOnlyList<(int Index, string Name)> GetOutputDevices() => [(0, "Test receiver")];
        public IMidiInputPort OpenInput(string id) => Input;
        public IMidiOutputPort OpenOutput(int index) => Output;
        public bool SupportsVirtualOutput { get; set; }
        public bool OpenedVirtual { get; private set; }
        public IMidiOutputPort OpenVirtualOutput() { OpenedVirtual = true; return Output; }
    }

    private sealed class FakeInput : IMidiInputPort
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailStart { get; set; }
        public event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
        public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived { add { } remove { } }
        public event EventHandler<string>? ErrorReceived { add { } remove { } }
        public void Emit(int raw) => MessageReceived?.Invoke(this, new(raw));
        public void Start()
        {
            if (FailStart) { throw new IOException("Simulated startup failure"); }
            Started = true;
        }
        public void Stop() => Started = false;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeOutput : IMidiOutputPort
    {
        public List<int> Messages { get; } = [];
        public bool FailSend { get; set; }
        public int FailAfter { get; set; } = int.MaxValue;
        public bool Disposed { get; private set; }
        public void Send(int message)
        {
            if (FailSend || Messages.Count >= FailAfter) { throw new IOException("Simulated output failure"); }
            Messages.Add(message);
        }
        public void SendBuffer(byte[] frame) => throw new InvalidOperationException("Generic sender must not send SysEx.");
        public void Dispose() => Disposed = true;
    }
}
