using ApplicationTestLab;
#if MAESTRO_PHYSICAL_HARDWARE
using System.Diagnostics;
using System.Text.Json;
#endif

string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
var stages = new List<LabStage>
{
    new("functional-presetmaestro-tests", "Functional", "bin/tests/PresetMaestro.LabTests" + suffix, ["PresetMaestro.Tests", "{result}"])
};
#if MAESTRO_PHYSICAL_HARDWARE
if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("Use this existing-connection profile only on Windows or macOS.");
}
int rootIndex = Array.IndexOf(args, "--root");
string root = Path.GetFullPath(rootIndex >= 0 ? args.ElementAtOrDefault(rootIndex + 1) ?? throw new ArgumentException("Missing --root value.")
    : Path.Combine(AppContext.BaseDirectory, "..", ".."));
string connectionFile = Path.Combine(root, "physical-midi.json");
if (File.Exists(connectionFile))
{
    using var connection = JsonDocument.Parse(File.ReadAllText(connectionFile));
    string input = connection.RootElement.GetProperty("inputPort").GetString() ?? "";
    string output = connection.RootElement.GetProperty("outputPort").GetString() ?? "";
    if (!input.Contains("FM9", StringComparison.OrdinalIgnoreCase) || !output.Contains("FM9", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("Select the existing FM9 input and output in physical-midi.json.");
    }
    stages.Add(new("native-fm9-read-only", "Native", "bin/tests/PresetMaestro.LabTests" + suffix,
        ["PresetMaestro.Tests", "{result}", "PhysicalFM9", input, output]));
}
else
{
    stages.Add(new("native-fm9-read-only", "Native", UnavailableReason: "Select existing real FM9 input/output names in physical-midi.json. No MIDI connections were opened."));
}
string physicalPilot = OperatingSystem.IsWindows() ? "bin/pilot/PresetMaestro.Pilot.exe" : "bin/Maestro Physical Desktop Tests.app/Contents/MacOS/PresetMaestro.Pilot";
stages.Add(new("desktop-no-midi", "Desktop", physicalPilot, ["desktop", "--result", "{result}"],
    ProgressFromOutput: OperatingSystem.IsWindows(), ProgressLog: OperatingSystem.IsMacOS() ? "mac-desktop.stdout.txt" : null));
string[] limits =
[
    "Physical host profile: offline functional tests and explicitly selected existing real FM9 connections only. No virtual MIDI peers or IAC setup are packaged.",
    "Native runs the existing read-only FM9 boundary/repeat/cancellation/reopen checks, with unchanged preset/scene assertions. No navigation, full-library scan, or device writes are enabled.",
    "Desktop runs eight no-MIDI UI scenarios. Its packaged application refuses native MIDI access in that process; the driver contains no virtual-peer scenarios. An unlocked desktop is required; Mac also requires its initial control/recording permissions."
];
#else
string pilot = OperatingSystem.IsMacOS() ? "bin/Maestro Desktop Tests.app/Contents/MacOS/PresetMaestro.Pilot" : "bin/pilot/PresetMaestro.Pilot.exe";
stages.Add(new("native-midi", "Native", pilot, ["native", "--result", "{result}"], ProgressFromOutput: true));
stages.Add(new("desktop", "Desktop", pilot, ["desktop", "--result", "{result}"],
    ProgressFromOutput: OperatingSystem.IsWindows(), ProgressLog: OperatingSystem.IsMacOS() ? "mac-desktop.stdout.txt" : null));
string[] limits =
[
    "Functional uses the existing offline tests. Native and desktop use dedicated synthetic MIDI peers; physical Fractal hardware is not exercised.",
    "Desktop launches the packaged application with isolated settings. Windows needs an unlocked desktop; Mac also needs Accessibility and Screen Recording permissions.",
    "These are application regression checks, not physical-device interoperability or installation/upgrade qualification."
];
#endif
#if MAESTRO_PHYSICAL_MAC
Process? awake = null;
#endif
#if MAESTRO_PHYSICAL_WINDOWS
WindowsAwakeAssertion? awake = null;
#endif
try
{
#if MAESTRO_PHYSICAL_MAC
    int suiteIndex = Array.IndexOf(args, "--suite");
    string suite = suiteIndex < 0 ? "All" : args.ElementAtOrDefault(suiteIndex + 1) ?? "";
    if (args.FirstOrDefault() == "run" && suite is "All" or "Desktop")
    {
        var keepAwake = new ProcessStartInfo("/usr/bin/caffeinate") { UseShellExecute = false };
        foreach (string value in new[] { "-d", "-i", "-u", "-t", "900", "-w", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) }) { keepAwake.ArgumentList.Add(value); }
        awake = Process.Start(keepAwake) ?? throw new IOException("Could not hold the test host awake.");
        if (awake.WaitForExit(100)) { throw new IOException("The temporary test-host awake assertion failed."); }
    }
#endif
#if MAESTRO_PHYSICAL_WINDOWS
    int suiteIndex = Array.IndexOf(args, "--suite");
    string suite = suiteIndex < 0 ? "All" : args.ElementAtOrDefault(suiteIndex + 1) ?? "";
    if (args.FirstOrDefault() == "run" && suite is "All" or "Desktop") { awake = new WindowsAwakeAssertion(); }
#endif
    return await PortableLab.RunAsync(args, new LabApplication("preset-maestro", "PresetMaestro", stages.ToArray(),
        limits,
        new Dictionary<string, string?>
        {
            ["PRESET_MAESTRO_HARDWARE"] = null,
            ["PRESET_MAESTRO_COMPATIBILITY"] = null,
            ["PRESET_MAESTRO_ALLOW_NAVIGATION"] = null,
#if MAESTRO_PHYSICAL_HARDWARE
            ["PRESET_MAESTRO_DISABLE_MIDI"] = "1",
#endif
            ["PRESET_MAESTRO_SCREENSHOT_DIR"] = "{output}/screenshots",
            ["MIDI_FORWARDER_UI_SNAPSHOTS"] = "{output}/screenshots",
            ["PRESET_MAESTRO_DATA_DIRECTORY"] = "{output}/profile",
            ["MBU_MIDI_BACKEND"] = null,
            ["MBU_MIDI_API_DIRECTORY"] = null,
            ["MIDI_FORWARDER_MIDI_API"] = null
        }, RunTimeout: TimeSpan.FromMinutes(30)));
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
#if MAESTRO_PHYSICAL_WINDOWS
finally { awake?.Dispose(); }
#endif
#if MAESTRO_PHYSICAL_MAC
finally
{
    if (awake is not null)
    {
        if (!awake.HasExited) { awake.Kill(); awake.WaitForExit(5000); }
        awake.Dispose();
    }
}
#endif
