using System.Threading.Channels;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro.Midi;

public sealed record FractalDeviceInformation(DeviceModel Model, string? DeviceName)
{
    // null means unverified/unavailable, not a successfully decoded blank name.
    public string Label => DeviceName is null ? ModelLabel : $"{ModelLabel} · {(string.IsNullOrWhiteSpace(DeviceName) ? "Unnamed device" : DeviceName)}";
    public string ModelLabel => Model == DeviceModel.AxeFxIII ? "Axe-Fx III" : Model.ToString();
}

/// <summary>Undocumented Gen-3 editor device-information exchange. No parameter writes.</summary>
public sealed class FractalDeviceInformationClient
{
    private readonly IMidiManager _midi;
    private readonly Action<string> _log;
    private int _active;
    private long _generation;

    public FractalDeviceInformationClient(IMidiManager midi, Action<string>? log = null)
    {
        _midi = midi;
        _log = log ?? (_ => { });
    }

    // Captured FM9-Edit discovery: broadcast WHO_AM_I, not the rejected 0x46 request.
    public static byte[] BuildDeviceInformationRequest() => [0xf0, 0, 1, 0x74, 0x7f, 0, 0x7a, 0xf7];

    public static byte[] BuildDeviceNameRequest(byte modelByte)
    {
        // Only FM9's complete read transaction has been captured. Do not substitute
        // other products' parameter IDs until their envelopes have been verified.
        if (modelByte != 0x12) { throw new ArgumentOutOfRangeException(nameof(modelByte)); }
        return [0xf0, 0, 1, 0x74, 0x12, 0x01, 0x1a, 0, 1, 0, 0x44, 0x0a,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0x43, 0xf7];
    }

    public static bool TryValidateFractalFrame(ReadOnlySpan<byte> message)
    {
        if (message.Length < 8 || message[0] != 0xf0 || message[1] != 0 || message[2] != 1 ||
            message[3] != 0x74 || message[^1] != 0xf7 || !TryMapModel(message[4], out _))
        {
            return false;
        }

        for (int i = 1; i < message.Length - 1; i++)
        {
            if (message[i] >= 0x80)
            {
                return false;
            }
        }

        return message[^2] == SysexProtocol.ComputeChecksum(message[..^2]);
    }

    public static bool TryParseModel(ReadOnlySpan<byte> message, out DeviceModel model)
    {
        model = default;
        return TryValidateFractalFrame(message) && TryMapModel(message[4], out model);
    }

    private static bool TryMapModel(byte value, out DeviceModel model)
    {
        model = value switch { 0x10 => DeviceModel.AxeFxIII, 0x11 => DeviceModel.FM3, _ => DeviceModel.FM9 };
        return value is 0x10 or 0x11 or 0x12;
    }

    public static bool TryParseDeviceNameResponse(ReadOnlySpan<byte> message, out string? name)
    {
        name = null;
        if (!TryValidateFractalFrame(message) || message.Length != 60 || message[4] != 0x12) { return false; }
        ReadOnlySpan<byte> envelope = [0x01, 0x1a, 0, 1, 0, 0x44, 0x0a, 0, 0, 0, 0, 0, 0, 0, 0x20, 0];
        if (!message.Slice(5, 16).SequenceEqual(envelope)) { return false; }

        // The captured 37 septets represent 32 bytes, MSB first, plus three zero bits.
        // This is a continuous bitstream, not a per-seven-byte MSB mask.
        if ((message[57] & 7) != 0) { return false; }
        Span<byte> decoded = stackalloc byte[32];
        int accumulator = 0, bits = 0, written = 0;
        foreach (byte septet in message.Slice(21, 37))
        {
            accumulator = (accumulator << 7) | septet;
            bits += 7;
            if (bits >= 8)
            {
                bits -= 8;
                decoded[written++] = (byte)(accumulator >> bits);
                accumulator &= (1 << bits) - 1;
            }
        }
        int end = decoded.IndexOf((byte)0);
        if (end < 0 || end > 8) { return false; }
        for (int i = 0; i < end; i++)
        {
            if (decoded[i] is < 0x20 or > 0x7e) { return false; }
        }
        for (int i = end; i < decoded.Length; i++)
        {
            if (decoded[i] != 0) { return false; }
        }
        name = System.Text.Encoding.ASCII.GetString(decoded[..end]);
        return true;
    }

    public async Task<FractalDeviceInformation> QueryDeviceInformationAsync(
        string input, string output, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            throw new InvalidOperationException("A device-information query is already active.");
        }
        long generation = Interlocked.Increment(ref _generation);
        try
        {
            if (!_midi.InputOpen || !_midi.OutputOpen)
            {
                throw new InvalidOperationException("Both selected MIDI ports must be open.");
            }
            _log($"FRACTAL [{generation}]: querying selected IN '{input}' / OUT '{output}'.");
            byte[]? identity = await ExchangeAsync(BuildDeviceInformationRequest(), frame =>
                TryValidateFractalFrame(frame) && frame.Length == 10 && frame[5] == 0x64 && frame[6] == 0 && frame[7] == 0,
                redact: false).ConfigureAwait(false);
            if (identity is null)
            {
                throw new TimeoutException("No valid WHO_AM_I response on selected MIDI IN (timeout, invalid frame, unsupported model or nonzero status).");
            }
            if (!TryParseModel(identity, out var model))
            {
                throw new InvalidDataException("Invalid Fractal identity response.");
            }
            var identified = new FractalDeviceInformation(model, null);
            if (identity[4] != 0x12)
            {
                _log($"FRACTAL: {identified.ModelLabel} identified; name read awaits a captured transaction for this model.");
                return identified;
            }
            byte[]? response = await ExchangeAsync(BuildDeviceNameRequest(identity[4]), frame =>
                TryParseDeviceNameResponse(frame, out _), redact: true).ConfigureAwait(false);
            if (response is not null && TryParseDeviceNameResponse(response, out var name)) { return new(model, name); }
            _log("FRACTAL: identity validated; name read timed out or returned an unrecognized payload.");
            return identified;
        }
        finally
        {
            Interlocked.Increment(ref _generation);
            Interlocked.Exchange(ref _active, 0);
        }

        async Task<byte[]?> ExchangeAsync(byte[] request, Func<byte[], bool> accept, bool redact)
        {
            var replies = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });
            var assembler = new SysexAssembler();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            void Receive(object? sender, byte[] bytes)
            {
                lock (assembler)
                {
                    if (generation != Interlocked.Read(ref _generation) || deadline.IsCancellationRequested) { return; }
                    foreach (var frame in assembler.Feed(bytes)) { replies.Writer.TryWrite(frame); }
                }
            }
            _midi.SysexMessageReceived += Receive;
            try
            {
                _log($"FRACTAL [{generation}] TX: {BitConverter.ToString(request).Replace('-', ' ')}");
                if (!_midi.SendSysEx(request)) { throw new IOException("MIDI output failed during device-information query."); }
                while (true)
                {
                    var frame = await replies.Reader.ReadAsync(deadline.Token).ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    // The capture proved the name encoding. Do not retain raw name or unrelated parameter data.
                    if (redact || (frame.Length > 5 && frame[5] is 0x01 or 0x07))
                    {
                        _log($"FRACTAL [{generation}] RX: {frame.Length} bytes (payload redacted).");
                    }
                    else { _log($"FRACTAL [{generation}] RX: {BitConverter.ToString(frame).Replace('-', ' ')}"); }
                    if (accept(frame)) { return frame; }
                    _log("FRACTAL: ignored invalid, unrelated, unsupported or unsuccessful response.");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            finally
            {
                deadline.Cancel();
                _midi.SysexMessageReceived -= Receive;
            }
        }
    }
}
