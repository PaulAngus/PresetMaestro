using System.Buffers.Binary;
using System.Security.Cryptography;
using PresetNameSync.Core;

namespace PresetMaestro.FractalIndex;

// Conservative permutation evidence: unknown bytes and all channel parameters stay
// in the common context. Any change there prevents automatic tag reassignment.
internal static class SceneContentFingerprint
{
    internal const string Prefix = "gen3-scene-v1:";

    internal static string[]? Compute(StoredPresetImage image, int chainStart)
    {
        if (chainStart < 2 || image.RawImage.Length < 0x48 || image.Body.Length < 0x104) { return null; }
        byte[] context = image.Body.ToArray();
        context.AsSpan(4, 8 * 32).Clear(); // Scene names describe identity, not sound.
        var states = Enumerable.Range(0, 8).Select(_ => new List<byte>()).ToArray();
        int effects = 0;
        for (int pos = chainStart; pos + 48 <= context.Length;)
        {
            int columns = BinaryPrimitives.ReadUInt16LittleEndian(image.Body.AsSpan(pos + 30, 2));
            int rows = BinaryPrimitives.ReadUInt16LittleEndian(image.Body.AsSpan(pos + 32, 2));
            if (columns == 0 && rows == 0 || columns == 257 && rows == 1) { break; }
            if (columns is < 1 or > 500 || rows is < 1 or > 8) { return null; }
            int size = (23 + columns * rows) * 2;
            if (pos + size > context.Length) { return null; }
            if (columns != 25 || rows != 1) // Modifier slots retain their opaque bytes.
            {
                effects++;
                for (int scene = 0; scene < 8; scene++)
                {
                    int channelOffset = scene == 0 ? pos - 2 : pos + (scene - 1) * 2;
                    int bypassOffset = pos + (7 + scene) * 2;
                    int channel = BinaryPrimitives.ReadUInt16LittleEndian(image.Body.AsSpan(channelOffset, 2));
                    int bypass = BinaryPrimitives.ReadUInt16LittleEndian(image.Body.AsSpan(bypassOffset, 2));
                    if (channel >= rows || bypass > 1) { return null; }
                    states[scene].Add((byte)channel); states[scene].Add((byte)bypass);
                    context.AsSpan(channelOffset, 2).Clear(); context.AsSpan(bypassOffset, 2).Clear();
                }
            }
            pos += size;
        }
        if (effects == 0) { return null; }
        byte[] header = image.RawImage.AsSpan(0, 0x48).ToArray();
        header[4] = header[5] = 0; // CRC changes after any scene move.
        return states.Select(state =>
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(header); hash.AppendData(context); hash.AppendData(state.ToArray());
            return Prefix + Convert.ToHexString(hash.GetHashAndReset());
        }).ToArray();
    }
}
