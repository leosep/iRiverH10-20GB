# Recovery

This page covers a player that does not boot, does not show up in Windows, or has lost its
firmware. Try the steps in order.

---

## 1. Reset and charge first

Most "dead" H10s are simply frozen or out of power.

1. Charge the player for at least one hour from a wall charger or a powered USB port.
2. Press the **RESET** hole on the bottom with a paperclip for about two seconds.
3. Power it on.

If it still does nothing, continue.

## 2. The player does not appear in Windows

The player must be in **Emergency Connect** mode, and Windows must see its disk.

1. With the player off, connect USB.
2. Hold the **O** button and press **Power**, and keep **O** held until `Emergency Connect...`
   appears.
3. Open **This PC** and look for a drive labelled **H10**.
4. If there is no drive, try another USB cable and a rear USB port without a hub.
5. If Windows shows *USB device not recognized*, unplug, reset with a paperclip, and repeat.

If Windows sees the device but shows no drive letter:

- Open **Disk Management** (`diskmgmt.msc`).
- The player's disk should be present as a removable disk. If it has no drive letter, assign one.
- If it shows **RAW** or **unallocated**, the file system is damaged: format it (see step 4) and
  install the firmware.

## 3. The player shows up but does not boot

Symptoms: it hangs on the logo, shows a white or blank screen, reboots repeatedly, or starts
Rockbox instead of the original firmware.

1. Connect the player in Emergency Connect mode.
2. Start **H10 Firmware Manager** and check the Dashboard status:
   - `Rockbox installed` → use the **Rockbox** page to restore the original firmware.
   - `Firmware CRC invalid` or `Firmware missing` → open **Install firmware** and reinstall.
   - `System folder missing` → install the firmware; the app recreates `\System`.
3. If the app reports file-system problems, run **Repair & format → Repair firmware and folders**
   first (non-destructive), then reinstall.
4. Safely eject and power on.

## 4. Format the volume and start from scratch

Use this when the file system is damaged, the drive is a new CompactFlash card, or Windows reports
the volume as RAW.

> Formatting erases **everything** on the player, including music and any backup stored on it.
> Keep your backups on the PC.

1. Connect the player in Emergency Connect mode.
2. Open **Repair & format**.
3. In *Format as FAT32*, keep:
   - **Volume label**: `H10`;
   - **Cluster size**: `16,384 bytes (16K, factory)`;
   - **Quick format**: enabled is fine for a healthy drive; disable it to scan for bad blocks.
4. Click **Format player (erases all data)** and confirm twice.
5. Approve the Windows UAC prompt.
6. After formatting, open **Install firmware** and install the original firmware.

A freshly formatted card or drive has no `\System` folder. That is expected: the installer creates
it. See [cf-mod.md](cf-mod.md) for the CompactFlash procedure.

## 5. Windows cannot format the drive

- Make sure the player is not write-protected. The H10 has no write-protect switch, but a faulty
  cable or hub can cause write errors. Try a direct rear USB port.
- Close Explorer windows, antivirus scans and media players that may hold the volume.
- If `format.com` fails with *The volume is in use*, eject and reconnect, then retry.
- If the drive reports hardware errors, the 1.8" hard drive may be failing. A CompactFlash
  replacement is documented in [cf-mod.md](cf-mod.md).

## 6. The bootloader itself is broken

If the player does not enter Emergency Connect at all, does not show up as a disk on any PC, or
shows a bootloader error, the bootloader (not the firmware file) is likely damaged.

The firmware package includes the original bootloader assets under `recovery/burnkey/`:

| File | Contents |
| --- | --- |
| `H10_20GC_image.mi4` | Official 1.03.01 system image (same firmware, from iRiver's package). |
| `H10_20GC.mi4` | *BURN KEY* loader from iRiver's official update package. |
| `BL_H10_20GC.hex` | Encrypted bootloader image. |
| `firmware.inf` | Original manifest describing the official install paths. |

> **Warning:** these files are provided **for reference and preservation only**. Recovering the
> bootloader requires the original iRiver USB update tool and a correct burn procedure. Doing it
> wrong can make the player permanently unusable, and this application deliberately does not
> perform it.

If the bootloader is intact but the firmware is not, the player still enters Emergency Connect and
you can reinstall the firmware as described above. That is the case this tool is designed for.

## 7. Nothing works

- Try another PC, preferably with Windows 10, to rule out driver or port problems.
- Substitute the USB cable.
- Check that the internal drive is still connected. On a player that was opened for a
  CompactFlash mod, a loose ZIF cable is a common cause of "no drive".
- If the drive is detected but cannot hold a format, replace it (see [cf-mod.md](cf-mod.md)).

## Data safety

Before any repair, the app offers to create a backup. Backups are ZIP files with a per-file
SHA-256 manifest and can be verified and restored from the **Backups** page. Keep a copy of your
backups somewhere other than the player.
