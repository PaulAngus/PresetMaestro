using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace PresetMaestro.Pilot;

internal static class Desktop
{
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    internal static void Run(Checks report)
    {
        nint previous = SetThreadDpiAwarenessContext(-4); // UIA coordinates are physical pixels.
        try
        {
#if MAESTRO_PHYSICAL_HARDWARE
            NoMidiDesktopScenario.Run(report, app => new WindowsDriver(app));
#else
            DesktopScenario.Run(report, app => new WindowsDriver(app));
#endif
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
}

internal sealed class WindowsDriver : IDesktopDriver
{
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetDpiForWindow(nint handle);
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint MonitorFromWindow(nint handle, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { internal uint Size; internal Rect Monitor; internal Rect Work; internal uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    private readonly UIA3Automation automation = new();
    private readonly Application application;
    private readonly Window window;
    internal WindowsDriver(Process process)
    {
        application = Application.Attach(process.Id);
        Window? found = null;
        Checks.Wait(() => (found = application.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Preset Maestro")) is not null, "Maestro main window did not open.");
        window = found!;
        _ = Find("NavConfig");
        Checks.Wait(() => !window.IsOffscreen, "Maestro window is not visible.");
        EnsureDisplayCapacity(1440, 900);
        Activate();
    }
    private AutomationElement Find(string id)
    {
        AutomationElement? element = null;
        Checks.Wait(() => (element = application.GetAllTopLevelWindows(automation).Select(w => w.FindFirstDescendant(cf => cf.ByAutomationId(id))).FirstOrDefault(e => e is not null && !e.IsOffscreen)) is not null, "Visible control missing: " + id);
        return element!;
    }
    private void Activate()
    {
        Console.WriteLine($"Activating {window.Title} ({window.Properties.NativeWindowHandle.Value}), bounds {window.BoundingRectangle}; foreground {GetForegroundWindow()}.");
        for (int attempt = 0; attempt < 3 && GetForegroundWindow() != window.Properties.NativeWindowHandle.Value; attempt++)
        {
            window.Focus();
            if (GetForegroundWindow() != window.Properties.NativeWindowHandle.Value)
            { (window.TitleBar ?? throw new IOException("No title bar to activate Maestro.")).Click(); }
            Thread.Sleep(600);
        }
        Console.WriteLine($"After activation: {GetForegroundWindow()} ({automation.FromHandle(GetForegroundWindow()).Name}), title bar {window.TitleBar?.BoundingRectangle}.");
        Checks.Wait(() => GetForegroundWindow() == window.Properties.NativeWindowHandle.Value, "Desktop must be unlocked and idle.");
    }
    public void Invoke(string id)
    {
        var button = Find(id).AsButton();
        if (id.StartsWith("Nav", StringComparison.Ordinal))
        {
            // A real client click activates the native keyboard input queue too;
            // UIA focus alone can report ready while the first key is still lost.
            button.Click();
            Checks.Wait(() => button.Properties.HasKeyboardFocus.Value, "Navigation control did not acquire keyboard focus: " + id);
        }
        else { button.Invoke(); }
    }
    public string Text(string id) => id is "MidiInput" or "MidiOutput" ? Find(id).AsComboBox().SelectedItem?.Text ?? "" : Find(id).Name;
    public bool Enabled(string id) => Find(id).IsEnabled;
    public void Type(string keys)
    {
        Activate();
        foreach (char key in keys)
        {
            Checks.Require(GetForegroundWindow() == window.Properties.NativeWindowHandle.Value, "Maestro lost the foreground before input.");
            Keyboard.Type(key == '\n' ? VirtualKeyShort.RETURN : (VirtualKeyShort)key);
            Thread.Sleep(100);
        }
    }
    public void Resize(int width, int height)
    {
        EnsureDisplayCapacity(width, height);
        nint handle = window.Properties.NativeWindowHandle.Value;
        double scale = GetDpiForWindow(handle) / 96.0;
        int targetWidth = (int)Math.Round(width * scale), targetHeight = (int)Math.Round(height * scale);
        Checks.Require(GetClientRect(handle, out var client), "Could not measure Maestro's client area.");
        Checks.Require(GetWindowRect(handle, out var outer), "Could not measure Maestro's native window.");
        // Size the client area in device-independent units, including the native frame.
        Checks.Require(SetWindowPos(handle, 0, 0, 0, targetWidth + outer.Right - outer.Left - client.Right,
            targetHeight + outer.Bottom - outer.Top - client.Bottom, 0x16), "Could not resize Maestro's native window.");
        Checks.Wait(() => GetClientRect(handle, out var actual) && Math.Abs(actual.Right - targetWidth) <= 2 && Math.Abs(actual.Bottom - targetHeight) <= 2,
            "Maestro did not reach the requested client size.");
        Thread.Sleep(250);
    }
    private void EnsureDisplayCapacity(int width, int height)
    {
        nint handle = window.Properties.NativeWindowHandle.Value;
        double scale = GetDpiForWindow(handle) / 96.0;
        var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        Checks.Require(GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor), "Could not read the test monitor's working area.");
        Checks.Require(GetClientRect(handle, out var client), "Could not measure the test client area.");
        Checks.Require(GetWindowRect(handle, out var outer), "Could not measure the test window frame.");
        int requiredWidth = (int)Math.Round(width * scale) + outer.Right - outer.Left - client.Right;
        int requiredHeight = (int)Math.Round(height * scale) + outer.Bottom - outer.Top - client.Bottom;
        int availableWidth = monitor.Work.Right - monitor.Work.Left, availableHeight = monitor.Work.Bottom - monitor.Work.Top;
        if (availableWidth < requiredWidth || availableHeight < requiredHeight)
        {
            throw new PrerequisiteException($"Desktop checks require a {width}x{height} client viewport ({requiredWidth}x{requiredHeight} pixels with this monitor's scale/frame). The current working area is {availableWidth}x{availableHeight}. Restore the normal display resolution; no viewport assertion was relaxed.");
        }
    }
    public void Capture(string path) { Activate(); Thread.Sleep(200); FlaUI.Core.Capturing.Capture.Element(window).ToFile(path); }
    public void Close() => window.Close();
    public void Dispose() { application.Dispose(); automation.Dispose(); }
}
