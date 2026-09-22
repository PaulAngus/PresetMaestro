# FM9 firmware 12.0 hardware fixtures

Source: user-supplied `usb-capture.pcapng`, recorded with FM9-Edit while the user
set the configured name to `PM-TEST`, then explicitly to `FM9`.
SHA-256: `1968C6C12E1E6FB9B00888C6920E0B336EF0385A1C9620687D01D0C3A932F44A`.

USBPcap bus 1, device 1; OUT endpoint 0x02, IN endpoint 0x82.
USB-MIDI event packets were reassembled by cable and direction, removing CIN
headers and unused terminal bytes. All listed frames have valid XOR checksums.
Packet numbers count Enhanced Packet Blocks across both interfaces in file order.

| Fixture | USB packet ending the frame | Direction |
| --- | ---: | --- |
| identity-request.hex | 835 | OUT |
| identity-fm9.hex | 843 | IN |
| name-request.hex | 118953 | OUT |
| name-pm-test.hex | 118993 | IN |
| name-fm9.hex | 146113 | IN |

The same name request also appears during startup at packet 893. The PM-TEST
response repeats at packets 120207 and 122269; FM9 repeats at 147077 and 149305.
These are complete observed read transactions, not inferred parameter commands.

Response length is 60 bytes. Bytes 5..20 are the correlated read envelope.
Bytes 21..57 are 37 seven-bit values. Concatenating their bits MSB first yields
32 decoded bytes followed by three zero bits. The decoded text is ASCII and
NUL padded. No separate septet mask or one-byte visible-name length was observed.
The parser requires the captured envelope and a printable name of at most eight
characters; it preserves spaces before the first NUL and rejects nonzero padding.

No blank or eight-character maximum name was captured. Those tests are explicitly
synthetic boundary tests. Setting the name to `FM9` does not establish the device's
behavior when its name is cleared. No FM3 or Axe-Fx III transaction is captured.
