# Installing the original firmware

This guide installs the original iRiver 1.03.01 firmware on an H10 20GB (H10 20GC) using
**H10 Firmware Manager**. It works whether the player has the original firmware, Rockbox, a
damaged system, or a freshly formatted drive.

---

## 1. Put the player in Emergency Connect (UMS) mode

The app needs the player's disk exposed as a normal USB mass-storage drive. This is the same
mode the original iRiver updater used, without needing iRiver's drivers.

1. Make sure the player is powered off (hold **Power** until it turns off).
2. Connect the USB cable to the PC.
3. Press and **hold the O button**, then press **Power** briefly — keep holding **O**.
4. Wait until the screen shows **`Emergency Connect...`** (or `UMS`).
5. Release the buttons.

Windows should mount a drive whose label is **H10**. It may also open an Explorer window; you can
close it.

> If Windows offers to format the drive, **cancel**. Formatting is done from the app, on purpose.

**If the player does not enter Emergency Connect:**

- Reset it first with a paperclip in the small reset hole, then repeat the sequence.
- Try a different USB cable and a rear USB port (no hub).
- Make sure the battery has some charge; a dead battery cannot stay in USB mode.

## 2. Start the app and select the player

1. Open **H10 Firmware Manager** from the Start menu.
2. On the **Dashboard**, the player should appear as `F: · H10` (the letter may differ) with
   `FAT32`, the capacity and `Ready`.
3. If several drives are listed, pick the one labelled `H10` in the device selector.

The status card summarizes what the app found:

| Status | Meaning |
| --- | --- |
| `Ready` | System folder and a valid original firmware are present. |
| `Rockbox installed` | The bootloader is Rockbox. Use the **Rockbox** page to restore the original firmware. |
| `System folder missing` | The drive is empty or not a player volume. Install will create the folders. |
| `Firmware missing` | `\System` exists but `H10_20GC.mi4` is absent. |
| `Firmware CRC invalid` | The firmware file is damaged or truncated. Reinstall it. |

## 3. Install the firmware

1. Open **Install firmware**.
2. Confirm that the **Player** box shows the right drive and that the package section shows
   `iRiver H10 20GB - original firmware` with firmware version `1.03.01`.
   The bundled package is selected automatically; **Browse…** lets you pick a different ZIP.
3. Review the entries that will be written:
   - `System/H10_20GC.mi4` — the firmware (required);
   - `System/Font/cjk12.fnt` — the CJK font (recommended).
4. Leave the default options:
   - **Create a safety backup first** — writes a ZIP backup before changing anything;
   - **Verify SHA-256 and CRC-32 after writing** — reads the file back and checks it.
5. Click **Install original firmware 1.03.01** and confirm.

The progress panel walks through: *Preflight → Backup → Prepare → Install → Verify → Complete*.
The right-hand log shows each step. Do not unplug the player while this runs.

What the app does internally:

| Step | Detail |
| --- | --- |
| Validate package | Checks `manifest.json`, entry hashes and the mi4 CRC. |
| Backup | Copies `\System` (and `.rockbox` when present) to a verified ZIP. |
| Create folders | Creates `\System` and `\System\Font` if missing. |
| Write | Copies each entry to `<target>.tmp`, flushes it, verifies SHA-256 and CRC-32, then atomically replaces the destination. |
| Keep previous | With the option enabled, the old firmware is kept as `H10_20GC.mi4.previous`. |
| Verify | Re-reads the installed firmware and compares hashes with the package. |

## 4. Eject and boot the player

1. In Windows, **safely eject** the H10 drive (or use the tray icon).
2. Unplug USB.
3. Power the player on. The iRiver logo and the original 1.03.01 interface should appear.

If the player still boots the old system, reinstall once more and make sure the operation reached
**Complete** without errors. See [recovery.md](recovery.md) if it does not boot at all.

## Common problems

| Symptom | Cause | Fix |
| --- | --- | --- |
| Drive not listed | Player is in MTP mode or not in Emergency Connect | Repeat step 1; look for `Emergency Connect...` on the player. |
| `Access denied` while writing | A program holds the file open (Explorer preview, antivirus, media player) | Close Explorer windows for the drive and retry. |
| Package validation fails | The ZIP is corrupted or was modified | Reinstall the app, or point **Browse…** at a fresh copy. |
| `Firmware CRC invalid` after install | The write did not complete | Retry on another USB port; check the log. |
| Player does not boot after install | Bootloader problem, not the firmware file | See [recovery.md](recovery.md). |

## Notes

- Installing over a working player is safe: the previous firmware can be kept and a backup is
  written first.
- The font is optional. If the player displays squares instead of characters, install
  `System/Font/cjk12.fnt`.
- The app never touches your music files; the backup only covers `\System` and, optionally,
  `.rockbox`.
