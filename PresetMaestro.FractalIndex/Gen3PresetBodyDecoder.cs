using System.Buffers.Binary;
using System.Security.Cryptography;
using PresetNameSync.Core;

namespace PresetMaestro.FractalIndex;

/// <summary>Decodes saved programmed Amp state. It does not claim effective live state under Scene Ignore.</summary>
public static class Gen3PresetBodyDecoder
{
    public static PresetSnapshot Decode(StoredPresetImage image, FractalDeviceDefinition device)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(device);
        byte[] body = image.Body;
        const int gridStart = 0x104;
        int gridBytes = device.GridRows * device.GridColumns * 4;
        if (body.Length < gridStart + gridBytes)
        {
            throw new InvalidDataException("Saved preset body is too short for the routing grid.");
        }

        ushort Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset, 2));
        // The grid supplies presence/instance IDs only. Coordinates and routing
        // do not form part of the indexed Amp state or determine its identity.
        var presentAmps = new SortedSet<int>();
        bool hasPlacedBlocks = false;
        for (int column = 0; column < device.GridColumns; column++)
        {
            for (int row = 0; row < device.GridRows; row++)
            {
                int effect = Word(gridStart + (column * device.GridRows + row) * 4);
                hasPlacedBlocks |= effect is > 0 and <= 1000;
                if (effect is 58 or 59)
                {
                    if (effect - 58 >= device.MaxAmpBlocks)
                    {
                        throw new InvalidDataException($"Amp {effect - 57} is not supported by {device.Variant}.");
                    }
                    if (!presentAmps.Add(effect - 57))
                    {
                        throw new InvalidDataException("The same Amp instance appears more than once in the routing grid.");
                    }
                }
            }
        }
        if (presentAmps.Count > device.MaxAmpBlocks)
        {
            throw new InvalidDataException($"More than {device.MaxAmpBlocks} Amp grid instances were found for {device.Variant}.");
        }

        const int modifierBytes = (23 + 25) * 2;
        int chainStart = -1;
        for (int offset = 0x200; offset + modifierBytes + 34 <= body.Length; offset += 2)
        {
            if (Word(offset + 30) == 25 && Word(offset + 32) == 1 &&
                Word(offset + modifierBytes + 30) == 25 && Word(offset + modifierBytes + 32) == 1)
            {
                chainStart = offset;
                break;
            }
        }
        if (chainStart < 0 && presentAmps.Count != 0)
        {
            throw new InvalidDataException("Amp grid instances have no block chain.");
        }

        var amps = new List<AmpBlockSnapshot>();
        int[] blockNumbers = [.. presentAmps];
        for (int pos = chainStart; pos >= 0 && pos + 48 <= body.Length;)
        {
            int columns = Word(pos + 30), rows = Word(pos + 32);
            if (columns == 0 || rows == 0)
            {
                break;
            }
            if (columns > 500 || rows > 8)
            {
                break;
            }
            int size = checked((23 + columns * rows) * 2);
            if (pos + size > body.Length)
            {
                break;
            }
            if (columns == device.AmpColumns && rows == 4)
            {
                if (amps.Count >= blockNumbers.Length)
                {
                    throw new InvalidDataException("Amp grid and block-record counts differ.");
                }
                int paramsStart = pos + 46;
                var models = new int[4];
                for (int channel = 0; channel < 4; channel++)
                {
                    models[channel] = device.AmpTypeInHeader
                        ? channel == 0 ? Word(pos + 34) : Word(paramsStart + (channel - 1) * columns * 2 + (columns - 6) * 2)
                        : Word(paramsStart + (channel * columns + device.AmpTypeParameterWordOffset) * 2);
                }
                var scenes = new AmpSceneState[8];
                for (int scene = 0; scene < 8; scene++)
                {
                    int selected = Word(scene == 0 ? pos - 2 : pos + (scene - 1) * 2);
                    int bypass = Word(pos + (7 + scene) * 2);
                    if (selected > 3 || bypass > 1)
                    {
                        throw new InvalidDataException("Saved Amp scene channel or bypass value is invalid.");
                    }
                    scenes[scene] = new AmpSceneState(selected, bypass != 0);
                }
                amps.Add(new AmpBlockSnapshot(blockNumbers[amps.Count], models, scenes));
            }
            pos += size;
        }
        if (amps.Count != presentAmps.Count)
        {
            throw new InvalidDataException("Amp grid and block-record counts differ.");
        }
        return new PresetSnapshot(image.Scenes.Slot, image.Scenes.PresetName, image.Scenes.Names,
            [.. amps], Convert.ToHexString(SHA256.HashData(image.RawImage)))
        { BypassIgnoredSha256 = BypassIgnoredFingerprint.Compute(image, chainStart, hasPlacedBlocks) };
    }
}
