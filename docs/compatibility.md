# Compatibility

## Supported player

| | |
| --- | --- |
| Product | iRiver H10 20GB |
| Model code | **H10 20GC** |
| Chipset | PortalPlayer PP5020 |
| Storage (original) | 20 GB 1.8" ZIF hard drive |
| USB identity | `VID_0B70` / `PID_00BA` — *iriver iriver H10 20GC USB Device* |
| Required mode | Emergency Connect (USB mass storage / UMS) |
| Volume label | `H10` |
| File system | FAT32 with 16,384-byte clusters |
| Firmware | `\System\H10_20GC.mi4`, version 1.03.01 |

The app identifies the player by the volume label, the presence of `\System\H10_20GC.mi4`, its
USB identity when available, and the volume geometry. It works with drives reformatted by the app
or by Windows, because it recreates the required folder structure.

## Operating system

| Requirement | Detail |
| --- | --- |
| Windows | Windows 10 (1809 or later) or Windows 11, **64-bit** |
| Runtime | None — the MSI ships a self-contained .NET 9 application |
| Drivers | None — Windows' built-in USB mass-storage driver is used |
| Permissions | The app runs as a normal user. Formatting triggers a UAC prompt |
| Disk space | About 90 MB for the install (77 MB MSI, 83 MB installed) |

## Not supported

| Device | Why |
| --- | --- |
| **iRiver H10 5GB (H10 5GC)** | Different capacity, different firmware file and layout. |
| **iRiver H10 20GB with a different model code** (for example H10 20GC variants sold in other regions with different bootloaders) | Firmware and bootloader differ; flashing the wrong image can brick the device. |
| iRiver H300, H100, E-series, T-series, U-series | Different hardware and firmware. |
| H10 in MTP mode | The app needs a drive letter; MTP does not provide one. |
| macOS / Linux | The app is a Windows WPF application. |
| Windows on ARM | Not tested; the MSI targets x64. |

## Verified against

The following values were read from a working H10 20GB and are used by the self-test:

| Item | Value |
| --- | --- |
| Firmware version | `1.03.01` (`0x00010301`) |
| Firmware size | 4,697,088 bytes |
| Firmware CRC-32 | `0x45E73CB6` |
| Firmware SHA-256 | `dd14bdf6bf0d7f692a4c04e48e5ecc227bc819a19d1d89e323bbd70796d18a36` |
| Volume | FAT32, label `H10`, 18.62 GB, 16,384-byte clusters |
| Font | `cjk12.fnt`, 3,103,341 bytes |

If your player reports a **different model code**, stop and check before installing anything.
