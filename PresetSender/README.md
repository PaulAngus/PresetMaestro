# Preset Sender

A separate compact MIDI Program Change utility. It reuses PresetMaestro's Windows and macOS MIDI backends and message encoder, without its profiles, favorites, Fractal protocols, device queries, or preset scans. The utility has its own settings and release version.

## Use

1. Open `PresetSender.exe`. Expand **MIDI connection** and select **Preset Sender (virtual device)** to publish an input that other apps on this PC can receive from. Click **Connect**, then select **Preset Sender** in the receiving app's MIDI input list. Alternatively, select an existing hardware or virtual output. An input controller is optional; the computer keyboard needs none. Connecting sends no program changes (virtual MIDI protocol discovery happens automatically). Connection settings collapse after connection; **Disconnect** remains visible.
2. In **Program options**, set **Output channel** to a single channel (1–16) or **All channels**, and choose **Preset numbering**. All channels sends the same Program Change once on each of the 16 channels. This is the sending equivalent of the requested omni option; MIDI's formal Omni mode is a receiver setting. A receiver listening to all channels may process the change 16 times, so prefer a single channel for that receiver. The default is channel 1 and zero-based numbering: displayed `42` sends MIDI PC 42. One-based numbering sends PC 41 for displayed `42`.
3. Click a keypad button to focus entry, then type `42` and press **Enter**, or click **Send**. Top-row and numpad digits work. Digits never send automatically. **Escape/Clear** discards the pending entry; **Backspace** removes its last digit. Successful sending clears entry and records the sent number, wire PC value, and channel. A failed send preserves entry and disconnects the ports.
4. Entry always supports up to **3 digits**, covering the complete MIDI PC range: 0–127 or displayed 1–128. One- and two-digit entries also work, without changing a setting. A fourth digit is rejected until you clear, edit, or send the current entry.

Computer keyboard entry works while this utility is focused, with focus in the keypad. Configuration controls keep their own keyboard behavior. MIDI controller input works while connected, including when another application has focus. No global keyboard hooks are installed.

## MIDI controllers

Expand **MIDI controls**. Choose a digit, **Send**, or **Clear** in **Command to learn**, click **Learn**, and press the corresponding controller button. Repeat for the commands you need. Notes, control changes (CC), and program changes (PC) can all be learned; the exact incoming MIDI channel is part of the mapping. **Input channel** can additionally restrict reception, or accept any channel. Learning consumes the incoming press and sends nothing. **Cancel learn** or Escape exits learning. **Remove mapping** removes the selected command's assignment.

Nonzero note-on and CC values count as presses; release with note-off, velocity-zero note-on, or CC value zero before pressing again. Repeated nonzero values are ignored until release. For controllers that only emit nonzero CC values, use their momentary press/release mode or PC messages. PC mappings fire on every incoming PC, since PC has no release message. Mapping a new control replaces that command's previous mapping and any command previously using the same control.

**Forward incoming program changes** optionally resends unmapped PCs immediately on the selected output channel. Learned PC commands take precedence. Forwarding is independent of the confirmation used for digit entry. Other incoming messages are ignored; this is not an all-message MIDI thru. Avoid routing the output back into the selected input, which would create a MIDI feedback loop.

## AmpliTube and other applications

On Windows 11 25H2 or later with compatible, updated Windows MIDI Services components, **Preset Sender (virtual device)** publishes a named MIDI source while connected. Other apps select **Preset Sender** as an **input**, including legacy MIDI 1.0 apps when Windows MIDI Services' legacy API integration is enabled. The internal **Preset Sender (Relay)** port also appears; choose the name without that suffix. A symmetric Windows loopback pair avoids the virtual-device cleanup path that stalled MidiSrv during coexistence testing. If a receiver only discovers ports at startup, connect Preset Sender before starting that receiver. Disconnecting or closing Preset Sender removes both ports. Only one instance can publish this device at a time. No third-party virtual MIDI driver is installed.

This implementation uses Microsoft's **Windows MIDI Services preview 10 API** (`0.99.88-preview.10`). Consult the [Microsoft minimum requirements](https://microsoft.github.io/MIDI/kb/minimum-requirements/) and [preview release notes and distribution terms](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-10). Public distribution of the preview API requires the developer to meet those terms, including updates as the API changes. The package ships its MIDI DLL and resource file beside the executable; keep those files together. The existing hardware-output path remains usable on older Windows versions.

**Native validation (11 October 2026):** the native integration check passed with the symmetric loopback publisher: two simultaneous legacy MIDI receivers both received PC 42 on channel 6 and PC 127 on each of channels 1–16, and the endpoints disappeared on disconnect. The published sender also passed MIDI Forwarder 1.0.2's coexistence check: both startup orders, two receivers on all channels, three forwarder start/stop cycles, bidirectional forwarding/SysEx and clean sender shutdown. No physical hardware was used in that check. Specific target-app preset mapping, such as AmpliTube, remains a separate setup step. The isolated sender host limits an unresponsive-service connection attempt to 15 seconds.

For hardware, select the device's USB MIDI output or the output of its MIDI interface. A software virtual source does not automatically route to physical MIDI cables or other computers. On macOS, use an existing virtual port such as an IAC bus; the built-in publisher is Windows-only. The target must accept standard MIDI 1.0 Program Change messages and listen on the selected channel.

In AmpliTube, assign the desired presets to MIDI program numbers and enable the corresponding MIDI input. IK documents MIDI preset switching in AmpliTube 5's standalone live mode. For a plug-in, your DAW must route MIDI into the plug-in; availability depends on the host and plug-in format. See [IK's AmpliTube specifications](https://www.ikmultimedia.com/products/amplitube5/?p=specs) and [IK's MIDI routing FAQ](https://www.ikmultimedia.com/support/faq/index.php?topic=amplitube-4) (the routing FAQ refers to AmpliTube 4).

The entered number is encoded as one standard MIDI program value. It sends only PC; it does not send bank-select CCs, read preset names, or configure the receiving app's preset assignments.

Settings are saved separately under `%APPDATA%\PresetSender\settings.json` on Windows, or the platform's application-data directory on macOS. Ports, virtual-output choice, numbering, channels, and learned commands are retained. Output channel `0` stores All channels. Startup restores selections but stays disconnected. Existing saved hardware selections remain selected; new Windows configurations default to the built-in virtual device. A missing saved output requires an explicit new selection. Older saved two-digit settings are ignored; entry always accepts three digits.

## Develop and package

From the repository root with .NET 10:

```powershell
./PresetSender/Initialize-MidiSdk.ps1
dotnet run --project PresetSender/PresetSender.csproj
dotnet test PresetSender/PresetSender.slnx -c Release
dotnet format PresetSender/PresetSender.slnx --verify-no-changes
./PresetSender/Publish-PresetSender.ps1
```

The initialization script downloads a pinned, checksum-verified Microsoft preview NuGet package into the ignored local package feed. Run it before the first Windows build on a fresh checkout; the publish script runs it automatically. The publish script creates a self-contained Windows x64 executable **plus the two Windows MIDI API sidecar files** in `PresetSender/artifacts/windows/`. Keep the directory together. It requires no separately installed .NET runtime and does not change PresetMaestro's version or release output. This utility has its own solution, so its checks are separate from the main application's solution.

Normal tests simulate MIDI and do not contact hardware or create system endpoints. The native check covers two simultaneous legacy MIDI receivers, channel 6 individually, all sixteen wire channels, and removal of both loopback endpoints. To run it explicitly:

```powershell
$env:PRESET_SENDER_NATIVE_MIDI_TEST = '1'
dotnet test PresetSender/PresetSender.slnx -c Release --filter FullyQualifiedName~VirtualMidiIntegrationTests
Remove-Item Env:PRESET_SENDER_NATIVE_MIDI_TEST
```

The project also selects the existing macOS/CoreMIDI backend when built on macOS (or with `-p:MaestroPlatform=macOS`). Native Mac launch and MIDI acceptance remain unverified; this task's runnable package is Windows only.

UX decisions and verification are recorded in [ux.md](ux.md).

## Logo and icon

The chosen **DIN Send** logo appears beside the app title and in its window/executable icon. Website-ready transparent PNGs and a favicon are in [Branding/Website](Branding/Website/README.md). The executable's multi-resolution icon is `Assets/preset-sender.ico`; the original and five alternate concepts are retained under `Branding/Options`. Re-export sizes on Windows with `Branding/Export-BrandAssets.ps1` before building if the source artwork changes.
