# Rockbox

## How Rockbox changes the player

The Rockbox installer for the H10 20GB does not flash a chip. It rearranges files on the FAT32
volume, because the H10 bootloader simply loads `\System\H10_20GC.mi4`:

1. It **renames the original firmware** from `\System\H10_20GC.mi4` to `\System\OF.mi4`.
2. It **writes a small Rockbox bootloader** to `\System\H10_20GC.mi4` (a mi4 file carrying the
   signature `RBBL` at offset `0x1F8`).
3. It **adds a `.rockbox` folder** at the root with the Rockbox code, themes, fonts and settings.

At boot, the Rockbox bootloader starts, then starts Rockbox from `.rockbox` or lets you launch the
original firmware from `OF.mi4` (dual boot).

| File | Meaning |
| --- | --- |
| `\System\H10_20GC.mi4` with `RBBL` at `0x1F8` | Rockbox bootloader |
| `\System\H10_20GC.mi4` with `PPOS` at offset 0 | Original iRiver firmware |
| `\System\OF.mi4` | Original firmware saved by the Rockbox installer |
| `\.rockbox\` | Rockbox data folder |

## What the app shows

Open the **Rockbox** page. The status card reports one of:

| Status | Meaning |
| --- | --- |
| `Rockbox is not installed.` | The active firmware is the original one (or missing). |
| `Rockbox bootloader detected.` | `H10_20GC.mi4` carries the `RBBL` signature. |
| `Rockbox data found.` | `.rockbox` and/or `OF.mi4` are present without an active Rockbox bootloader. |

The page also lists:

- **Bootloader** — original firmware or Rockbox bootloader;
- **Saved original** — whether `\System\OF.mi4` exists and is a valid mi4;
- **Rockbox data** — whether `.rockbox` exists, its file count and size.

## Replacing Rockbox with the original firmware

1. Connect the player in Emergency Connect mode and select it on the Dashboard.
2. Open the **Rockbox** page.
3. Choose what to remove after installing:
   - **Delete the .rockbox folder after installing** — frees space, removes all Rockbox data
     (settings, themes, plugins);
   - **Delete System\OF.mi4 (saved original firmware)** — removes the copy the Rockbox installer
     saved. Keep it if you might return to Rockbox.
4. Click **Replace Rockbox with original firmware** and confirm.

The app then:

1. creates a full safety backup (including `.rockbox` when present);
2. writes the original 1.03.01 image to `\System\H10_20GC.mi4` (temporary file, SHA-256 and
   CRC-32 verification, atomic replace);
3. keeps the Rockbox bootloader as `H10_20GC.mi4.previous`;
4. optionally deletes `.rockbox` and `OF.mi4`.

Safely eject the player and power it on. It boots the original iRiver firmware again.

> **Do not** delete `\System\H10_20GC.mi4` by hand and expect the player to boot. Without a valid
> `H10_20GC.mi4` the bootloader has nothing to load. If that happens, use
> [recovery.md](recovery.md).

## Removing Rockbox data only

The **Repair & format** page has **Remove Rockbox leftovers only**. It deletes `.rockbox` and
`\System\OF.mi4` but leaves the active firmware untouched. Use it when the player already runs the
original firmware and you only want to reclaim the space.

## Going back to Rockbox

This tool does not install Rockbox. If you removed `.rockbox` but kept `OF.mi4`, reinstall Rockbox
with the official Rockbox installer (it will detect `OF.mi4`). If you deleted everything, use the
Rockbox installer's full installation, which works from the original firmware state.

Rockbox is a separate open-source project. Nothing from Rockbox is bundled or redistributed here.
See <https://www.rockbox.org/> for its documentation and licence.
