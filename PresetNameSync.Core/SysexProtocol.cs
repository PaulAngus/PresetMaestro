using System.Globalization;
using System.Text;

namespace PresetNameSync.Core;

public static class SysexProtocol
{
    public static byte[] Frame(byte opcode, ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[payload.Length + 8];
        byte[] header = [0xf0, 0, 1, 0x74, 0x12, opcode];
        header.CopyTo(frame, 0); payload.CopyTo(frame.AsSpan(6));
        frame[^2] = ComputeChecksum(frame.AsSpan(0, frame.Length - 2)); frame[^1] = 0xf7;
        return frame;
    }

    public static bool ValidFrame(ReadOnlySpan<byte> frame, byte opcode, int length)
    {
        if (frame.Length != length || length < 8 || frame[0] != 0xf0 || frame[^1] != 0xf7 ||
            frame[1] != 0 || frame[2] != 1 || frame[3] != 0x74 || frame[4] != 0x12 || frame[5] != opcode)
        {
            return false;
        }

        for (int i = 1; i < frame.Length - 1; i++)
        {
            if (frame[i] > 0x7f)
            {
                return false;
            }
        }

        return frame[^2] == ComputeChecksum(frame[..^2]);
    }

    public static byte[] BuildSceneNameQuery(int index)
    {
        if (index is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Frame(0x0e, [(byte)index]);
    }

    public static byte[] BuildCurrentPresetQuery() => Frame(0x0d, [0x7f, 0x7f]);
    public static byte[] BuildCurrentSceneQuery() => Frame(0x0c, [0x7f]);

    public static bool TryParseSceneNameResponse(ReadOnlySpan<byte> frame, out SceneNameResult? result)
    {
        result = null;
        if (!ValidFrame(frame, 0x0e, 41) || frame[6] > 7)
        {
            return false;
        }

        result = new SceneNameResult(frame[6], ReadName(frame.Slice(7, 32)));
        return true;
    }

    public static string ReadName(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(nul < 0 ? bytes : bytes[..nul]).TrimEnd(' ');
    }
    private const byte StartOfExclusive = 0xF0;
    private const byte EndOfExclusive = 0xF7;
    private const byte QueryPresetNameOpcode = 0x0D;
    private const int PresetNameLength = 32;

    public static byte ComputeChecksum(ReadOnlySpan<byte> bytes)
    {
        byte xor = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            xor ^= bytes[i];
        }

        return (byte)(xor & 0x7F);
    }

    public static byte[] BuildPresetNameQuery(int slot)
    {
        if (slot < 0 || slot > 511)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "Slot must be in the range 0-511.");
        }

        byte[] frameWithoutChecksum =
        [
            StartOfExclusive, 0x00, 0x01, 0x74, 0x12, QueryPresetNameOpcode,
            (byte)(slot & 0x7F), (byte)((slot >> 7) & 0x7F)
        ];
        byte[] frame = new byte[frameWithoutChecksum.Length + 2];
        frameWithoutChecksum.CopyTo(frame, 0);
        frame[^2] = ComputeChecksum(frameWithoutChecksum);
        frame[^1] = EndOfExclusive;
        return frame;
    }

    public static bool TryParsePresetNameResponse(ReadOnlySpan<byte> frame, out PresetNameResult? result, out string error)
    {
        result = null;
        error = string.Empty;
        int expectedLength = 1 + 3 + 1 + 1 + 2 + PresetNameLength + 1 + 1;

        if (frame.Length != expectedLength)
        {
            error = $"Unexpected response length {frame.Length}; expected {expectedLength}.";
            return false;
        }

        if (frame[0] != StartOfExclusive || frame[^1] != EndOfExclusive)
        {
            error = "Invalid SysEx framing.";
            return false;
        }

        if (frame[1] != 0x00 || frame[2] != 0x01 || frame[3] != 0x74)
        {
            error = "Unexpected manufacturer header.";
            return false;
        }

        if (frame[4] != 0x12)
        {
            error = "Unexpected model byte.";
            return false;
        }

        if (frame[5] != QueryPresetNameOpcode)
        {
            error = "Unexpected opcode.";
            return false;
        }

        byte computed = ComputeChecksum(frame[..^2]);
        if (frame[^2] != computed)
        {
            error = $"Checksum mismatch: received 0x{frame[^2]:X2}, computed 0x{computed:X2}.";
            return false;
        }

        if (!ValidFrame(frame, QueryPresetNameOpcode, expectedLength) || (frame[6] | frame[7] << 7) > 511)
        {
            error = "Invalid preset address or non-MIDI data byte.";
            return false;
        }
        byte[] nameBytes = frame.Slice(8, PresetNameLength).ToArray();
        int nulIndex = Array.IndexOf(nameBytes, (byte)0x00);
        int length = nulIndex >= 0 ? nulIndex : nameBytes.Length;
        string name = Encoding.ASCII.GetString(nameBytes, 0, length).TrimEnd(' ');
        result = new PresetNameResult(frame[6] | (frame[7] << 7), name);
        return true;
    }

    public static string ToHex(ReadOnlySpan<byte> frame) =>
        string.Join(" ", frame.ToArray().Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
}
