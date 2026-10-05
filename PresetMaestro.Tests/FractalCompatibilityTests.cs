using System.Diagnostics;
using System.Text.Json;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;
using Xunit.Abstractions;

namespace PresetMaestro.Tests;

public sealed class CompatibilityFactAttribute : FactAttribute
{
    public CompatibilityFactAttribute(bool navigation = false)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRESET_MAESTRO_COMPATIBILITY")))
        { Skip = "Opt-in physical device test; run scripts/Test-Compatibility.ps1."; }
        else if (navigation && Environment.GetEnvironmentVariable("PRESET_MAESTRO_ALLOW_NAVIGATION") != "1")
        { Skip = "Preset/scene changes require -AllowNavigation."; }
    }
}

[Collection("Physical MIDI"), Trait("Category", "Compatibility")]
public sealed class FractalCompatibilityTests(ITestOutputHelper output)
{
    private static FractalDeviceDefinition Definition => FractalDeviceDefinition.For(Enum.Parse<FractalDeviceVariant>(Environment.GetEnvironmentVariable("PRESET_MAESTRO_COMPATIBILITY")!));
    private static int[] Slots(FractalDeviceDefinition device)
    {
        string? configured = Environment.GetEnvironmentVariable("PRESET_MAESTRO_TEST_SLOTS");
        var slots = !string.IsNullOrWhiteSpace(configured) ? configured.Split(',').Select(int.Parse).ToArray() :
            Environment.GetEnvironmentVariable("PRESET_MAESTRO_FULL_SCAN") == "1" ? Enumerable.Range(0, device.PresetSlots).ToArray() :
            new[] { 0, 127, 128, 255, 256, 511, device.PresetSlots - 1 };
        if (slots.Length == 0 || slots.Any(slot => slot < 0 || slot >= device.PresetSlots)) { throw new InvalidOperationException("Test slots are outside this device's capacity."); }
        return slots.Distinct().ToArray();
    }
    private static void SaveReport(HardwareSession session, string name, object report) =>
        File.WriteAllText(Path.Combine(session.DirectoryPath, name + ".json"), JsonSerializer.Serialize(report, IndexJson.Options));

    [CompatibilityFact]
    public async Task CapacityStoredDecodingAndRepeatability()
    {
        var definition = Definition; var slots = Slots(definition);
        using var session = new HardwareSession(output, definition);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var token = deadline.Token;
        var identity = await session.IdentifyAsync(token);
        var before = await HardwareProtocol.PresetAsync(session, definition, null, token);
        int scene = await HardwareProtocol.SceneAsync(session, definition.ModelByte, token);
        using var client = new PresetNameClient(session);
        client.SetDeviceModel(identity.Model);
        var reader = new PresetIndexReader(client, AmpModelCatalogRegistry.CreateStarter());
        var results = new List<object>(); var failures = new List<string>(); var timer = Stopwatch.StartNew();
        try
        {
            foreach (int slot in slots)
            {
                try
                {
                    var name = await HardwareProtocol.PresetAsync(session, definition, slot, token);
                    var first = await reader.ReadAsync(definition, Version.Parse(identity.Firmware!), slot, token);
                    var second = await reader.ReadAsync(definition, Version.Parse(identity.Firmware!), slot, token);
                    Assert.Equal(name.Name, first.Name);
                    Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
                    Assert.Equal(8, first.SceneNames.Length);
                    Assert.All(first.Amps, amp => { Assert.Equal(4, amp.Channels.Length); Assert.Equal(8, amp.Scenes.Length); });
                    results.Add(new { slot, first.Name, first.NameOnlyEmpty, first.ContentSha256, first.BypassIgnoredSha256, first.Amps });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { failures.Add($"Slot {slot}: {ex.GetType().Name}: {ex.Message}"); break; }
            }
        }
        finally
        {
            SaveReport(session, "capacity", new
            {
                identity,
                definition,
                requestedSlots = slots,
                milliseconds = timer.ElapsedMilliseconds,
                results,
                failures,
                scope = "Verifies addressed slots against configured capacity; does not infer a physical capacity from a wrapped or missing response.",
                productionLiveNameSupport = definition.ModelByte == 0x12 ? "FM9" : "Diagnostic protocol queries only; production live-name client is FM9-specific."
            });
            Assert.Equal(before, await HardwareProtocol.PresetAsync(session, definition, null, token));
            Assert.Equal(scene, await HardwareProtocol.SceneAsync(session, definition.ModelByte, token));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [CompatibilityFact]
    public async Task DeviceAmpNamesMatchScopedCatalogue()
    {
        var definition = Definition;
        string? configured = Environment.GetEnvironmentVariable("PRESET_MAESTRO_AMP_TYPE_PARAMETER");
        int? parameter = int.TryParse(configured, out int value) ? value : definition.ModelByte == 0x12 ? 10 : null;
        using var session = new HardwareSession(output, definition, ampTypeParameter: parameter);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var token = deadline.Token;
        var identity = await session.IdentifyAsync(token);
        var before = await HardwareProtocol.PresetAsync(session, definition, null, token);
        int scene = await HardwareProtocol.SceneAsync(session, definition.ModelByte, token);
        var rows = new List<AmpCompatibilityRow>(); var failures = new List<string>(); string boundary = "Not reached";
        try
        {
            if (parameter is null)
            { failures.Add("No verified amp-roster query parameter for this model. Supply -AmpTypeParameter from a captured editor read; this is a compatibility gap, not a pass."); }
            else
            {
                for (int id = 0; id < 1024; id++)
                {
                    byte[] reply = await HardwareProtocol.ExchangeAsync(session, HardwareProtocol.AmpNameQuery(definition.ModelByte, parameter.Value, id), f => HardwareProtocol.IsAmpReply(f, definition.ModelByte, parameter.Value), token);
                    string? name = HardwareProtocol.AmpName(reply, definition.ModelByte, parameter.Value);
                    if (name is null)
                    {
                        boundary = $"Unrecognized descriptor at ordinal {id}: {Convert.ToHexString(reply)}";
                        // Only the captured firmware/table boundary is currently established.
                        if (definition.Variant != FractalDeviceVariant.FM9 || Version.Parse(identity.Firmware!) != new Version(12, 0) || id != 336)
                        { failures.Add("Amp roster boundary is unverified for this model/firmware. Inspect the retained response; do not treat invalid text as a confirmed end of table."); }
                        break;
                    }
                    rows.Add(AmpCompatibilityRow.Compare(definition, Version.Parse(identity.Firmware!), id, name));
                    if (id == 1023) { failures.Add("Reached the bounded 1024-entry probe limit; roster completeness is unverified."); }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { failures.Add($"{ex.GetType().Name}: {ex.Message}. Stopped rather than retrying uncorrelated amp-name replies."); }
        finally
        {
            SaveReport(session, "amp-compatibility", new { identity, definition.Variant, parameter, boundary, rows, failures, unknownOrDifferent = rows.Where(r => r.Status != "Match") });
            Assert.Equal(before, await HardwareProtocol.PresetAsync(session, definition, null, token));
            Assert.Equal(scene, await HardwareProtocol.SceneAsync(session, definition.ModelByte, token));
        }
        Assert.True(failures.Count == 0 && rows.Count > 0 && rows.All(r => r.Status == "Match"),
            $"{rows.Count(r => r.Status != "Match")} unknown/different amp models. {string.Join(" ", failures)} See amp-compatibility.json.");
    }

    [CompatibilityFact(navigation: true), Trait("Capability", "ChangesPresetsAndScenes")]
    public async Task NavigatePresetsAndAllScenesAndVerifyStoredDataIsUnchanged()
    {
        var definition = Definition; var slots = Slots(definition);
        int channel = int.Parse(Environment.GetEnvironmentVariable("PRESET_MAESTRO_MIDI_CHANNEL") ?? "1");
        using var session = new HardwareSession(output, definition, allowNavigation: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30)); var token = deadline.Token;
        var identity = await session.IdentifyAsync(token);
        var original = await HardwareProtocol.PresetAsync(session, definition, null, token);
        int originalScene = await HardwareProtocol.SceneAsync(session, definition.ModelByte, token);
        using var client = new PresetNameClient(session);
        client.SetDeviceModel(identity.Model);
        var reader = new PresetIndexReader(client, AmpModelCatalogRegistry.CreateStarter());
        var visited = new List<object>(); var failures = new List<string>();
        try
        {
            foreach (int slot in slots)
            {
                var saved = await reader.ReadAsync(definition, Version.Parse(identity.Firmware!), slot, token);
                Assert.True(session.SendBankAndPC(slot / 128, slot % 128, channel));
                await HardwareProtocol.WaitForStateAsync(session, definition, slot, null, token);
                // Empty slots can load a device-generated default; they still must address correctly.
                if (!saved.NameOnlyEmpty) { Assert.Equal(saved.Name, (await HardwareProtocol.PresetAsync(session, definition, null, token)).Name); }
                for (int scene = 0; scene < 8; scene++)
                {
                    Assert.True(session.SendSysEx(SysexProtocol.Frame(definition.ModelByte, 0x0c, [(byte)scene])));
                    await HardwareProtocol.WaitForStateAsync(session, definition, slot, scene, token);
                    string name = await HardwareProtocol.SceneNameAfterNavigationAsync(session, definition, scene, token);
                    if (!saved.NameOnlyEmpty) { Assert.Equal(saved.SceneNames[scene], name); }
                    visited.Add(new { slot, scene, name });
                }
                var after = await reader.ReadAsync(definition, Version.Parse(identity.Firmware!), slot, token);
                Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(after));
            }
        }
        catch (Exception ex) { failures.Add(ex.ToString()); throw; }
        finally
        {
            try
            {
                using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Assert.True(session.SendBankAndPC(original.Slot / 128, original.Slot % 128, channel));
                await HardwareProtocol.WaitForStateAsync(session, definition, original.Slot, null, restore.Token);
                Assert.True(session.SendSysEx(SysexProtocol.Frame(definition.ModelByte, 0x0c, [(byte)originalScene])));
                await HardwareProtocol.WaitForStateAsync(session, definition, original.Slot, originalScene, restore.Token);
            }
            catch (Exception ex) { failures.Add("Returning to original selection failed: " + ex.Message); }
            SaveReport(session, "navigation", new { identity, definition, slots, channel, originalSlot = original.Slot, originalScene, visited, failures });
        }
        Assert.Empty(failures);
    }
}

internal sealed record AmpCompatibilityRow(int Id, string DeviceName, string CodeName, string Status)
{
    private static readonly AmpModelCatalogRegistry Catalogues = AmpModelCatalogRegistry.CreateStarter();
    public static AmpCompatibilityRow Compare(FractalDeviceDefinition definition, Version firmware, int id, string name)
    {
        var known = Catalogues.Resolve(definition, firmware, id);
        return new(id, name, known.DisplayName, !known.IsKnown ? "Unknown ID or firmware scope" : known.DisplayName != name ? "Name differs" : "Match");
    }
}
