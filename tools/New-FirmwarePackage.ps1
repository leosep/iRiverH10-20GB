#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the original firmware ZIP that is bundled with H10 Firmware Manager.

.DESCRIPTION
    Combines the firmware installed on a connected player (default F:) with the assets
    from the official iRiver update package (system image, burnkey loader and bootloader)
    into a reproducible package with a manifest and SHA-256 / MD5 hashes.

    The generated ZIP contains:
      System/H10_20GC.mi4              original firmware read from the player
      System/Font/cjk12.fnt            system font
      recovery/burnkey/*               official iRiver update package assets
      manifest.json                    machine readable description
      README.txt                       human readable instructions

.EXAMPLE
    .\New-FirmwarePackage.ps1 -PlayerRoot F:\ -OfficialPackage ..\_research\h1020gc

.EXAMPLE
    .\New-FirmwarePackage.ps1 -PlayerRoot F:\ -KeepStaging
#>
[CmdletBinding()]
param(
    [string]$PlayerRoot = 'F:\',
    [string]$OfficialPackage = '',
    [string]$Output = '',
    [string]$StagingRoot = '',
    [switch]$KeepStaging
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OfficialPackage) { $OfficialPackage = Join-Path $scriptDir '..\_research\h1020gc' }
if (-not $Output) { $Output = Join-Path $scriptDir '..\firmware\H10-20GC_original_1.03.01.zip' }
if (-not $StagingRoot) { $StagingRoot = Join-Path $scriptDir '..\artifacts\firmware-staging' }

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-Mi4Info {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 0x200 -or
        $bytes[0] -ne 0x50 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4F -or $bytes[3] -ne 0x53) {
        throw "Not a valid mi4 file (PPOS magic is missing): $Path"
    }

    $version = [BitConverter]::ToUInt32($bytes, 4)
    $dataLen = [BitConverter]::ToUInt32($bytes, 8)
    $crc     = [BitConverter]::ToUInt32($bytes, 0x0C)
    $crypto  = [BitConverter]::ToUInt32($bytes, 0x10)
    $total   = [BitConverter]::ToUInt32($bytes, 0x14)
    $plain   = [BitConverter]::ToUInt32($bytes, 0x18)

    # CRC-32 (polynomial 0xEDB88320, init 0, no final XOR) over 0x200..EOF.
    [uint64]$c = 0
    [uint64]$poly = 3988292384
    for ($i = 0x200; $i -lt $bytes.Length; $i++) {
        $c = $c -bxor [uint64]$bytes[$i]
        for ($k = 0; $k -lt 8; $k++) {
            if (($c -band 1) -ne 0) { $c = (($c -shr 1) -bxor $poly) -band 0xFFFFFFFF }
            else { $c = ($c -shr 1) -band 0xFFFFFFFF }
        }
    }

    $rbb = $bytes.Length -ge 0x1FC -and
           $bytes[0x1F8] -eq 0x52 -and $bytes[0x1F9] -eq 0x42 -and
           $bytes[0x1FA] -eq 0x42 -and $bytes[0x1FB] -eq 0x4C

    $major = ($version -shr 16) -band 0xFF
    $minor = ($version -shr 8) -band 0xFF
    $patch = $version -band 0xFF

    [ordered]@{
        versionField  = ('0x{0:X8}' -f $version)
        version       = ('{0}.{1:D2}.{2:D2}' -f $major, $minor, $patch)
        dataLength    = [int64]$dataLen
        totalLength   = [int64]$total
        plainLength   = [int64]$plain
        crypto        = [int64]$crypto
        cryptoName    = if ($crypto -eq 2) { 'TEA' } elseif ($crypto -eq 0) { 'none' } else { "unknown ($crypto)" }
        crc32         = ('0x{0:X8}' -f $crc)
        crc32Valid    = ([uint32]$c -eq $crc)
        crc32Computed = ('0x{0:X8}' -f [uint32]$c)
        rockbox       = $rbb
        size          = [int64]$bytes.Length
        sha256        = (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        md5           = (Get-FileHash $Path -Algorithm MD5).Hash.ToLowerInvariant()
    }
}

function New-Entry {
    param(
        [string]$Id, [string]$Role, [string]$Name, [string]$Description,
        [string]$ArchivePath, [string]$DevicePath, [string]$SourcePath,
        [int]$Order, [switch]$Mi4
    )

    $item = Get-Item -Force $SourcePath
    $entry = [ordered]@{
        id          = $Id
        role        = $Role
        name        = $Name
        description = $Description
        archivePath = $ArchivePath
        devicePath  = $DevicePath
        size        = [int64]$item.Length
        sha256      = (Get-FileHash $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
        md5         = (Get-FileHash $SourcePath -Algorithm MD5).Hash.ToLowerInvariant()
        order       = $Order
    }
    if ($Mi4) { $entry.mi4 = Get-Mi4Info -Path $SourcePath }
    [pscustomobject]$entry
}

$playerRoot = (Resolve-Path $PlayerRoot).Path.TrimEnd('\')
$official = (Resolve-Path $OfficialPackage).Path
$outFile = [IO.Path]::GetFullPath($Output)
$stage = [IO.Path]::GetFullPath($StagingRoot)

$fwSrc = Join-Path $playerRoot 'System\H10_20GC.mi4'
$fontSrc = Join-Path $playerRoot 'System\Font\cjk12.fnt'
$recoveryMi4 = Join-Path $official 'H10_20GC.mi4'
$recoveryImage = Join-Path $official 'H10_20GC_image.mi4'
$recoveryBl = Join-Path $official 'BL_H10_20GC.hex'
$recoveryInf = Join-Path $official 'firmware.inf'

foreach ($p in @($fwSrc, $fontSrc, $recoveryMi4, $recoveryImage, $recoveryBl, $recoveryInf)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Required file is missing: $p" }
}

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$dirs = @(
    (Join-Path $stage 'System\Font'),
    (Join-Path $stage 'recovery\burnkey')
)
foreach ($d in $dirs) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

Copy-Item -LiteralPath $fwSrc      -Destination (Join-Path $stage 'System\H10_20GC.mi4') -Force
Copy-Item -LiteralPath $fontSrc    -Destination (Join-Path $stage 'System\Font\cjk12.fnt') -Force
Copy-Item -LiteralPath $recoveryMi4   -Destination (Join-Path $stage 'recovery\burnkey\H10_20GC.mi4') -Force
Copy-Item -LiteralPath $recoveryImage -Destination (Join-Path $stage 'recovery\burnkey\H10_20GC_image.mi4') -Force
Copy-Item -LiteralPath $recoveryBl    -Destination (Join-Path $stage 'recovery\burnkey\BL_H10_20GC.hex') -Force
Copy-Item -LiteralPath $recoveryInf   -Destination (Join-Path $stage 'recovery\burnkey\firmware.inf') -Force

# Copy-Item preserves attributes; clear them so no entry is marked hidden in the ZIP.
Get-ChildItem -LiteralPath $stage -Recurse -File -Force | ForEach-Object {
    $_.Attributes = [IO.FileAttributes]::Normal
}

$entries = @(
    (New-Entry -Id 'os-player' -Role 'os' -Name 'Original firmware (read from the player)' `
        -Description 'Original iRiver 1.03.01 system image exactly as installed on the H10 20GB. This is the recommended entry for installing or restoring the player.' `
        -ArchivePath 'System/H10_20GC.mi4' -DevicePath 'System/H10_20GC.mi4' `
        -SourcePath $fwSrc -Order 1 -Mi4),

    (New-Entry -Id 'font-cjk' -Role 'font' -Name 'CJK system font (cjk12.fnt)' `
        -Description 'System font required to render text on the player. It is part of the complete system file set.' `
        -ArchivePath 'System/Font/cjk12.fnt' -DevicePath 'System/Font/cjk12.fnt' `
        -SourcePath $fontSrc -Order 2),

    (New-Entry -Id 'recovery-image' -Role 'recovery-os' -Name 'iRiver 1.03.01 official image' `
        -Description 'System image from the official iRiver update package (package version 2.51). Alternative to the firmware read from the player.' `
        -ArchivePath 'recovery/burnkey/H10_20GC_image.mi4' -DevicePath 'System/H10_20GC.mi4' `
        -SourcePath $recoveryImage -Order 3 -Mi4),

    (New-Entry -Id 'recovery-burnkey' -Role 'recovery-burnkey' -Name 'iRiver 2.51 burnkey loader' `
        -Description 'BURN KEY firmware from the official package. For advanced bootloader recovery only. Do not install it as normal firmware.' `
        -ArchivePath 'recovery/burnkey/H10_20GC.mi4' -DevicePath 'System/H10_20GC.mi4' `
        -SourcePath $recoveryMi4 -Order 4 -Mi4),

    (New-Entry -Id 'recovery-bootloader' -Role 'recovery-bootloader' -Name 'Encrypted bootloader (BL_H10_20GC.hex)' `
        -Description 'Original encrypted bootloader. For advanced recovery only (burnkey). Flashing it incorrectly can leave the player unusable.' `
        -ArchivePath 'recovery/burnkey/BL_H10_20GC.hex' -DevicePath 'BL_H10_20GC.hex' `
        -SourcePath $recoveryBl -Order 5),

    (New-Entry -Id 'official-manifest' -Role 'document' -Name 'Official firmware.inf' `
        -Description 'Manifest from the official iRiver update package, kept as a reference for file names and install paths.' `
        -ArchivePath 'recovery/burnkey/firmware.inf' -DevicePath '' `
        -SourcePath $recoveryInf -Order 6)
)

$readme = @'
iRiver H10 20GB (H10 20GC) - original firmware package
======================================================

Contents
--------
System/H10_20GC.mi4
    Original firmware read from a working H10 20GB. Version 1.03.01.
    This is the file the player bootloader loads at startup.

System/Font/cjk12.fnt
    System font. Installed as \System\Font\cjk12.fnt.

recovery/burnkey/
    Files from the official iRiver update package (package version 2.51):
      - H10_20GC_image.mi4 : official 1.03.01 system image
      - H10_20GC.mi4       : BURN KEY loader for bootloader recovery
      - BL_H10_20GC.hex    : encrypted bootloader
      - firmware.inf       : original manifest with install paths
    ADVANCED RECOVERY ONLY. Do not install these as normal firmware.

Recommended installation
------------------------
1. Connect the H10 in Emergency Connect (UMS) mode:
   - 20GB: press RESET with a paperclip, connect USB, then hold "O"
     while powering on until "Emergency Connect..." appears.
   - The player appears as a normal disk labelled H10.
2. Open H10 Firmware Manager, select the drive and click "Install firmware".
3. The app creates \System and \System\Font when they are missing,
   so it also works on a freshly formatted drive.
4. Safely eject the player and power it on.

If Rockbox is installed
-----------------------
Rockbox renames the original firmware to \System\OF.mi4 and installs its own
bootloader (signature "RBBL" at offset 0x1F8) as \System\H10_20GC.mi4.
The app detects this, creates a backup and can restore the original firmware,
optionally removing the .rockbox folder.

Legal notice
------------
The firmware is the property of iRiver/Reigncom. It is distributed only for
recovering and preserving H10 players. Use it at your own risk.
'@
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, $utf8NoBom)

$manifest = [ordered]@{
    schema          = 1
    packageId       = 'iriver-h10-20gc-original-1.03.01'
    packageName     = 'iRiver H10 20GB - original firmware'
    createdAtUtc    = (Get-Date).ToUniversalTime().ToString('o')
    target          = 'iriver H10 20GC (H10 20GB, PortalPlayer PP5020)'
    firmwareVersion = '1.03.01'
    source          = 'Firmware read from the player (F:\System) plus the official iRiver 2.51 package'
    defaultInstall  = @('os-player', 'font-cjk')
    entries         = $entries
}
$json = $manifest | ConvertTo-Json -Depth 10
[IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), $json, $utf8NoBom)

New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outFile)) | Out-Null
if (Test-Path -LiteralPath $outFile) { Remove-Item -LiteralPath $outFile -Force }

# Build the archive manually so entry names always use forward slashes (ZIP spec),
# independent of the Windows directory separator.
$zipStream = [IO.File]::Open($outFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        Get-ChildItem -LiteralPath $stage -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
            $entryName = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $_.LastWriteTime
            $inStream = [IO.File]::OpenRead($_.FullName)
            try {
                $outStream = $entry.Open()
                try { $inStream.CopyTo($outStream) }
                finally { $outStream.Dispose() }
            }
            finally { $inStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $zipStream.Dispose() }

if (-not $KeepStaging) { Remove-Item -LiteralPath $stage -Recurse -Force }

$zip = [IO.Compression.ZipFile]::OpenRead($outFile)
try {
    Write-Host ''
    Write-Host "Package: $outFile"
    Write-Host ("Size   : {0:N0} bytes" -f (Get-Item $outFile).Length)
    Write-Host ("SHA256 : {0}" -f (Get-FileHash $outFile -Algorithm SHA256).Hash.ToLowerInvariant())
    Write-Host ''
    $zip.Entries | Sort-Object FullName | ForEach-Object {
        Write-Host ("  {0,-45} {1,12:N0}" -f $_.FullName, $_.Length)
    }
    Write-Host ''
    Write-Host 'Main firmware:'
    $os = $entries | Where-Object id -eq 'os-player'
    Write-Host ("  version {0}  CRC {1} ({2})  {3:N0} bytes" -f $os.mi4.version, $os.mi4.crc32, $(if($os.mi4.crc32Valid){'valid'}else{'INVALID'}), $os.size)
    Write-Host ("  sha256  {0}" -f $os.sha256)
}
finally { $zip.Dispose() }
