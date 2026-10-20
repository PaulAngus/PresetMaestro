# Preset Maestro

Quick guide • Draft for feedback • 20 October 2026

Preset Maestro gives you quicker access to your Fractal sounds and more ways to organise and explore them. Jump to a preset, recall favourite scenes, add tags and find where you use an amp model. This guide follows version 1.2.29; see the [full manual](https://github.com/PaulAngus/PresetMaestro/blob/main/docs/user-manual.md) for device support and detailed workflows.

## 1. Connect your Fractal

Use the connection appropriate to your model. FM9 and Axe-Fx III can use USB MIDI. For standard MIDI, including FM3, use a MIDI interface: interface **Out** to Fractal **In**, and Fractal **Out** to interface **In**. Both directions are needed for selections and readback.

Open **Config → MIDI Connection**, choose the matching **MIDI In** and **MIDI Out**, then click **Connect**. Follow the first-time **Sync device** prompts to read saved presets, scenes and amp information. This can take several minutes. Keep the **Default** profile while learning the app. Available reads and catalogues depend on the model; FM9 has the most complete support.

Under **Config → Device for this profile → Manage devices**, check that **MIDI channel**, **Display offset** and **Scene CC#** match the Fractal. Save any sound edits you want to keep before trying other presets.

## 2. Jump straight to a preset

Open **Preset Sender**, type the preset number, then press **Enter** or click **SEND**. For example, entering **287** takes you straight to preset 287. Use **Backspace** to correct a digit or **CLR** to clear the number. Keep the app in the foreground.

To send without pressing Enter, enable **Auto-send full numbers** under **Config → Entry Options**. Enter a short number such as 7 as **007** on a 512-slot device, or **0007** on a 1,024-slot device. The app waits for all three or four digits before sending. On **Favorites**, auto-send always uses three digits for the favourite slot number.

**SENT** confirms the command was sent. Check the Fractal's display and play to confirm the result.

<!-- pagebreak -->
## 3. Keep your favourite scenes ready to play

Open **Favorites** and click **New**. Choose the **Preset** and **Scene**, give the favourite a useful name, and click **Save**. On a connected FM9, **Use Current** fills in its current preset and scene; check the numbers before saving.

To recall a favourite, double-click its row. You can also type its **Slot** number on **Favorites** and press **Enter**, or enter that slot number through your configured MIDI controller. Favourite 7 might recall preset 287, scene 3. A single click inspects a row without loading it.

## 4. Organise your sounds with tags

Tags describe qualities you choose after playing: *single coils*, *humbuckers*, *warm*, *tight bass*, *slapback echo* or *wide stereo*. Give a sound as many useful tags as you need.

In a favourite's editor, type a tag in **Tags**, press **Enter**, then **Save**. You can also tag presets and scenes without making favourites. Within a profile, a scene's tags are shared between the scene browser and favourites pointing to it.

Use the search box to choose tags or search by name. **All tags** requires all selected tags; **Any tag** accepts at least one. Ordinary text searches still all have to match. Search, sort and browsing do not load sounds or change favourite slot numbers.

## 5. Browse presets and scenes

Open **Presets and scenes**. Choose **Presets** to inspect saved presets, their scenes and amp information, or **Scenes** to browse scenes across the collection. Double-click a result, or select it and use **SEND** or **Go to**. A scene action loads its preset first when needed.

To add tags, open a preset's details and select a scene, then choose **Add tag / Edit tags**. With no scene selected, that action edits preset tags. Choose **Save tags**. Close the pane with **× Close** or **Escape**.

<!-- pagebreak -->
## 6. Find amps by names you recognise

Open **Amps** to find the real amp behind a Fractal model name. Search by manufacturer, real amp name or Fractal name. Select an amp family, tick its variations under **Use in search**, and click **Show presets for…**. Open a matching preset and choose a scene to try.

Choose **Used in my presets** to revisit your collection, or **Not used in my presets** for ideas. The unused list needs a complete device scan and matching amp catalogue. It describes whole amp families, and includes every saved Amp channel when deciding whether a model is used. The browser does not insert amps into presets.

## 7. Use a MIDI pad or foot controller

Configure the controller to send MIDI notes for digits and commands such as **SEND**. In **Config → MIDI Connection**, select its input under **Thru In(s)**. Under **Entry Options**, enable **MIDI note entry**, then open **MIDI note mapping → Configure…** to match note numbers to actions.

Use it to enter preset numbers on **Preset Sender**, or favourite slot numbers on **Favorites**. Select only one input route per controller; duplicate routes can send each press twice. Match the per-input channel filter to the controller. Other messages can pass through using their original MIDI channel and assignments.

## 8. Keep different projects and devices organised

Use **Config → Profiles → Manage Profiles** to create, copy or switch profiles. Each keeps its own favourites and tags. Several profiles can share the same device while using different organisation.

Under **Config → Device for this profile → Manage devices**, give each physical unit a recognisable name and assign the intended device to each profile. Check the actual MIDI connection when changing units.

After saving changes on the Fractal, use **Resync Presets** to refresh the collection or **Resync Scenes** for one preset. FM9 also checks observed saved changes automatically; changes made offline or missed between polls can still need a manual refresh. Browse saved information while disconnected, and connect when you want to load sounds.
