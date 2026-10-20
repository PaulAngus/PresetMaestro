using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace PresetMaestro.Tests;

[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")]
internal sealed class Fm9ApplicationHardwareFactAttribute : FactAttribute
{
    public Fm9ApplicationHardwareFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PRESET_MAESTRO_HARDWARE") != "FM9" ||
            Environment.GetEnvironmentVariable("PRESET_MAESTRO_ALLOW_NAVIGATION") != "1")
        { Skip = "Physical FM9 application acceptance requires Test-Hardware.ps1 -AllowNavigation -SceneCc <assignment>."; }
    }
}

[Collection("Physical MIDI"), Trait("Category", "Hardware"), Trait("Capability", "ChangesPresetsAndScenes")]
public sealed class Fm9ApplicationHardwareTests(ITestOutputHelper output)
{
    private static readonly int[] BoundarySlots = [0, 1, 126, 127, 128, 255, 256, 383, 384, 510, 511];
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static Button FindButton(MainWindow window, string name) => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    private static void Press(MainWindow window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
    private static void TypeAndSend(MainWindow window, int displayed)
    {
        foreach (char digit in displayed.ToString()) { Press(window, Enum.Parse<PhysicalKey>("Digit" + digit)); }
        Press(window, PhysicalKey.Enter);
    }

    private sealed class Acceptance(MainWindow window, HardwareSession session, AppSettings settings, CancellationToken token)
    {
        public MainWindow Window { get; } = window;
        public HardwareSession Session { get; } = session;
        public AppSettings Settings { get; } = settings;
        public List<object> Outcomes { get; } = [];
        public PresetNameClient Client => Field<PresetNameClient>(Window, "_presetNameClient");
        public CancellationToken Token { get; } = token;

        public async Task Verify(string action, int slot, int? scene = null)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var timer = Stopwatch.StartNew();
            string last = "No state received";
            try
            {
                while (true)
                {
                    try
                    {
                        // Read through the application's own serialized client, so its
                        // automatic scene refresh cannot compete with an independent reader.
                        var state = await Client.CurrentStateAsync(deadline.Token);
                        last = $"slot {state.Preset.Slot}, scene {state.Scene.Index + 1}";
                        if (state.Preset.Slot == slot && (scene is null || state.Scene.Index + 1 == scene))
                        {
                            Outcomes.Add(new
                            {
                                action,
                                expectedSlot = slot,
                                expectedScene = scene,
                                actualSlot = state.Preset.Slot,
                                actualScene = state.Scene.Index + 1,
                                state.Preset.PresetName,
                                elapsedMs = timer.ElapsedMilliseconds
                            });
                            return;
                        }
                    }
                    catch (TimeoutException ex) { last = ex.Message; }
                    catch (InvalidOperationException ex) { last = ex.Message; }
                    await Task.Delay(100, deadline.Token);
                }
            }
            catch (OperationCanceledException ex) when (!Token.IsCancellationRequested)
            { throw new TimeoutException($"{action}: FM9 did not reach slot {slot}, scene {scene?.ToString() ?? "any"}. Last observation: {last}", ex); }
        }
    }

    private async Task Run(string scenario, int[] touchedSlots, Func<Acceptance, Task> exercise)
    {
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCENE_CC"), out int cc) && cc is >= 0 and <= 127,
            "An explicit FM9 Scene Select CC assignment is required.");
        int channel = int.Parse(Environment.GetEnvironmentVariable("PRESET_MAESTRO_MIDI_CHANNEL") ?? "1");
        Assert.InRange(channel, 1, 16);
        using var session = new HardwareSession(output, allowNavigation: true, sceneCc: cc);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var token = deadline.Token;
        var identity = await session.IdentifyAsync(token);
        var definition = FractalDeviceDefinition.For(FractalDeviceVariant.FM9);
        var firmware = Version.Parse(identity.Firmware!);
        using var preflight = new PresetNameClient(session);
        var active = await preflight.ActivePresetAsync(token);
        Assert.False(active.Edited, "Save or discard FM9 edits before navigation acceptance; no navigation was attempted.");
        var original = await preflight.CurrentStateAsync(token);
        Assert.Equal(active.Slot, original.Preset.Slot);
        var snapshots = new Dictionary<int, string>();
        var reader = new PresetIndexReader(preflight, AmpModelCatalogRegistry.CreateStarter());
        foreach (int slot in touchedSlots.Append(original.Preset.Slot).Distinct())
        { snapshots[slot] = JsonSerializer.Serialize(await reader.ReadAsync(definition, firmware, slot, token)); }
        // Preflight checks are fail-closed; never navigate if the saved/edit state changed while preparing.
        var ready = await preflight.ActivePresetAsync(token);
        Assert.False(ready.Edited); Assert.Equal(original.Preset.Slot, ready.Slot);
        preflight.Dispose();

        var settings = new AppSettings { MidiChannel = channel, SceneCc = cc, DeviceModel = DeviceModel.FM9 };
        var window = new MainWindow(settings, [], session, saveSettings: _ => { }, saveFavorites: _ => { });
        window.ConnectionState.Device = identity;
        var acceptance = new Acceptance(window, session, settings, token);
        var failures = new List<string>();
        try
        {
            window.Show(); Click(FindButton(window, "NavPresetSender"));
            Field<DispatcherTimer>(window, "_scenePoll").Start();
            await exercise(acceptance);
        }
        catch (Exception ex) { failures.Add(ex.ToString()); }
        finally
        {
            try
            {
                // Restoration has its own deadline even if the test timed out.
                using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                Assert.True(session.SendBankAndPC(original.Preset.Slot / 128, original.Preset.Slot % 128, channel));
                var restoration = new Acceptance(window, session, settings, restore.Token);
                await restoration.Verify("restore preset", original.Preset.Slot);
                Assert.True(session.SendSysEx(SysexProtocol.Frame(0x0c, [(byte)original.Scene.Index])));
                await restoration.Verify("restore scene", original.Preset.Slot, original.Scene.Index + 1);
                acceptance.Outcomes.AddRange(restoration.Outcomes);
            }
            catch (Exception ex) { failures.Add("Restoring the starting selection failed: " + ex); }
            try
            {
                using var verify = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var finalReader = new PresetIndexReader(acceptance.Client, AmpModelCatalogRegistry.CreateStarter());
                foreach (var (slot, before) in snapshots)
                { Assert.Equal(before, JsonSerializer.Serialize(await finalReader.ReadAsync(definition, firmware, slot, verify.Token))); }
            }
            catch (Exception ex) { failures.Add("Saved preset preservation check failed: " + ex); }
            File.WriteAllText(Path.Combine(session.DirectoryPath, "application-acceptance.json"), JsonSerializer.Serialize(new
            {
                scenario,
                identity,
                channel,
                sceneCc = cc,
                touchedSlots,
                originalSlot = original.Preset.Slot,
                originalScene = original.Scene.Index + 1,
                acceptance.Outcomes,
                failures
            }, IndexJson.Options));
            window.Close();
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fm9ApplicationHardwareFact]
    public Task TypedPresetsReachEveryBankBoundaryWithBothDisplayOffsets() => Run(nameof(TypedPresetsReachEveryBankBoundaryWithBothDisplayOffsets), BoundarySlots, async test =>
    {
        foreach (int offset in new[] { 0, 1 })
        {
            test.Settings.DisplayOffset = offset;
            Invoke(test.Window, "ApplyProfileSettingsToUI");
            foreach (int slot in BoundarySlots)
            {
                TypeAndSend(test.Window, slot + offset);
                await test.Verify($"type {slot + offset}, offset {offset}", slot);
            }
        }
    });

    [Fm9ApplicationHardwareFact]
    public Task FavoriteCommandsReachAllEightScenesAcrossABankBoundary() => Run(nameof(FavoriteCommandsReachAllEightScenesAcrossABankBoundary), [127, 128], async test =>
    {
        var favorites = Field<List<Favorite>>(test.Window, "_favorites");
        Click(FindButton(test.Window, "NavFavorites"));
        foreach (int offset in new[] { 0, 1 })
        {
            test.Settings.DisplayOffset = offset; Invoke(test.Window, "ApplyProfileSettingsToUI");
            for (int scene = 1; scene <= 8; scene++)
            {
                foreach (int slot in new[] { 127, 128 })
                {
                    favorites.Clear(); favorites.Add(new Favorite { Id = 1, Slot = 1, Name = "Hardware acceptance", Preset = slot + offset, Scene = scene });
                    TypeAndSend(test.Window, 1);
                    await test.Verify($"favorite slot 1 -> {slot}/{scene}, offset {offset}", slot, scene);
                }
            }
        }
    });

    [Fm9ApplicationHardwareFact]
    public Task GoToSceneUsesConfiguredCcAndDoesNotReloadTheCurrentPreset() => Run(nameof(GoToSceneUsesConfiguredCcAndDoesNotReloadTheCurrentPreset), [127, 128], async test =>
    {
        foreach (int slot in new[] { 127, 128 })
        {
            Assert.True((bool)Invoke(test.Window, "GoToDevice", slot, null, DeviceModel.FM9)!);
            await test.Verify("Go to preset", slot);
            for (int scene = 1; scene <= 8; scene++)
            {
                // Wait for the real background refresh before exercising the same-preset path.
                while (Field<CancellationTokenSource?>(test.Window, "_sceneCts") is not null || Field<bool>(test.Window, "_pollBusy"))
                { await Task.Delay(20, test.Token); }
                int presetCommands = test.Session.PresetCommands;
                Assert.True((bool)Invoke(test.Window, "GoToDevice", slot, scene, DeviceModel.FM9)!);
                await test.Verify($"Go to scene {scene} on current preset", slot, scene);
                Assert.Equal(presetCommands, test.Session.PresetCommands);
            }
        }
    });

    [Fm9ApplicationHardwareFact]
    public Task NextPreviousAndLastReachExpectedHardwareSelections() => Run(nameof(NextPreviousAndLastReachExpectedHardwareSelections), [0, 127, 128, 511], async test =>
    {
        foreach (int offset in new[] { 0, 1 })
        {
            test.Settings.DisplayOffset = offset; Invoke(test.Window, "ApplyProfileSettingsToUI");
            TypeAndSend(test.Window, 511 + offset); await test.Verify("start at last preset", 511);
            foreach (var (action, slot) in new[] { ("HandleNext", 0), ("HandlePrev", 511), ("HandleLast", 0) })
            { Invoke(test.Window, action); await test.Verify(action, slot); }
            TypeAndSend(test.Window, 127 + offset); await test.Verify("start before bank boundary", 127);
            foreach (var (action, slot) in new[] { ("HandleNext", 128), ("HandlePrev", 127), ("HandleLast", 128) })
            { Invoke(test.Window, action); await test.Verify(action, slot); }
        }
    });

    [Fm9ApplicationHardwareFact]
    public Task RapidSelectionsFinishOnTheLastRequestedPreset() => Run(nameof(RapidSelectionsFinishOnTheLastRequestedPreset), [0, 127, 128, 255, 256, 511], async test =>
    {
        TypeAndSend(test.Window, 511);
        await test.Verify("establish a different starting preset", 511);
        int commands = test.Session.PresetCommands;
        foreach (int slot in new[] { 127, 128, 255, 256, 511, 0 }) { TypeAndSend(test.Window, slot); }
        await test.Verify("rapid preset sequence ends at 0", 0);
        Assert.Equal(commands + 6, test.Session.PresetCommands);
    });
}
