using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: AvaloniaTestApplication(typeof(PresetSender.Tests.TestApplication))]

namespace PresetSender.Tests;

public sealed class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SenderApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class SenderTests
{
    [Fact]
    public void SettingsRetainAllChannelsAndDistinguishLegacyHardwareFromVirtualChoice()
    {
        var legacy = SenderSettings.FromJson("""{"OutputName":"USB MIDI","OutputChannel":6}""");
        Assert.Null(legacy.VirtualOutput);
        Assert.Equal("USB MIDI", legacy.OutputName);
        Assert.Equal(6, legacy.OutputChannel);
        var broadcast = SenderSettings.FromJson("""{"VirtualOutput":true,"OutputChannel":0}""");
        Assert.True(broadcast.VirtualOutput);
        Assert.Equal(0, broadcast.OutputChannel);
        Assert.Equal(1, SenderSettings.FromJson("{}").OutputChannel);
    }

    [Fact]
    public void ExtraDigitsCannotSilentlyReplaceThePendingProgram()
    {
        var state = new SenderState();
        Assert.True(state.Digit(1)); Assert.True(state.Digit(2)); Assert.True(state.Digit(7));
        Assert.False(state.Digit(4)); Assert.Equal("127", state.Entry);
        state.Backspace(); Assert.True(state.Digit(3)); Assert.Equal("123", state.Entry);
        state.Sent(123); Assert.True(state.Digit(7)); Assert.Equal("7", state.Entry);
    }
    [Theory]
    [InlineData(0, "00", 0)]
    [InlineData(0, "42", 42)]
    [InlineData(0, "127", 127)]
    [InlineData(1, "42", 41)]
    [InlineData(1, "128", 127)]
    public void DisplayNumberMapsToWireProgram(int offset, string entry, int expected)
    {
        var state = new SenderState { NumberingOffset = offset };
        foreach (char digit in entry)
        {
            state.Digit(digit - '0');
        }

        Assert.True(state.TryGetProgram(out int program)); Assert.Equal(expected, program);
    }

    [Theory]
    [InlineData(0, "128")]
    [InlineData(1, "000")]
    [InlineData(1, "129")]
    public void InvalidProgramIsNotSendable(int offset, string entry)
    {
        var state = new SenderState { NumberingOffset = offset };
        foreach (char digit in entry)
        {
            state.Digit(digit - '0');
        }

        Assert.False(state.TryGetProgram(out _));
    }

    [Fact]
    public void MidiButtonsFireOnceUntilReleasedAndRespectChannel()
    {
        var state = new SenderState { InputChannel = 2 };
        state.Bindings.Add(new(0x90, 2, 60, "4"));
        Assert.Null(state.Receive(0x90 | (60 << 8) | (127 << 16), out _));
        int note = 0x91 | (60 << 8) | (127 << 16);
        Assert.Equal("4", state.Receive(note, out _));
        Assert.Null(state.Receive(note, out _));
        Assert.Null(state.Receive(0x81 | (60 << 8) | (40 << 16), out _));
        Assert.Equal("4", state.Receive(note, out _));
        state.Receive(0x91 | (60 << 8), out _);
        Assert.Equal("4", state.Receive(note, out _));
        state.Bindings.Add(new(0xb0, 2, 20, "Send"));
        Assert.Equal("Send", state.Receive(0xb1 | (20 << 8) | (127 << 16), out _));
        Assert.Null(state.Receive(0xb1 | (20 << 8) | (64 << 16), out _));
        state.Receive(0xb1 | (20 << 8), out _);
        Assert.Equal("Send", state.Receive(0xb1 | (20 << 8) | (127 << 16), out _));
    }

    [Fact]
    public void IncomingProgramChangeRequiresOptInAndOtherMessagesAreIgnored()
    {
        var state = new SenderState();
        Assert.Null(state.Receive(0xc5 | (42 << 8), out _));
        state.ForwardProgramChanges = true;
        Assert.Equal("PC:42", state.Receive(0xc5 | (42 << 8), out _));
        Assert.Null(state.Receive(0xe0 | (50 << 8) | (100 << 16), out _));
        state.Bindings.Add(new(0xc0, 6, 42, "4"));
        Assert.Equal("4", state.Receive(0xc5 | (42 << 8), out var pressed));
        Assert.Equal(0xc0, pressed!.Kind);
        Assert.Equal("4", state.Receive(0xc5 | (42 << 8), out _));
    }

    [AvaloniaFact]
    public void KeyboardEntrySendsOnlyOnConfirmationAndDoesNotStealConfigurationKeys()
    {
        var writes = new List<int>();
        var window = new SenderWindow(true, writes.Add);
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var keypad = window.GetVisualDescendants().OfType<StackPanel>().First(c => c.Name == "SenderKeypad"); keypad.Focus();
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Digit4, Avalonia.Input.RawInputModifiers.None); window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Digit4, Avalonia.Input.RawInputModifiers.None);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Digit2, Avalonia.Input.RawInputModifiers.None); window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Digit2, Avalonia.Input.RawInputModifiers.None);
            Assert.Empty(writes);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None); window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(new[] { 0xc0 | (42 << 8) }, writes);
            Assert.Equal("–––", window.GetVisualDescendants().OfType<TextBlock>().First(c => c.Name == "ProgramDisplay").Text);
            foreach (var key in new[] { Avalonia.Input.PhysicalKey.Digit1, Avalonia.Input.PhysicalKey.Digit2, Avalonia.Input.PhysicalKey.Digit7 })
            {
                window.KeyPressQwerty(key, Avalonia.Input.RawInputModifiers.None);
                window.KeyReleaseQwerty(key, Avalonia.Input.RawInputModifiers.None);
            }
            Assert.Single(writes);
            Assert.Equal("127", window.GetVisualDescendants().OfType<TextBlock>().First(c => c.Name == "ProgramDisplay").Text);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(0xc0 | (127 << 8), writes[1]);
            window.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "MidiOutput").Focus();
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Digit7, Avalonia.Input.RawInputModifiers.None); window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Digit7, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal("–––", window.GetVisualDescendants().OfType<TextBlock>().First(c => c.Name == "ProgramDisplay").Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(460, "Light", 740)]
    [InlineData(360, "Light", 740)]
    [InlineData(460, "Dark", 740)]
    [InlineData(360, "Dark", 740)]
    [InlineData(360, "Light", 520)]
    [InlineData(360, "Dark", 520)]
    public void RenderSenderAndExpandedControls(int width, string theme, int height)
    {
        var window = new SenderWindow(true, null) { Width = width, Height = height, RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            string? directory = Environment.GetEnvironmentVariable("PRESET_SENDER_SCREENSHOT_DIR");
            if (directory is null)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            Save("sender");
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = true; }
            Dispatcher.UIThread.RunJobs();
            var viewer = window.GetVisualDescendants().OfType<ScrollViewer>().First();
            viewer.Offset = new Vector(0, viewer.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Save("controls");
            foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = Equals(expander.Header, "Program options"); }
            Dispatcher.UIThread.RunJobs();
            viewer.Offset = new Vector(0, viewer.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Save("options");

            void Save(string view)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame); frame.Save(Path.Combine(directory, $"{view}-{width}-{height}-{theme}.png"));
            }
        }
        finally { window.Close(); }
    }
}


