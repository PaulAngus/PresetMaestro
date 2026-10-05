using NAudio.Midi;
using PresetMaestro.Midi;

namespace PresetMaestro.Tests;

public sealed class MidiManagerTests
{
    private static MidiManager Create(Input input, Output output, Func<string, IMidiInput>? createInput = null) => new(
        () => [("main", "Main"), ("thru", "Controller")], createInput ?? (_ => input),
        () => [(7, "Device")], index => { Assert.Equal(7, index); return output; });

    [Fact]
    public void SendsExactBankProgramSceneAndSysexMessagesAndRejectsInvalidValues()
    {
        var output = new Output();
        using var manager = Create(new Input(), output);
        Assert.False(manager.SendBankAndPC(0, 1, 1));
        Assert.False(manager.SendFavorite(0, 1, 2, 34, 1));
        Assert.False(manager.SendScene(1, 34, 1));
        Assert.False(manager.SendSysEx([0xf0, 0xf7]));
        Assert.True(manager.OpenOutput("device", out var error)); Assert.Null(error);
        Assert.True(manager.SendFavorite(2, 5, 8, 34, 16));
        Assert.Equal([0x0200bf, 0x05cf, 0x0722bf], output.Messages);
        Assert.True(manager.SendBankAndPC(1, 127, 1));
        Assert.True(manager.SendScene(3, 34, 2));
        Assert.True(manager.SendSysEx([0xf0, 1, 0xf7]));
        Assert.Equal([0xf0, 1, 0xf7], Assert.Single(output.Frames));
        int sent = output.Messages.Count;
        Assert.False(manager.SendBankAndPC(-1, 0, 1));
        Assert.False(manager.SendBankAndPC(0, 128, 1));
        Assert.False(manager.SendBankAndPC(0, 0, 17));
        Assert.False(manager.SendFavorite(128, 0, 1, 34, 1));
        Assert.False(manager.SendFavorite(0, 0, 9, 34, 1));
        Assert.False(manager.SendFavorite(0, 0, 1, 128, 1));
        Assert.False(manager.SendScene(0, 34, 1));
        Assert.Equal(sent, output.Messages.Count);
    }

    [Fact]
    public void InputRoutesNotesPresetChangesAndSysexButIgnoresClosedPortCallbacks()
    {
        var input = new Input();
        using var manager = Create(input, new Output());
        var notes = new List<NoteOnEventArgs>(); var presets = new List<int>(); var frames = new List<byte[]>();
        manager.NoteOnReceived += (_, note) => notes.Add(note);
        manager.PresetChangeReceived += (_, channel) => presets.Add(channel);
        manager.SysexMessageReceived += (_, frame) => frames.Add(frame);
        Assert.True(manager.OpenInput("Main", out _));
        input.Emit(0x643c92); input.Emit(0x003c92); input.Emit(0x003c82); input.Emit(0xf8); input.Emit(0x01c2);
        input.EmitSysex([0xf0, 0x01, 0xf7]);
        var note = Assert.Single(notes);
        Assert.Equal((60, 100, 3, "Main"), (note.NoteNumber, note.Velocity, note.Channel, note.SourcePort));
        Assert.Equal([3], presets); Assert.Single(frames);
        var late = input.Capture();
        manager.CloseInput(); late?.Invoke(input, new(0x643c92, TimeSpan.Zero));
        Assert.Single(notes); Assert.True(input.Disposed); Assert.False(manager.InputOpen);
    }

    [Fact]
    public void ThruPreservesRawMessagesAndSourceAndIgnoresClockAndClosedPorts()
    {
        var input = new Input(); var output = new Output();
        using var manager = Create(input, output);
        var notes = new List<NoteOnEventArgs>(); var presets = new List<int>();
        manager.NoteOnReceived += (_, note) => notes.Add(note);
        manager.PresetChangeReceived += (_, channel) => presets.Add(channel);
        Assert.True(manager.OpenOutput("Device", out _));
        Assert.True(manager.OpenThruInput("Controller", out _));
        Assert.True(manager.OpenThruInput("Controller", out _));
        Assert.Equal(1, input.Starts);
        input.Emit(0x643c93); input.Emit(0x403db3); input.Emit(0x06c3); input.Emit(0xf8); input.Emit(0xfe);
        Assert.Equal([0x643c93, 0x403db3, 0x06c3], output.Messages);
        Assert.Equal("Controller", Assert.Single(notes).SourcePort); Assert.Equal([4], presets);
        var late = input.Capture();
        var listed = manager.ThruInputPorts;
        manager.CloseThruInput("Controller");
        late?.Invoke(input, new(0x643c93, TimeSpan.Zero));
        Assert.Equal(3, output.Messages.Count);
        Assert.Single(listed); Assert.Empty(manager.ThruInputPorts); Assert.True(input.Disposed);
    }

    [Fact]
    public async Task ClosingOutputWaitsForAnInFlightFavoriteSend()
    {
        var output = new Output();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var manager = Create(new Input(), output);
        manager.OpenOutput("Device", out _);
        output.OnSend = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
        var sending = Task.Run(() => manager.SendFavorite(1, 2, 3, 34, 1));
        Task? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            closing = Task.Run(() => { closeStarted.SetResult(); manager.CloseOutput(); });
            await closeStarted.Task;
            await Task.Delay(50);
            Assert.False(output.Disposed);
            Assert.False(closing.IsCompleted);
        }
        finally
        {
            release.Set();
            await sending.WaitAsync(TimeSpan.FromSeconds(5));
            if (closing is not null) { await closing.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        Assert.True(await sending); Assert.True(output.Disposed); Assert.Equal(3, output.Messages.Count);
        Assert.False(manager.SendScene(1, 34, 1));
    }

    [Fact]
    public async Task ThruCallbacksCanRacePortRefreshWithoutEnumeratingAMutatingDictionary()
    {
        var output = new Output(); var inputs = new System.Collections.Concurrent.ConcurrentQueue<Input>();
        using var manager = Create(new Input(), output, _ => { var input = new Input(); inputs.Enqueue(input); return input; });
        manager.OpenOutput("Device", out _);
        Assert.True(manager.OpenThruInput("Controller", out _));
        var firstCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var callbacks = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var input in inputs) { input.Emit(0x403cb1); firstCallback.TrySetResult(); }
                Thread.Yield();
            }
        });
        try
        {
            await firstCallback.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 100; i++)
            {
                Assert.True(manager.OpenThruInput("Controller", out _));
                manager.CloseAllThruInputs();
            }
        }
        finally { stop.Cancel(); await callbacks.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Empty(manager.ThruInputPorts);
        Assert.All(inputs, input => Assert.True(input.Disposed));
        Assert.NotEmpty(output.Messages);
        Assert.All(output.Messages, message => Assert.Equal(0x403cb1, message));
    }

    [Fact]
    public void DriverFailuresCleanUpPortsAndSendingReportsFailure()
    {
        var input = new Input { OnStart = () => throw new IOException("Input failed") };
        var output = new Output { OnSend = () => throw new IOException("Output failed") };
        using var manager = Create(input, output);
        Assert.False(manager.OpenInput("Main", out var error)); Assert.Equal("Input failed", error);
        Assert.False(manager.InputOpen); Assert.True(input.Disposed);
        Assert.False(manager.OpenThruInput("Controller", out _)); Assert.Empty(manager.ThruInputPorts);
        Assert.False(manager.OpenInput("Missing", out _)); Assert.False(manager.OpenOutput("Missing", out _));
        Assert.False(manager.OpenThruInput("Missing", out _));
        Assert.True(manager.OpenOutput("Device", out _));
        Assert.False(manager.SendBankAndPC(0, 1, 1));
        Assert.False(manager.SendFavorite(0, 1, 2, 34, 1));
        Assert.False(manager.SendScene(1, 34, 1));
        Assert.False(manager.SendSysEx([0xf0, 0xf7]));
        manager.Dispose(); manager.Dispose();
        Assert.True(output.Disposed);
        Assert.Throws<ObjectDisposedException>(() => manager.OpenInput("Main", out _));
        Assert.Throws<ObjectDisposedException>(() => manager.OpenOutput("Device", out _));
        Assert.Throws<ObjectDisposedException>(() => manager.OpenThruInput("Controller", out _));
    }

    private sealed class Input : IMidiInput
    {
        public event EventHandler<MidiInMessageEventArgs>? MessageReceived;
        public event EventHandler<MidiInSysexMessageEventArgs>? SysexMessageReceived;
        public Action? OnStart { get; init; }
        public int Starts { get; private set; }
        public bool Disposed { get; private set; }
        public void Start() { Starts++; OnStart?.Invoke(); }
        public void Stop() { }
        public void Dispose() => Disposed = true;
        public void Emit(int message) => MessageReceived?.Invoke(this, new(message, TimeSpan.Zero));
        public void EmitSysex(byte[] frame) => SysexMessageReceived?.Invoke(this, new(frame, TimeSpan.Zero));
        public EventHandler<MidiInMessageEventArgs>? Capture() => MessageReceived;
    }

    private sealed class Output : IMidiOutput
    {
        public List<int> Messages { get; } = [];
        public List<byte[]> Frames { get; } = [];
        public Action? OnSend { get; set; }
        public bool Disposed { get; private set; }
        public void Send(int message) { OnSend?.Invoke(); ObjectDisposedException.ThrowIf(Disposed, this); Messages.Add(message); }
        public void SendBuffer(byte[] frame) { OnSend?.Invoke(); ObjectDisposedException.ThrowIf(Disposed, this); Frames.Add(frame); }
        public void Dispose() => Disposed = true;
    }
}
