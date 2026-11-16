# Firmware format

This document describes the parts of the iRiver H10 20GB firmware container that H10 Firmware
Manager reads, verifies and writes. It is enough to understand the app's reports and to validate an
image independently. It is not a full disassembly.

---

## The mi4 container

`H10_20GC.mi4` is a **PortalPlayer mi4** image: a 512-byte header followed by a payload. The
player bootloader reads the same file, so replacing it is all that is needed to change firmware.

All integers are **little-endian**.

| Offset | Size | Field | Value for iRiver 1.03.01 |
| --- | --- | --- | --- |
| `0x000` | 4 | Magic (`PPOS`) | `50 50 4F 53` |
| `0x004` | 4 | Version | `0x00010301` → **1.03.01** |
| `0x008` | 4 | Data length | `4,696,036` |
| `0x00C` | 4 | Stored CRC-32 | `0x45E73CB6` |
| `0x010` | 4 | Crypto type | `2` = TEA (0 = none) |
| `0x014` | 4 | Total length | `4,697,088` |
| `0x018` | 4 | Plaintext length | `512` |
| `0x01C`–`0x1FF` | — | Reserved / loader metadata | — |
| `0x200` | … | Encrypted payload | — |

Notes:

- The **version** is encoded as `major << 16 | minor << 8 | patch`, printed as `1.03.01`.
- **Plaintext length** is the number of unencrypted bytes that follow the header before the
  encrypted payload begins (the app computes `0x200 + plaintextLength` as the payload start).
- The file size is **4,697,088 bytes**; `Total length` matches it.
- `Magic offset` reported by the analyser is where a `PPOS` signature was found. It is `0` for a
  normal firmware and non-zero when an image is wrapped or has a prefix.

## CRC-32

The checksum is a standard reflected CRC-32 with the following parameters:

| Parameter | Value |
| --- | --- |
| Polynomial | `0xEDB88320` (reversed `0x04C11DB7`) |
| Initial value | `0x00000000` |
| Final XOR | none |
| Input reflected | yes |
| Range | `0x200` … end of file |

This differs from the common ZIP/PNG CRC-32 only in the initial value and by having no final XOR.
Known-answer test used by the self-test: the ASCII string `123456789` gives `0x2DFD2D88`.

The app recomputes this value whenever it loads a full image and compares it with `Stored CRC-32`.
A mismatch is reported as `Firmware CRC invalid`. A header-only scan does not compute the CRC and
therefore reports `CRC not checked` rather than a false failure.

## TEA encryption

The payload uses the **8-round TEA variant** implemented by `mi4code` and Rockbox's `mi4-loader`,
not textbook TEA:

| Parameter | Value |
| --- | --- |
| Rounds | 8 |
| Block size | 8 bytes (two 32-bit words) |
| Sum initial value (`Y_INIT`) | `0xF1BBCDC8` |
| Sum step (`Y_STEP`) | `0x61C88647` |
| Key update | a 32-bit block index is added to the 128-bit key every 8-byte block |
| Key order | four 32-bit words, little-endian |

The key for the H10 20GB English firmware is named **`20gc_eng`**:

```
0x0240772C  0x6F3329B5  0x3EC9A6C5  0xB0C9E493
```

The analyser tries every known key and reports the one that produces a valid mi4 payload. The
`20gc_fre` key is present in the table for the French build. Other known keys (`sansa`, `c200`, …)
belong to different PortalPlayer devices and are only used to identify unrelated images.

Encryption is **not** required to install firmware: the app copies the container byte-for-byte and
only decrypts on demand for analysis. This means an install can never re-encrypt an image
incorrectly.

## Rockbox detection

The Rockbox bootloader for the H10 is also an mi4 container, but it carries a signature:

| Offset | Bytes | Meaning |
| --- | --- | --- |
| `0x1F8` | `52 42 42 4C` (`RBBL`) | Rockbox bootloader |

The app checks this first. If present, the "firmware" is treated as Rockbox, not as an iRiver
image, and the Dashboard reports `Rockbox installed`.

The Rockbox installer also:

- saves the original firmware as `\System\OF.mi4`;
- creates a `\.rockbox` folder at the root.

The **Rockbox** page reports all three signals and can restore the original firmware.

## Firmware package (ZIP)

The bundled package `H10-20GC_original_1.03.01.zip` is a plain ZIP with forward-slash entry names
and this layout:

```
manifest.json
README.txt
System/H10_20GC.mi4
System/Font/cjk12.fnt
recovery/burnkey/H10_20GC_image.mi4
recovery/burnkey/H10_20GC.mi4
recovery/burnkey/BL_H10_20GC.hex
recovery/burnkey/firmware.inf
```

### `manifest.json`

| Field | Meaning |
| --- | --- |
| `schema` | Manifest schema version (currently `1`). |
| `packageId` | Stable identifier, `iriver-h10-20gc-original-1.03.01`. |
| `packageName` | Human-readable name shown in the app. |
| `createdAtUtc` | When the package was generated. |
| `target` | Player description. |
| `firmwareVersion` | `1.03.01`. |
| `source` | Where the files came from. |
| `defaultInstall` | Entry ids installed by default (`os-player`, `font-cjk`). |
| `entries[]` | One object per file (see below). |

Each entry has:

| Field | Meaning |
| --- | --- |
| `id` | Unique entry id. |
| `role` | `os`, `font`, `recovery-os`, `recovery-burnkey`, `recovery-bootloader` or `document`. |
| `name`, `description` | Text shown in the app. |
| `archivePath` | Path inside the ZIP (forward slashes). |
| `devicePath` | Destination on the player, or empty for documents. |
| `size` | Exact byte size. |
| `sha256`, `md5` | Hashes of the file. |
| `order` | Display / install order. |
| `mi4` | For mi4 files: version, lengths, crypto, CRC and Rockbox flag. |

The app validates every `archivePath` against its size and SHA-256 before installing anything.

### Regenerating the package

```powershell
.\tools\New-FirmwarePackage.ps1 -PlayerRoot F:\ -OfficialPackage _research\h1020gc
```

The script reads `\System\H10_20GC.mi4` and `\System\Font\cjk12.fnt` from a connected player and
combines them with the official iRiver update assets, then writes the ZIP with an English manifest
and README. It prints the per-file sizes, the main firmware CRC and the package SHA-256.

## Verifying an image by hand

```powershell
# CRC-32 over 0x200..EOF, then compare with the value at 0x0C
$b = [IO.File]::ReadAllBytes('H10_20GC.mi4')
[uint64]$c = 0; [uint64]$poly = 3988292384
for ($i = 0x200; $i -lt $b.Length; $i++) {
    $c = $c -bxor [uint64]$b[$i]
    for ($k = 0; $k -lt 8; $k++) {
        if (($c -band 1) -ne 0) { $c = (($c -shr 1) -bxor $poly) -band 0xFFFFFFFF }
        else { $c = ($c -shr 1) -band 0xFFFFFFFF }
    }
}
'computed: 0x{0:X8}' -f [uint32]$c
'stored  : 0x{0:X8}' -f [BitConverter]::ToUInt32($b, 0x0C)
```
