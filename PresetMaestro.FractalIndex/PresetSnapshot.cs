namespace PresetMaestro.FractalIndex;

public sealed record AmpSceneState(int Channel, bool Bypassed);

public sealed record AmpBlockSnapshot(int BlockNumber, int[] ModelIds, AmpSceneState[] Scenes);

public sealed record PresetSnapshot(
    int Slot,
    string Name,
    string[] SceneNames,
    AmpBlockSnapshot[] Amps,
    string ContentSha256);
