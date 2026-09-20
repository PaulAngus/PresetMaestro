namespace PresetNameSync.Core;

public sealed record SceneNameResult(int Index, string Name);
public sealed record PresetScenes(int Slot, string PresetName, string[] Names);
