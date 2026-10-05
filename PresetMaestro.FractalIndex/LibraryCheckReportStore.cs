using System.Text.Json;

namespace PresetMaestro.FractalIndex;

public sealed record LibraryCheckReport(Guid LibraryId, Guid BaselineId, DateTimeOffset BaselineStartedAt,
    DateTimeOffset CheckedAt, string? ConnectedDeviceName, string? ConnectedFirmware, int ThresholdPercent,
    LibraryMatchResult? Result, LibraryPresetDifference[] NameDifferences);

/// <summary>Last completed check evidence, kept separate from the saved baseline.</summary>
public sealed class LibraryCheckReportStore(string directory)
{
    public void Save(LibraryCheckReport report)
    {
        string folder = Path.Combine(directory, "Checks");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, report.LibraryId.ToString("N") + ".json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, IndexJson.Options));
            if (File.Exists(path)) { File.Copy(path, path + ".bak", true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
