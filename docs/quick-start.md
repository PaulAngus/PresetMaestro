# PresetMaestro quick-start

**Version 1.0.4 · FM9 over USB · Draft for review**

PresetMaestro keeps a list of the presets and scenes you use on your Fractal device. You can give each entry a name that tells you when to use it, such as **Clean intro**, and double-click it to load that preset and scene again. This saves you looking up their numbers each time you practise or play a song. Your FM9 holds the sounds and produces the audio; PresetMaestro sends it the instruction to load the sound you chose.

A **preset** is a saved amp-and-effects setup. A **scene** is one of eight variations within that preset. A **favourite** remembers the preset and scene together under a name you choose.

## 1. Get ready

You need a Windows 10 version 2004 or later, 64-bit PC; PresetMaestro.exe; an FM9; a USB data cable; and your usual guitar and headphones or speaker connections. This guide uses the FM9 USB connection checked with firmware 12.00. FM3 and Axe-Fx III have different support.

1. Install Fractal Audio’s **Windows USB Driver** for the FM9 if it is not already installed. Follow the instructions on the [official FM9 downloads page](https://www.fractalaudio.com/fm9-downloads/).
2. Connect the FM9 to the computer by USB and switch it on. Close FM9-Edit and other applications using its MIDI connection. **MIDI** means control messages here: it tells the FM9 what to select. Keep your normal listening connections.
3. For this simple setup, check the FM9’s **SETUP → MIDI/Remote → General** settings: **MIDI Channel: 1**, **Program Change: ON**, **MIDI PC Offset: 0**, **PC Mapping: OFF**. Under **Other**, set **Scene Select** to **34**. This number tells the FM9 which control message selects a scene. See the [FM9 Owner’s Manual](https://www.fractalaudio.com/downloads/manuals/FM9/FM9-Owners-Manual.pdf), “MIDI/Remote” and “Selecting Scenes & Channels Remotely”.

**You should have:** an FM9 ready to receive preset and scene selections. Save any sound edits you want to keep on the FM9 before trying other presets.

## 2. Connect PresetMaestro

1. Open **PresetMaestro.exe**. On **Config**, under **MIDI Connection**, choose **FM9 MIDI In** for **MIDI In** and **FM9 MIDI Out** for **MIDI Out**. Leave the adjacent **Omni** setting as it is and leave **Thru In(s)** unchecked.
2. Click **Connect**.

**You should see:** **Sync with connected device**, identifying **FM9**, its device name if available, and its firmware. Merely seeing ports in the lists does not confirm a connection. [View the connection controls](images/quick-start/connection.png).

## 3. Complete first-time setup

A **library** is PresetMaestro’s locally saved record of the device’s presets, scenes and amps. A **profile** keeps your favourites and chooses which library they use. Keep the **Default** profile for this guide.

1. If the dialog says **Full library sync required**, click **Sync library** and wait. This build requires that first read; allow several minutes. It reads saved sounds into the computer without saving or rewriting presets on the FM9.
2. Wait for **Preset Index** to open automatically, showing the library’s presets. If **Default** is already in use and a library choice appears, choose **Create new library & sync** to keep the existing library.

**You should see:** preset names in **Preset Index** after setup completes. [View the first-time sync prompt](images/quick-start/first-sync.png).

On a later connection, an existing complete library allows **Skip for now**. You do not need a new full scan every time.

## 4. Load a preset and scene you want to use again

1. On your FM9, load a preset you already use and select the scene you want to come back to. For example, choose the preset and scene you use for a song’s clean introduction.
2. Play your guitar to check that this is the sound you want. Note the preset and scene numbers on the FM9’s display.

**You should see:** the chosen preset and scene on the FM9. Leave them selected while you add the favourite below.

## 5. Add that preset and scene to your favourites

1. In PresetMaestro, open **Favorites** and click **+ New**. The **New Favorite** editor opens on the right.
2. Click **Use Current**. PresetMaestro reads the preset and scene currently selected on the FM9 and fills in **Preset** and **Scene**. Check that the numbers agree with the FM9’s display.
3. In **Name**, type **Clean intro**, or another name that reminds you when to use this sound. Leave **Tags** empty for now.
4. Click **Save**. The editor closes and the new entry appears in the **Favorites** table.

**You have now saved:** a named shortcut to that preset and scene on this computer, in the **Default** profile. **Save** does not rename the FM9 preset or store sound edits on the device. [View an example favourite before saving](images/quick-start/favourite-draft.png). Your preset and scene names will differ.

## 6. Return to the preset and scene using your favourite

1. Choose a different preset or scene on the FM9 so you can check that the shortcut works.
2. In PresetMaestro’s **Favorites** table, double-click **Clean intro**.
3. Check the FM9’s display: it should have returned to the preset and scene you saved in that favourite. Play your guitar to confirm the sound.

**You should see:** **SENT** in PresetMaestro and your saved preset and scene selected on the FM9. **SENT** confirms that the app sent the command; the FM9’s display confirms the selection. [View the recall confirmation](images/quick-start/favourite-recalled.png).

A single click selects a favourite’s row. Double-clicking loads its preset and scene on the FM9.

## If something does not work

- **No FM9 ports:** check power, USB cable and driver, then click **Refresh Devices** on **Config**.
- **Connection error:** select both FM9 ports, close FM9-Edit or other MIDI applications, then try **Connect** again.
- **Setup stops:** correct the connection and choose **Resume** if offered. Closing incomplete setup disconnects; it does not finish the library.
- **Connected, but the wrong sound loads:** open **Config → Manage library**, select the assigned library and check **MIDI channel: 1**, **Display offset: 0 (device mapping disabled)** and **Scene CC#: 34**. Click **Save changes**. These must agree with the FM9 settings in step 1. If only the scene is wrong, recheck **Scene Select** on the FM9.
- **The display changes but there is no sound:** check your usual guitar, headphones/speaker connections and output level on the FM9.
