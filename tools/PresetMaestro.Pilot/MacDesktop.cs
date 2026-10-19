using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PresetMaestro.Pilot;

internal static class Desktop
{
#if MAESTRO_PHYSICAL_MAC
    private const string DriverName = "Maestro Physical Desktop Tests.app";
#else
    private const string DriverName = "Maestro Desktop Tests.app";
#endif
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NSApplicationLoad();
    internal static void Run(Checks report)
    {
        if (Environment.GetEnvironmentVariable("MAESTRO_PILOT_GUI") != "1")
        {
            LaunchAsApplication(report);
            return;
        }
        Checks.Require(NSApplicationLoad(), "Could not initialize the Mac pilot application.");
        Environment.CurrentDirectory = Environment.GetEnvironmentVariable("MAESTRO_PILOT_ROOT") ?? throw new IOException("Missing bundle root.");
        if (MacDriver.SessionProblem() is { } sessionProblem) { throw new PrerequisiteException(sessionProblem); }
        if (!MacDriver.AccessibilityTrusted())
        {
            throw new PrerequisiteException($"Allow {DriverName} in macOS System Settings > Privacy & Security > Device Control and Data Access (Accessibility on older macOS), then restart the agent. No desktop scenarios ran.");
        }
        if (!MacDriver.ScreenCaptureAllowed())
        {
            throw new PrerequisiteException($"Allow {DriverName} in macOS Screen & System Audio Recording, then restart the agent. Screenshots are required evidence.");
        }
        using var stop = new CancellationTokenSource();
        Process? activeApp = null;
        int parentId = int.Parse(Environment.GetEnvironmentVariable("MAESTRO_PILOT_PARENT")!, System.Globalization.CultureInfo.InvariantCulture);
        long parentStarted = long.Parse(Environment.GetEnvironmentVariable("MAESTRO_PILOT_PARENT_STARTED")!, System.Globalization.CultureInfo.InvariantCulture);
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                bool alive;
                try { using var parent = Process.GetProcessById(parentId); alive = !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == parentStarted; }
                catch (ArgumentException) { alive = false; }
                if (!alive)
                {
                    try { if (activeApp is { HasExited: false }) { activeApp.Kill(true); } }
                    finally { Environment.Exit(1); }
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
        });
        try
        {
#if MAESTRO_PHYSICAL_MAC
            NoMidiDesktopScenario.Run(report, app => { activeApp = app; return new MacDriver(app); });
#else
            DesktopScenario.Run(report, app => { activeApp = app; return new MacDriver(app); });
#endif
        }
        finally { stop.Cancel(); }
    }
    private static void LaunchAsApplication(Checks report)
    {
        string app = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "..", ".."));
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        using var parent = Process.GetCurrentProcess();
        string childResult = Path.Combine(report.Directory, "mac-desktop-result.json");
        File.Delete(childResult);
        foreach (string argument in new[] { "-n", "-W", "--stdout", Path.Combine(report.Directory, "mac-desktop.stdout.txt"),
            "--stderr", Path.Combine(report.Directory, "mac-desktop.stderr.txt"),
            "--env", "MAESTRO_PILOT_GUI=1", "--env", "MAESTRO_PILOT_ROOT=" + Environment.CurrentDirectory,
            "--env", "MAESTRO_PILOT_PARENT=" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--env", "MAESTRO_PILOT_PARENT_STARTED=" + parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            app, "--args", "desktop", "--result", childResult }) { start.ArgumentList.Add(argument); }
        using var launch = Process.Start(start) ?? throw new IOException("Could not launch the Mac test application.");
        if (!launch.WaitForExit(300000)) { launch.Kill(true); throw new TimeoutException("Mac desktop pilot did not finish."); }
        Checks.Require(launch.ExitCode == 0, "Could not launch the Mac test application.");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { report.Import(childResult); break; }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException)
            {
                if (timer.Elapsed > TimeSpan.FromMinutes(5)) { throw new TimeoutException("Mac test application did not return its result.", error); }
                Thread.Sleep(100);
            }
        }
    }
}

internal sealed class MacDriver : IDesktopDriver
{
    private const string AX = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private readonly nint application;
    private readonly int pid;
    private readonly nint window;
    private static readonly nint True = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CF), "kCFBooleanTrue"));

    [DllImport(AX, EntryPoint = "AXIsProcessTrusted")][return: MarshalAs(UnmanagedType.I1)] internal static extern bool AccessibilityTrusted();
    [DllImport(CG, EntryPoint = "CGPreflightScreenCaptureAccess")][return: MarshalAs(UnmanagedType.I1)] internal static extern bool ScreenCaptureAllowed();
    [DllImport(AX)] private static extern nint AXUIElementCreateApplication(int pid);
    [DllImport(AX)] private static extern int AXUIElementCopyAttributeValue(nint element, nint attribute, out nint value);
    [DllImport(AX)] private static extern int AXUIElementSetAttributeValue(nint element, nint attribute, nint value);
    [DllImport(AX)] private static extern int AXUIElementPerformAction(nint element, nint action);
    [DllImport(AX)] private static extern nint AXValueCreate(int type, ref Size value);
    [DllImport(CF, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)] private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CF)][return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(nint value, byte[] buffer, nint size, uint encoding);
    [DllImport(CF)] private static extern nint CFArrayGetCount(nint array);
    [DllImport(CF)] private static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(CF)] private static extern nint CFRetain(nint value);
    [DllImport(CF)] private static extern void CFRelease(nint value);
    [DllImport(CF)][return: MarshalAs(UnmanagedType.I1)] private static extern bool CFEqual(nint first, nint second);
    [DllImport(CF)][return: MarshalAs(UnmanagedType.I1)] private static extern bool CFBooleanGetValue(nint value);
    [DllImport(CF)] private static extern nint CFGetTypeID(nint value);
    [DllImport(CF)] private static extern nint CFStringGetTypeID();
    [DllImport(CF)] private static extern nint CFDictionaryGetValue(nint dictionary, nint key);
    [DllImport(CF)][return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(nint value, int type, out int result);
    [DllImport(CG)] private static extern nint CGEventCreateKeyboardEvent(nint source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CG)] private static extern void CGEventPostToPid(int pid, nint value);
    [DllImport(CG)] private static extern nint CGWindowListCopyWindowInfo(uint options, uint relativeTo);
    [DllImport(CG)] private static extern nint CGSessionCopyCurrentDictionary();
    [DllImport(CF)] private static extern nint CFBooleanGetTypeID();
    [StructLayout(LayoutKind.Sequential)] private struct Size { internal double Width; internal double Height; }

    internal static string? SessionProblem()
    {
        nint session = CGSessionCopyCurrentDictionary();
        if (session == 0) { return "Desktop tests need a signed-in Mac GUI session. No desktop scenarios ran."; }
        try
        {
            bool? Flag(string name)
            {
                nint key = String(name);
                try
                {
                    nint value = CFDictionaryGetValue(session, key);
                    return value != 0 && CFGetTypeID(value) == CFBooleanGetTypeID() ? CFBooleanGetValue(value) : null;
                }
                finally { Release(key); }
            }
            if (Flag("kCGSSessionOnConsoleKey") != true) { return "The test user is not on the active Mac console. Sign in to its desktop before running Desktop."; }
            // macOS omits this optional key when the console session is unlocked.
            // Actual window focus is checked again before any keyboard input.
            return Flag("CGSSessionScreenIsLocked") == true
                ? "The Mac screen is locked. Unlock the existing test session before running Desktop; no desktop scenarios ran."
                : null;
        }
        finally { Release(session); }
    }

    internal MacDriver(Process process)
    {
        pid = process.Id;
        application = AXUIElementCreateApplication(pid);
        nint found = 0;
        Checks.Wait(() =>
        {
            nint windows = Attribute(application, "AXWindows");
            try
            {
                if (windows != 0)
                {
                    if (CFArrayGetCount(windows) > 0) { found = CFRetain(CFArrayGetValueAtIndex(windows, 0)); }
                }
            }
            finally { Release(windows); }
            return found != 0;
        }, "Maestro's accessible native window did not open.");
        window = found;
        Activate();
        Checks.Wait(() =>
        {
            nint navigation = Search(application, "NavConfig");
            try { return navigation != 0; }
            finally { Release(navigation); }
        }, "Maestro's main window controls did not become accessible after activation.");
    }
    private static nint String(string text) => CFStringCreateWithCString(0, text, 0x08000100);
    private static void Release(nint value) { if (value != 0) { CFRelease(value); } }
    private static nint Attribute(nint element, string name)
    {
        nint key = String(name);
        try { return AXUIElementCopyAttributeValue(element, key, out nint value) == 0 ? value : 0; }
        finally { Release(key); }
    }
    private static string TextAttribute(nint element, string name)
    {
        nint value = Attribute(element, name);
        try
        {
            if (value == 0 || CFGetTypeID(value) != CFStringGetTypeID()) { return ""; }
            byte[] bytes = new byte[8192];
            return CFStringGetCString(value, bytes, bytes.Length, 0x08000100) ? Encoding.UTF8.GetString(bytes).TrimEnd('\0') : "";
        }
        finally { Release(value); }
    }
    private static void Set(nint element, string name, nint value)
    {
        nint key = String(name);
        try { int error = AXUIElementSetAttributeValue(element, key, value); Checks.Require(error == 0, $"macOS could not set {name}: {error}"); }
        finally { Release(key); }
    }
    private static void Action(nint element, string name)
    {
        nint action = String(name);
        try { int error = AXUIElementPerformAction(element, action); Checks.Require(error == 0, $"macOS action {name} failed: {error}"); }
        finally { Release(action); }
    }
    private static nint Search(nint element, string id, int depth = 0)
    {
        if (TextAttribute(element, "AXIdentifier") == id) { return CFRetain(element); }
        if (depth >= 40) { return 0; }
        nint children = Attribute(element, "AXChildren");
        try
        {
            if (children != 0)
            {
                for (nint i = 0; i < CFArrayGetCount(children); i++)
                {
                    nint found = Search(CFArrayGetValueAtIndex(children, i), id, depth + 1);
                    if (found != 0) { return found; }
                }
            }
            return 0;
        }
        finally { Release(children); }
    }
    private nint Find(string id)
    {
        nint found = 0;
        Checks.Wait(() => (found = Search(application, id)) != 0, "Accessible control missing: " + id);
        return found;
    }
    private bool HasKeyboardFocus()
    {
        nint front = Attribute(application, "AXFrontmost"), focused = Attribute(application, "AXFocusedWindow");
        try { return front != 0 && CFBooleanGetValue(front) && focused != 0 && CFEqual(focused, window); }
        finally { Release(front); Release(focused); }
    }
    private void Activate()
    {
        // AXRaise is optional and can be unsupported while the native window appears.
        // Require the actual outcome: this application's main window owns keyboard focus.
        Set(application, "AXFrontmost", True);
        try { Checks.Wait(HasKeyboardFocus, "Maestro's main window did not gain keyboard focus."); }
        catch
        {
            nint focused = Attribute(application, "AXFocusedWindow");
            try { Console.Error.WriteLine($"Window: {TextAttribute(window, "AXRole")} / {TextAttribute(window, "AXTitle")}; focused: {TextAttribute(focused, "AXRole")} / {TextAttribute(focused, "AXTitle")}"); }
            finally { Release(focused); }
            throw;
        }
    }
    public void Invoke(string id) { nint element = Find(id); try { Action(element, "AXPress"); } finally { Release(element); } }
    public string Text(string id)
    {
        nint element = Find(id);
        try { string value = TextAttribute(element, "AXValue"); return value.Length > 0 ? value : TextAttribute(element, "AXTitle"); }
        finally { Release(element); }
    }
    public bool Enabled(string id)
    {
        nint element = Find(id), enabled = Attribute(element, "AXEnabled");
        try { return enabled != 0 && CFBooleanGetValue(enabled); }
        finally { Release(enabled); Release(element); }
    }
    public void Type(string keys)
    {
        Activate();
        foreach (char key in keys)
        {
            Checks.Require(HasKeyboardFocus(), "Maestro's main window lost keyboard focus before input.");
            ushort code = key switch { '0' => 29, '1' => 18, '2' => 19, '3' => 20, '4' => 21, '5' => 23, '6' => 22, '7' => 26, '8' => 28, '9' => 25, '\n' => 36, _ => throw new ArgumentException("Unsupported test key.") };
            foreach (bool down in new[] { true, false })
            {
                nint e = CGEventCreateKeyboardEvent(0, code, down);
                try { CGEventPostToPid(pid, e); } finally { Release(e); }
            }
            Thread.Sleep(100);
        }
    }
    public void Resize(int width, int height)
    {
        var size = new Size { Width = width, Height = height };
        nint value = AXValueCreate(2, ref size);
        try { Set(window, "AXSize", value); } finally { Release(value); }
        Thread.Sleep(250);
    }
    public void Capture(string path)
    {
        int number = 0;
        nint windows = CGWindowListCopyWindowInfo(1, 0), ownerKey = String("kCGWindowOwnerPID"), numberKey = String("kCGWindowNumber");
        try
        {
            for (nint i = 0; i < CFArrayGetCount(windows); i++)
            {
                nint row = CFArrayGetValueAtIndex(windows, i);
                if (CFNumberGetValue(CFDictionaryGetValue(row, ownerKey), 9, out int owner) && owner == pid)
                { CFNumberGetValue(CFDictionaryGetValue(row, numberKey), 9, out number); break; }
            }
        }
        finally { Release(windows); Release(ownerKey); Release(numberKey); }
        Checks.Require(number != 0, "Could not locate the app window for screenshot evidence.");
        var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false };
        foreach (string arg in new[] { "-x", "-l", number.ToString(System.Globalization.CultureInfo.InvariantCulture), path }) { start.ArgumentList.Add(arg); }
        using var capture = Process.Start(start)!;
        Checks.Require(capture.WaitForExit(10000) && capture.ExitCode == 0 && File.Exists(path), "Window screenshot failed.");
    }
    public void Close()
    {
        nint button = Attribute(window, "AXCloseButton");
        try { Checks.Require(button != 0, "No accessible close button."); Action(button, "AXPress"); }
        finally { Release(button); }
    }
    public void Dispose() { Release(window); Release(application); }
}
