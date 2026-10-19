using System.Diagnostics;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetNameSync.Core;

namespace PresetMaestro.Pilot;

internal static class DesktopScenario
{
    internal static void Run(Checks report, Func<Process, IDesktopDriver> create)
    {
        var peer = new MidiPeer(report.Directory) { EmulateDevice = true };
        try
        {
            foreach (string theme in new[] { "Dark", "Light" })
            {
                string profile = Path.Combine(report.Directory, "desktop-" + theme.ToLowerInvariant() + "-profile");
                Directory.CreateDirectory(profile);
                var store = new ProfileStore(profile);
                var settings = store.LoadSettings();
                settings.MidiInputPort = peer.Name; settings.MidiOutputPort = peer.Name;
                settings.MidiChannel = 6; settings.DisplayOffset = 0; settings.Theme = theme;
                settings.AutoSend = false; settings.KeyboardEntryEnabled = true;
                // A complete synthetic saved library exercises the returning-user connection flow.
                var device = new IndexDevice(Guid.NewGuid(), "Synthetic FM3", FractalDeviceVariant.FM3, "9.00");
                new IndexLibrary(Path.Combine(profile, "FractalIndex")).Save(new DeviceIndex
                {
                    Device = device,
                    PresetMapping = new DevicePresetMapping(6),
                    Committed = new IndexScan
                    {
                        Status = "Complete",
                        Firmware = "9.00",
                        FinishedAt = DateTimeOffset.UtcNow,
                        Presets = Enumerable.Range(0, 512).ToDictionary(slot => slot, slot => new IndexedPreset(
                            FractalDeviceVariant.FM3, slot, "Test preset " + slot,
                            Enumerable.Range(1, 8).Select(scene => "Scene " + scene).ToArray(), [], new string('A', 64)))
                    }
                });
                settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
                store.SaveSettings(settings);
                store.SaveFavorites(settings.ActiveProfile, [new Favorite { Id = 1, Slot = 1, Name = "Native test scene", Preset = 170, Scene = 8, Tags = ["Test"] }]);
                string exe = Path.Combine(Environment.CurrentDirectory, "bin", "app", "PresetMaestro" + (OperatingSystem.IsWindows() ? ".exe" : ""));
                var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment["PRESET_MAESTRO_DATA_DIRECTORY"] = profile;
                using var app = Process.Start(start) ?? throw new IOException("Could not start packaged Maestro.");
                Task<string> stdout = app.StandardOutput.ReadToEndAsync(), stderr = app.StandardError.ReadToEndAsync();
                try
                {
                    using var driver = create(app);
                    report.Check(theme + ": launch and connect the packaged app to a synthetic FM3", () =>
                    {
                        Checks.Wait(() => driver.Text("MidiInput") == peer.Name && driver.Text("MidiOutput") == peer.Name, "Selected test MIDI ports did not appear in the UI.");
                        Checks.Wait(() => driver.Enabled("Connect"), "Connect did not become available.");
                        driver.Invoke("Connect");
                        try { Checks.Wait(() => driver.Text("Connect") == "Connected" && !driver.Enabled("Connect"), "Device handshake did not validate."); }
                        catch { driver.Capture(Path.Combine(report.Directory, "connection-failure.png")); throw; }
                        Checks.Wait(() => driver.Text("ConnectionSyncDone") != "Disconnect", "Saved library did not permit dismissing the sync dialog.");
                        driver.Invoke("ConnectionSyncDone");
                        Checks.Require(driver.Enabled("Disconnect"), "Disconnect is unavailable after connection.");
                    });
                    report.Check(theme + ": physical keyboard entry sends bank/program exactly once", () =>
                    {
                        driver.Invoke("NavPresetSender");
                        peer.ShortMessages.Clear();
                        driver.Type("170");
                        Checks.Wait(() => driver.Text("ProgramDisplay") == "170", "Entered preset is not displayed.");
                        driver.Type("\n");
                        Expect(peer, [0xb5, 0, 1], [0xc5, 42]);
                    });
                    report.Check(theme + ": navigation sends the next preset through native MIDI", () =>
                    {
                        peer.ShortMessages.Clear(); driver.Invoke("KeyNEXT");
                        Expect(peer, [0xb5, 0, 1], [0xc5, 43]);
                    });
                    report.Check(theme + ": saved favorite sends bank/program/scene", () =>
                    {
                        driver.Invoke("NavFavorites"); peer.ShortMessages.Clear();
                        driver.Type("1\n");
                        Expect(peer, [0xb5, 0, 1], [0xc5, 42], [0xb5, 34, 7]);
                    });
                    report.Check(theme + ": configuration and sender remain accessible at narrow and normal widths", () =>
                    {
                        driver.Invoke("NavConfig");
                        driver.Resize(1000, 800); driver.Capture(Path.Combine(report.Directory, "desktop-" + theme.ToLowerInvariant() + "-narrow.png"));
                        Checks.Require(driver.Enabled("Disconnect"), "Narrow connection control is unavailable.");
                        driver.Invoke("NavPresetSender"); driver.Resize(1440, 900);
                        driver.Capture(Path.Combine(report.Directory, "desktop-" + theme.ToLowerInvariant() + "-normal.png"));
                    });
                    report.Check(theme + ": disconnect prevents further MIDI sends and closes cleanly", () =>
                    {
                        driver.Invoke("NavConfig"); driver.Invoke("Disconnect");
                        Checks.Wait(() => driver.Text("MidiConnectionStatus").Contains("Not connected", StringComparison.Ordinal), "Disconnect did not complete.");
                        driver.Invoke("NavPresetSender"); peer.ShortMessages.Clear(); driver.Type("002\n"); Thread.Sleep(300);
                        Checks.Require(peer.ShortMessages.IsEmpty, "Disconnected UI emitted MIDI.");
                        driver.Close(); Checks.Require(app.WaitForExit(10000) && app.ExitCode == 0, "Application did not close cleanly.");
                    });
                    var saved = store.LoadSettings();
                    Checks.Require(saved.MidiChannel == 6 && saved.Theme == theme, "Isolated settings were not preserved.");
                }
                finally
                {
                    if (!app.HasExited) { app.Kill(true); app.WaitForExit(5000); }
                    File.WriteAllText(Path.Combine(report.Directory, "desktop-" + theme + ".stdout.txt"), stdout.GetAwaiter().GetResult());
                    File.WriteAllText(Path.Combine(report.Directory, "desktop-" + theme + ".stderr.txt"), stderr.GetAwaiter().GetResult());
                }
            }
        }
        finally { peer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static void Expect(MidiPeer peer, params byte[][] expected)
    {
        Checks.Wait(() => peer.ShortMessages.Count >= expected.Length, "Expected UI-generated MIDI did not arrive.");
        Thread.Sleep(200);
        var actual = peer.ShortMessages.ToArray();
        Checks.Require(actual.Length == expected.Length && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)),
            "UI MIDI differs: " + string.Join(", ", actual.Select(Convert.ToHexString)));
    }
}
