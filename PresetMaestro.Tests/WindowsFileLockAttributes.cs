using Xunit.Sdk;

namespace PresetMaestro.Tests;

// Windows FileShare denies rename/delete. POSIX permits replacing an open inode, so
// these Windows failure-injection cases do not represent a macOS persistence failure.
internal sealed class WindowsFileLockFactAttribute : FactAttribute
{
    public WindowsFileLockFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "Tests Windows mandatory file-sharing semantics."; }
    }
}

internal sealed class WindowsFileLockTheoryAttribute : TheoryAttribute
{
    public WindowsFileLockTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "Tests Windows mandatory file-sharing semantics."; }
    }
}

[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")]
internal sealed class WindowsFileLockAvaloniaFactAttribute : FactAttribute
{
    public WindowsFileLockAvaloniaFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) { Skip = "Tests Windows mandatory file-sharing semantics."; }
    }
}
