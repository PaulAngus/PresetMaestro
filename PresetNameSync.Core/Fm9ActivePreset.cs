using System.Buffers.Binary;

namespace PresetNameSync.Core;

/// <summary>FM9 editor status observed on hardware. Live names are never a saved snapshot.</summary>
public sealed record Fm9ActivePreset(int Slot, bool Edited, string PresetName, string[] SceneNames)
{
    public static byte[] BuildQuery() => SysexProtocol.Frame(0x01, [0x2e, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    public static bool TryParse(ReadOnlySpan<byte> frame, out Fm9ActivePreset? result)
    {
        result = null;
        if (!SysexProtocol.ValidFrame(frame, 0x12, 0x01, 755) || frame[6] != 0x2e ||
            (frame[19] | frame[20] << 7) != 640) { return false; }
        Span<byte> decoded = stackalloc byte[640];
        int bits = 0, accumulator = 0, written = 0;
        foreach (byte septet in frame[21..^2])
        {
            accumulator = (accumulator << 7) | septet;
            bits += 7;
            if (bits < 8) { continue; }
            bits -= 8;
            if (written == decoded.Length) { return false; }
            decoded[written++] = (byte)(accumulator >> bits);
            accumulator &= (1 << bits) - 1;
        }
        if (written != decoded.Length || accumulator != 0) { return false; }
        uint slot = BinaryPrimitives.ReadUInt32LittleEndian(decoded);
        uint edited = BinaryPrimitives.ReadUInt32LittleEndian(decoded[4..]);
        if (slot >= 512 || edited > 1) { return false; }
        var names = new string[8];
        for (int i = 0; i < 8; i++) { names[i] = SysexProtocol.ReadName(decoded.Slice(48 + i * 32, 32)); }
        result = new((int)slot, edited == 1, SysexProtocol.ReadName(decoded.Slice(16, 32)), names);
        return true;
    }
}
