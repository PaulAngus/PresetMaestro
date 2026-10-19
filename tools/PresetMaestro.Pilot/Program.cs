using System.Diagnostics;
using System.Text.Json;

namespace PresetMaestro.Pilot;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 3 || args[1] != "--result") { Console.Error.WriteLine("Use native|desktop --result PATH"); return 1; }
        var report = new Checks(args[2]);
        Directory.CreateDirectory(report.Directory);
        try
        {
            if (args[0] == "native")
            {
#if MAESTRO_PHYSICAL_HARDWARE
                throw new PrerequisiteException("This desktop driver contains no MIDI scenarios. Use the separate existing-FM9 read-only stage.");
#else
                Native.Run(report).GetAwaiter().GetResult();
#endif
            }
            else if (args[0] == "desktop") { Desktop.Run(report); }
            else { throw new ArgumentException("Unknown suite."); }
        }
        catch (PrerequisiteException error) { report.Unavailable(error.Message); }
        catch (Exception error) { report.Failure(error); Console.Error.WriteLine(error); }
        return report.Save();
    }
}

internal sealed class PrerequisiteException(string message) : Exception(message);

internal sealed class Checks(string result)
{
    private readonly List<object> checks = [];
    private string status = "passed";
#if MAESTRO_MACOS
    internal string ResultPath { get; } = Path.GetFullPath(result);
    internal void Import(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        status = document.RootElement.GetProperty("status").GetString()!;
        checks.AddRange(document.RootElement.GetProperty("checks").EnumerateArray().Select(item => (object)item.Clone()));
    }
#endif
    internal string Directory { get; } = Path.GetDirectoryName(Path.GetFullPath(result))!;
    internal void Check(string name, Action action)
    {
        var timer = Stopwatch.StartNew();
        try { action(); checks.Add(new { name, status = "passed", seconds = timer.Elapsed.TotalSeconds }); Console.WriteLine("PASS " + name); }
        catch (Exception error) { checks.Add(new { name, status = "failed", seconds = timer.Elapsed.TotalSeconds, error = error.ToString() }); throw; }
    }
    internal void Failure(Exception error) { status = "failed"; checks.Add(new { name = "adapter", status, error = error.ToString() }); }
    internal void Unavailable(string reason) { status = "incomplete"; checks.Add(new { name = "prerequisite", status, reason }); }
    internal int Save()
    {
        if (checks.Count == 0) { Failure(new IOException("No checks ran.")); }
        System.IO.Directory.CreateDirectory(Directory);
#if MAESTRO_PHYSICAL_HARDWARE
        string scope = "Physical host desktop checks with MIDI disabled for the application process. No MIDI ports are opened or created, and no MIDI messages are sent.";
#else
        string scope = "Real native MIDI over dedicated virtual ports and packaged desktop automation; physical-device interoperability is separate.";
#endif
        File.WriteAllText(result, JsonSerializer.Serialize(new { status, total = checks.Count, checks, scope }, Program.Json));
        return status == "passed" ? 0 : status == "incomplete" ? 2 : 1;
    }
    internal static void Require(bool condition, string message) { if (!condition) { throw new IOException(message); } }
    internal static void Wait(Func<bool> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(20)) { throw new TimeoutException(description); } Thread.Sleep(80); }
    }
}
