using System.Diagnostics;
using PresetMaestro.Core;

namespace PresetMaestro.Pilot;

internal static class NoMidiDesktopScenario
{
    internal static void Run(Checks report, Func<Process, IDesktopDriver> create)
    {
        foreach (string theme in new[] { "Dark", "Light" })
        {
            string profile = Path.Combine(report.Directory, "desktop-" + theme.ToLowerInvariant() + "-profile");
            Directory.CreateDirectory(profile);
            var store = new ProfileStore(profile);
            var settings = store.LoadSettings();
            settings.MidiInputPort = ""; settings.MidiOutputPort = ""; settings.ThruInputPorts = [];
            settings.Theme = theme; settings.AutoSend = false; settings.KeyboardEntryEnabled = true; settings.MidiEntryEnabled = false;
            settings.DisplayOffset = 0;
            store.SaveSettings(settings);
            string exe = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.CurrentDirectory, "bin", "app", "PresetMaestro.exe")
                : Path.Combine(Environment.CurrentDirectory, "bin", "Preset Maestro.app", "Contents", "MacOS", "PresetMaestro");
            var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["PRESET_MAESTRO_DATA_DIRECTORY"] = profile;
            // The production transport refuses enumeration/opening in this process, even if a UI regression tries to connect.
            start.Environment["PRESET_MAESTRO_DISABLE_MIDI"] = "1";
            using var app = Process.Start(start) ?? throw new IOException("Could not start packaged Maestro.");
            Task<string> stdout = app.StandardOutput.ReadToEndAsync(), stderr = app.StandardError.ReadToEndAsync();
            try
            {
                using var driver = create(app);
                report.Check(theme + ": launch disconnected with MIDI unavailable", () =>
                {
                    driver.Invoke("NavConfig");
                    Checks.Wait(() => driver.Text("Connect") == "Connect" && !driver.Enabled("Disconnect")
                        && driver.Text("MidiConnectionStatus").Contains("Not connected", StringComparison.Ordinal), "The no-MIDI application did not remain disconnected.");
                });
                report.Check(theme + ": real keyboard preset entry works while disconnected", () =>
                {
                    driver.Invoke("NavPresetSender");
                    Checks.Wait(() => driver.Text("ProgramDisplay") == "---", "The empty preset sender view did not become ready.");
                    driver.Type("170");
                    try { Checks.Wait(() => driver.Text("ProgramDisplay") == "170", "Entered preset is not displayed."); }
                    catch
                    {
                        Console.WriteLine("Preset display after typing: " + driver.Text("ProgramDisplay"));
                        driver.Capture(Path.Combine(report.Directory, "keyboard-failure-" + theme.ToLowerInvariant() + ".png"));
                        throw;
                    }
                    driver.Type("\n");
                    driver.Invoke("NavConfig");
                    Checks.Require(driver.Text("MidiConnectionStatus").Contains("Not connected", StringComparison.Ordinal), "The offline app unexpectedly connected.");
                });
                report.Check(theme + ": navigation and configuration remain accessible at narrow and normal widths", () =>
                {
                    driver.Invoke("NavFavorites"); driver.Invoke("NavConfig");
                    driver.Resize(1000, 800); driver.Capture(Path.Combine(report.Directory, "desktop-" + theme.ToLowerInvariant() + "-narrow.png"));
                    Checks.Require(driver.Text("Connect") == "Connect" && !driver.Enabled("Disconnect"), "Narrow layout changed the disconnected state.");
                    driver.Invoke("NavPresetSender"); driver.Resize(1440, 900);
                    driver.Capture(Path.Combine(report.Directory, "desktop-" + theme.ToLowerInvariant() + "-normal.png"));
                });
                report.Check(theme + ": clean shutdown preserves the isolated offline profile", () =>
                {
                    driver.Close(); Checks.Require(app.WaitForExit(10000) && app.ExitCode == 0, "Application did not close cleanly.");
                    var saved = store.LoadSettings();
                    Checks.Require(saved.Theme == theme && saved.MidiInputPort.Length == 0 && saved.MidiOutputPort.Length == 0 && saved.ThruInputPorts.Count == 0,
                        "Offline profile settings changed unexpectedly.");
                });
            }
            finally
            {
                if (!app.HasExited) { app.Kill(true); app.WaitForExit(5000); }
                File.WriteAllText(Path.Combine(report.Directory, "desktop-" + theme + ".stdout.txt"), stdout.GetAwaiter().GetResult());
                File.WriteAllText(Path.Combine(report.Directory, "desktop-" + theme + ".stderr.txt"), stderr.GetAwaiter().GetResult());
            }
        }
    }
}
