using System.Globalization;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using PresetMaestro.Midi;

namespace PresetMaestro.Platforms;

// DryWetMIDI owns CoreMIDI's native callbacks, packet decoding and SysEx reassembly.
// Only this platform's source and native library are included in the Mac build.
internal static class MidiBackend
{
    public static IReadOnlyList<(string Id, string Name)> GetInputDevices()
    {
        var devices = new List<(string Id, string Name)>();
        for (int i = 0, count = InputDevice.GetDevicesCount(); i < count; i++)
        {
            try
            {
                using var device = InputDevice.GetByIndex(i);
                devices.Add((i.ToString(CultureInfo.InvariantCulture), device.Name));
            }
            catch (MidiDeviceException) { } // A removed endpoint must not hide the others.
        }
        return devices;
    }

    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices()
    {
        var devices = new List<(int Index, string Name)>();
        for (int i = 0, count = OutputDevice.GetDevicesCount(); i < count; i++)
        {
            try
            {
                using var device = OutputDevice.GetByIndex(i);
                devices.Add((i, device.Name));
            }
            catch (MidiDeviceException) { }
        }
        return devices;
    }

    public static IMidiInputPort OpenInput(string id) =>
        new MacInput(InputDevice.GetByIndex(int.Parse(id, CultureInfo.InvariantCulture)));

    public static IMidiOutputPort OpenOutput(int index) => new MacOutput(OutputDevice.GetByIndex(index));

    private sealed class MacInput : IMidiInputPort
    {
        private readonly InputDevice _device;
        private readonly object _conversionLock = new();
        private readonly MidiEventToBytesConverter _converter = MacMidiCodec.CreateEncoder();
        private bool _disposed;

        public MacInput(InputDevice device)
        {
            _device = device;
            _device.WaitForCompleteSysExEvent = true;
            _device.RaiseMidiTimeCodeReceived = false;
            _device.EventReceived += Receive;
            _device.ErrorOccurred += ReceiveError;
        }

        public event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
        public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived;
        public event EventHandler<string>? ErrorReceived;
        public void Start() => _device.StartEventsListening();
        public void Stop() => _device.StopEventsListening();

        private void Receive(object? sender, MidiEventReceivedEventArgs e)
        {
            try
            {
                byte[] bytes;
                lock (_conversionLock)
                {
                    if (_disposed) { return; }
                    bytes = _converter.Convert(e.Event);
                }
                if (bytes.Length == 0) { return; }
                if (bytes[0] == 0xf0)
                {
                    SysexMessageReceived?.Invoke(this, new(bytes));
                }
                else if (bytes.Length <= 3)
                {
                    int raw = bytes[0];
                    for (int i = 1; i < bytes.Length; i++) { raw |= bytes[i] << (8 * i); }
                    MessageReceived?.Invoke(this, new(raw));
                }
            }
            catch (Exception ex) { ReportError(ex); } // Never unwind through a native callback.
        }

        private void ReceiveError(object? sender, ErrorOccurredEventArgs e) => ReportError(e.Exception);

        private void ReportError(Exception error)
        {
            try { ErrorReceived?.Invoke(this, error.Message); }
            catch { } // Diagnostic subscribers cannot throw into CoreMIDI.
        }

        public void Dispose()
        {
            lock (_conversionLock)
            {
                if (_disposed) { return; }
                _disposed = true;
            }
            _device.EventReceived -= Receive;
            _device.ErrorOccurred -= ReceiveError;
            // Do not hold the conversion lock while the native driver drains callbacks.
            try { _device.Dispose(); }
            finally { lock (_conversionLock) { _converter.Dispose(); } }
        }
    }

    private sealed class MacOutput(OutputDevice device) : IMidiOutputPort
    {
        private readonly BytesToMidiEventConverter _converter = MacMidiCodec.CreateDecoder();
        // MidiManager serializes all sends and disposal, including forwarded messages.
        public void Send(int message) => device.SendEvent(_converter.Convert(MidiMessageEncoding.GetShortMessageBytes(message)));
        public void SendBuffer(byte[] frame) => device.SendEvent(_converter.Convert(frame));
        public void Dispose()
        {
            try { device.Dispose(); }
            finally { _converter.Dispose(); }
        }
    }
}

internal static class MacMidiCodec
{
    // File is the library default; wire format is required to avoid adding a SysEx length prefix.
    internal static MidiEventToBytesConverter CreateEncoder() => new() { BytesFormat = BytesFormat.Device, UseRunningStatus = false };
    internal static BytesToMidiEventConverter CreateDecoder() => new() { BytesFormat = BytesFormat.Device };
}
