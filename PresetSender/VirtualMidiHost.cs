#if MAESTRO_WINDOWS
using System.Diagnostics;
using System.Globalization;
using PresetMaestro.Midi;

namespace PresetSender;

// Native service calls can block indefinitely on incompatible Windows builds.
// Isolate them so a failed connection can be closed without orphaning a worker.
internal sealed class VirtualMidiHost : IMidiOutputPort
{
    private readonly Process _process;
    private bool _disposed;

    private VirtualMidiHost(Process process) => _process = process;

    public static VirtualMidiHost Open()
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PresetSender.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("--virtual-midi-host");
        var host = new VirtualMidiHost(Process.Start(start) ?? throw new IOException("Could not start the virtual MIDI host."));
        try
        {
            host.Expect("READY", TimeSpan.FromSeconds(15));
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public void Send(int message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _process.StandardInput.WriteLine(message.ToString(CultureInfo.InvariantCulture));
        _process.StandardInput.Flush();
        Expect("OK", TimeSpan.FromSeconds(3));
    }

    private void Expect(string expected, TimeSpan timeout)
    {
        string? response;
        try { response = _process.StandardOutput.ReadLineAsync().WaitAsync(timeout).GetAwaiter().GetResult(); }
        catch (TimeoutException ex)
        {
            throw new IOException("Windows MIDI Services did not respond. Update Windows and its MIDI Services components, or select an existing MIDI output.", ex);
        }
        if (response != expected)
        {
            throw new IOException(response?.StartsWith("ERROR ", StringComparison.Ordinal) == true ? response[6..] : "The virtual MIDI host closed unexpectedly.");
        }
    }

    public void SendBuffer(byte[] frame) => throw new NotSupportedException("Preset Sender sends only Program Change messages.");

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                // EOF requests normal session disposal and endpoint removal.
                _process.StandardInput.Close();
                if (!_process.WaitForExit(1000))
                {
                    _process.Kill();
                    _process.WaitForExit(1000);
                }
            }
        }
        finally { _process.Dispose(); }
    }

    internal static void Run()
    {
        try
        {
            using var output = WindowsVirtualMidiOutput.Open();
            Reply("READY");
            while (Console.ReadLine() is { } line)
            {
                output.Send(int.Parse(line, CultureInfo.InvariantCulture));
                Reply("OK");
            }
        }
        catch (Exception ex) { Reply("ERROR " + ex.Message.ReplaceLineEndings(" ")); }
    }

    private static void Reply(string message) { Console.WriteLine(message); Console.Out.Flush(); }
}
#endif
