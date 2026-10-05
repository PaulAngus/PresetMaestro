using System.Text;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

internal sealed class HardwareRequestPolicy
{
    private readonly FractalDeviceDefinition _device;
    private readonly bool _navigation;
    public HashSet<string> AllowedReads { get; }
    public HardwareRequestPolicy(FractalDeviceDefinition device, bool navigation = false, int? ampTypeParameter = null)
    {
        _device = device; _navigation = navigation;
        byte model = device.ModelByte;
        var frames = new List<byte[]> { FractalDeviceInformationClient.BuildDeviceInformationRequest(), FractalDeviceInformationClient.BuildFirmwareRequest(model), HardwareProtocol.PresetQuery(model), SysexProtocol.Frame(model, 0x0c, [0x7f]) };
        if (model == 0x12) { frames.Add(FractalDeviceInformationClient.BuildDeviceNameRequest(model)); }
        frames.AddRange(Enumerable.Range(0, 8).Select(i => SysexProtocol.Frame(model, 0x0e, [(byte)i])));
        frames.AddRange(Enumerable.Range(0, device.PresetSlots).Select(i => HardwareProtocol.PresetQuery(model, i)));
        frames.AddRange(Enumerable.Range(0, device.PresetSlots).Select(i => StoredPresetDecoder.BuildQuery(i, model, device.PresetSlots - 1)));
        if (ampTypeParameter is int parameter)
        { frames.AddRange(Enumerable.Range(0, 1024).Select(i => HardwareProtocol.AmpNameQuery(model, parameter, i))); }
        AllowedReads = frames.Select(Convert.ToHexString).ToHashSet(StringComparer.Ordinal);
    }
    public void ValidateSysEx(byte[] frame)
    {
        if (AllowedReads.Contains(Convert.ToHexString(frame))) { return; }
        if (_navigation && SysexProtocol.ValidFrame(frame, _device.ModelByte, 0x0c, 9) && frame[6] < 8) { return; }
        throw new InvalidOperationException("Hardware test blocked a request outside its allowlist.");
    }
    public void ValidateProgramChange(int bank, int pc, int channel)
    {
        if (!_navigation) { throw new InvalidOperationException("Preset navigation requires -AllowNavigation."); }
        if (bank < 0 || bank >= _device.PresetSlots / 128 || pc is < 0 or > 127 || channel is < 1 or > 16)
        { throw new ArgumentOutOfRangeException(nameof(bank), "Program address or MIDI channel is outside the selected device's range."); }
    }
}

internal static class HardwareProtocol
{
    public static byte[] PresetQuery(byte model, int? slot = null) => SysexProtocol.Frame(model, 0x0d,
        slot is int n ? [(byte)(n & 127), (byte)(n >> 7)] : [0x7f, 0x7f]);
    public static byte[] AmpNameQuery(byte model, int parameter, int index)
    {
        if (parameter is < 0 or > 16383 || index is < 0 or > 1023) { throw new ArgumentOutOfRangeException(nameof(parameter)); }
        return SysexProtocol.Frame(model, 1, [0x1f, 0, 58, 0, (byte)(parameter & 127), (byte)(parameter >> 7), (byte)(index & 127), (byte)(index >> 7), 0, 0, 0, 0, 0, 0, 0]);
    }
    public static bool IsAmpReply(byte[] frame, byte model, int parameter) =>
        frame.Length >= 23 && SysexProtocol.ValidFrame(frame, model, 1, frame.Length) &&
        frame[6] == 0x1f && frame[7] == 0 && frame[8] == 58 && frame[9] == 0 &&
        (frame[10] | frame[11] << 7) == parameter;

    // Captured editor protocol. Invalid text is an unrecognized descriptor, not
    // proof of the end of the model list. Replies do not echo the requested ordinal.
    public static string? AmpName(byte[] frame, byte model, int parameter)
    {
        if (!IsAmpReply(frame, model, parameter)) { return null; }
        int length = frame[19] | frame[20] << 7;
        if (length is < 2 or > 64 || frame.Length != 23 + (length * 8 + 6) / 7) { return null; }
        var decoded = new List<byte>(); int bits = 0, accumulator = 0;
        foreach (byte value in frame.AsSpan(21, frame.Length - 23))
        {
            accumulator = (accumulator << 7) | value; bits += 7;
            if (bits < 8) { continue; }
            bits -= 8; decoded.Add((byte)(accumulator >> bits)); accumulator &= (1 << bits) - 1;
        }
        if (accumulator != 0 || decoded.Count != length) { return null; }
        int nul = decoded.IndexOf(0);
        if (nul < 1 || decoded.Take(nul).Any(b => b is < 32 or > 126) || decoded.Skip(nul).Any(b => b != 0)) { return null; }
        return Encoding.ASCII.GetString(decoded.Take(nul).ToArray());
    }
    public static async Task<byte[]> ExchangeAsync(IMidiManager midi, byte[] request, Func<byte[], bool> matches, CancellationToken token, TimeSpan? replyTimeout = null)
    {
        var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var assembler = new SysexAssembler(); var gate = new object();
        void Receive(object? sender, byte[] bytes)
        {
            lock (gate) { foreach (var frame in assembler.Feed(bytes)) { if (matches(frame)) { reply.TrySetResult(frame); } } }
        }
        midi.SysexMessageReceived += Receive;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!midi.SendSysEx(request)) { throw new IOException("MIDI read request failed to send."); }
            return await reply.Task.WaitAsync(replyTimeout ?? TimeSpan.FromSeconds(3), token);
        }
        finally { midi.SysexMessageReceived -= Receive; }
    }
    public static async Task<(int Slot, string Name)> PresetAsync(IMidiManager midi, FractalDeviceDefinition device, int? slot, CancellationToken token, TimeSpan? replyTimeout = null)
    {
        var frame = await ExchangeAsync(midi, PresetQuery(device.ModelByte, slot), f =>
            SysexProtocol.ValidFrame(f, device.ModelByte, 0x0d, 42) &&
            (f[6] | f[7] << 7) < device.PresetSlots && (slot is null || (f[6] | f[7] << 7) == slot), token, replyTimeout);
        return (frame[6] | frame[7] << 7, SysexProtocol.ReadName(frame.AsSpan(8, 32)));
    }
    public static async Task<int> SceneAsync(IMidiManager midi, byte model, CancellationToken token, TimeSpan? replyTimeout = null)
    {
        var frame = await ExchangeAsync(midi, SysexProtocol.Frame(model, 0x0c, [0x7f]), f => SysexProtocol.ValidFrame(f, model, 0x0c, 9) && f[6] < 8, token, replyTimeout);
        return frame[6];
    }
    public static async Task<string> SceneNameAfterNavigationAsync(IMidiManager midi, FractalDeviceDefinition device, int scene, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while (true)
            {
                try
                {
                    var frame = await ExchangeAsync(midi, SysexProtocol.Frame(device.ModelByte, 0x0e, [(byte)scene]),
                        f => SysexProtocol.ValidFrame(f, device.ModelByte, 0x0e, 41) && f[6] == scene,
                        deadline.Token, TimeSpan.FromMilliseconds(500));
                    return SysexProtocol.ReadName(frame.AsSpan(7, 32));
                }
                catch (TimeoutException) { await Task.Delay(100, deadline.Token); }
            }
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new TimeoutException($"No name reply for scene index {scene} after navigation within 8 seconds.", ex); }
    }

    public static async Task WaitForStateAsync(IMidiManager midi, FractalDeviceDefinition device, int slot, int? scene, CancellationToken token,
        TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
        int? observedSlot = null, observedScene = null;
        int missedReplies = 0;
        try
        {
            while (true)
            {
                try
                {
                    // Loading a preset/scene can temporarily suppress queries. Only
                    // idempotent state reads are retried; never resend the change command.
                    var preset = await PresetAsync(midi, device, null, deadline.Token, TimeSpan.FromMilliseconds(500));
                    observedSlot = preset.Slot; observedScene = null;
                    observedScene = await SceneAsync(midi, device.ModelByte, deadline.Token, TimeSpan.FromMilliseconds(500));
                    if (observedSlot == slot && (scene is null || scene == observedScene)) { return; }
                }
                catch (TimeoutException) { missedReplies++; }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"Navigation did not reach preset slot {slot}, scene {scene?.ToString() ?? "any"} (zero-based). " +
                $"Last observed slot: {observedSlot?.ToString() ?? "unavailable"}; scene: {observedScene?.ToString() ?? "unavailable"}; " +
                $"missed read replies: {missedReplies}. Check MIDI channel/program mapping and whether another controller is active.", ex);
        }
    }
}
