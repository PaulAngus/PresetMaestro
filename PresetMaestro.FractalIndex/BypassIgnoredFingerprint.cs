using System.Buffers.Binary;
using System.Security.Cryptography;
using PresetNameSync.Core;

namespace PresetMaestro.FractalIndex;

/// <summary>Versioned saved-content comparison. Integrity checks still use the original image.</summary>
internal static class BypassIgnoredFingerprint
{
    public const string Prefix = "gen3-no-bypass-v1:";

    public static string? Compute(StoredPresetImage image, int chainStart, bool hasPlacedBlocks)
    {
        if (image.RawImage.Length < 0x48 || chainStart < 0 && hasPlacedBlocks) { return null; }
        byte[] body = image.Body.ToArray();
        for (int pos = chainStart; pos >= 0 && pos + 48 <= body.Length;)
        {
            int columns = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(pos + 30, 2));
            int rows = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(pos + 32, 2));
            if (columns == 0 || rows == 0 || columns > 500 || rows > 8) { break; }
            // Opaque 257x1 trailing section observed on FM9: retain its bytes
            // without applying a placed-effect bypass layout.
            if (columns == 257 && rows == 1) { break; }
            int size = (23 + columns * rows) * 2;
            if (pos + size > body.Length) { return null; }
            // The two 25x1 records are modifier slots, not placed effect blocks.
            if (columns != 25 || rows != 1)
            {
                for (int scene = 0; scene < 8; scene++)
                {
                    int offset = pos + (7 + scene) * 2;
                    if (BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset, 2)) > 1) { return null; }
                    body[offset] = body[offset + 1] = 0;
                }
            }
            pos += size;
        }
        // Include the original preset header and full decompressed body. CRC,
        // compressed size/encoding and trailing storage padding are representation,
        // not settings; CRC necessarily changes when a bypass state changes.
        byte[] content = new byte[0x48 + body.Length];
        image.RawImage.AsSpan(0, 0x48).CopyTo(content);
        content[4] = content[5] = 0;
        body.CopyTo(content, 0x48);
        return Prefix + Convert.ToHexString(SHA256.HashData(content));
    }
}
