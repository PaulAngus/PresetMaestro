#if MAESTRO_PHYSICAL_WINDOWS
using System.Runtime.InteropServices;

// Execution-state assertions belong to the requesting thread. Restore them on
// that same thread when the bounded test runner exits, including failed runs.
internal sealed class WindowsAwakeAssertion : IDisposable
{
    private readonly ManualResetEventSlim stop = new();
    private readonly Thread owner;
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SetThreadExecutionState(uint flags);

    internal WindowsAwakeAssertion()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner = new Thread(() =>
        {
            if (SetThreadExecutionState(0x80000003) == 0)
            {
                ready.SetException(new IOException("Could not hold the Windows test desktop awake."));
                return;
            }
            ready.SetResult();
            try { stop.Wait(); }
            finally { _ = SetThreadExecutionState(0x80000000); }
        })
        { IsBackground = true, Name = "Physical desktop awake assertion" };
        owner.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    public void Dispose() { stop.Set(); owner.Join(); stop.Dispose(); }
}
#endif
