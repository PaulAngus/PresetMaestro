#if MAESTRO_WINDOWS
using PresetMaestro.Midi;
using Windows.Devices.Midi2;
using Windows.Devices.Midi2.Transports.Loopback;

namespace PresetSender;

// A symmetric loopback pair avoids the service's virtual-device removal deadlock.
internal sealed class WindowsVirtualMidiOutput : IMidiOutputPort
{
    private MidiSession? _session;
    private MidiEndpointConnection? _connection;
    private Guid _associationId;
    private Mutex? _instance;

    public static WindowsVirtualMidiOutput Open()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200))
        {
            throw new PlatformNotSupportedException("The built-in virtual output requires Windows 11 25H2 or later with Windows MIDI Services. You can still select an existing MIDI output.");
        }

        var port = new WindowsVirtualMidiOutput();
        try
        {
            port._instance = new Mutex(false, @"Local\PresetSender.VirtualMidiOutput", out bool created);
            if (!created) { throw new InvalidOperationException("Preset Sender is already publishing its MIDI device in another instance. Disconnect that instance first."); }
            port.Initialize();
            return port;
        }
        catch (Exception ex)
        {
            port.Dispose();
            throw new InvalidOperationException($"Could not create the Preset Sender MIDI device. Check that Windows MIDI Services is enabled and up to date, or select an existing MIDI output. {ex.Message}", ex);
        }
    }

    private void Initialize()
    {
        if (!MidiApi.EnsureServiceAvailable() || !MidiLoopbackManager.IsTransportAvailable)
        {
            throw new InvalidOperationException("Windows MIDI Services loopback transport is unavailable.");
        }

        _session = MidiSession.Create("Preset Sender") ?? throw new IOException("Could not start a MIDI session.");
        string uniqueId = Guid.NewGuid().ToString("N");
        var relay = new MidiLoopbackEndpointDefinition
        {
            Name = "Preset Sender (Relay)",
            Description = "Internal Preset Sender output connection",
            UniqueId = uniqueId + "a",
            CreateOnlyUmpEndpoint = false
        };
        var application = new MidiLoopbackEndpointDefinition
        {
            Name = "Preset Sender",
            Description = "Preset Sender program changes",
            UniqueId = uniqueId + "b",
            CreateOnlyUmpEndpoint = false
        };
        var result = MidiLoopbackManager.CreateTransientLoopback(new MidiLoopbackCreationConfig(relay, application));
        if (!result.Success) { throw new IOException("Could not publish the MIDI device."); }
        _associationId = result.CreatedLoopbackEntry.AssociationId;
        _connection = _session.CreateEndpointConnection(result.CreatedLoopbackEntry.EndpointA.EndpointDeviceId)
            ?? throw new IOException("Could not create the device connection.");
        if (!_connection.Open())
        {
            throw new IOException("Could not open the device connection.");
        }
    }

    public void Send(int message)
    {
        ObjectDisposedException.ThrowIf(_connection is null, this);
        if ((message & 0xf0) != 0xc0) { throw new ArgumentException("Only Program Change messages are supported.", nameof(message)); }
        uint ump = 0x20000000u | ((uint)(message & 0xff) << 16) | ((uint)((message >> 8) & 0x7f) << 8);
        var result = _connection.SendSingleMessageWords(0, ump);
        if (MidiEndpointConnection.SendMessageFailed(result)) { throw new IOException($"Virtual MIDI send failed ({result})."); }
    }

    public void SendBuffer(byte[] frame) => throw new NotSupportedException("Preset Sender sends only Program Change messages.");

    public void Dispose()
    {
        try
        {
            if (_connection is not null) { _session?.DisconnectEndpointConnection(_connection.ConnectionId); }
            _session?.Dispose();
        }
        finally
        {
            try
            {
                if (_associationId != Guid.Empty)
                {
                    MidiLoopbackManager.RemoveTransientLoopback(new MidiLoopbackRemovalConfig(_associationId));
                }
            }
            finally
            {
                _connection = null; _session = null; _associationId = Guid.Empty;
                _instance?.Dispose(); _instance = null;
            }
        }
    }
}
#endif


