namespace PresetMaestro.Midi;

/// <summary>Owns connection identity and invalidates work from earlier connections.</summary>
internal sealed class DeviceConnectionSession
{
    public long Generation { get; private set; }
    public CancellationTokenSource? Pending { get; private set; }
    public FractalDeviceInformation? Device { get; internal set; }
    public CancellationTokenSource Begin()
    {
        if (Pending is not null) { throw new InvalidOperationException("A connection is already in progress."); }
        Generation++;
        return Pending = new CancellationTokenSource();
    }
    public void Finish(CancellationTokenSource operation)
    {
        if (ReferenceEquals(Pending, operation)) { Pending = null; }
    }
    public void Disconnect()
    {
        Generation++;
        Pending?.Cancel();
        Device = null;
    }
}
