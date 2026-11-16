#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the H10 Firmware Manager release: self-contained executable, MSI and hashes.

.DESCRIPTION
    Produces, under artifacts\release:
      H10FirmwareManager-<version>-x64.msi    installer with the firmware ZIP embedded
      H10-20GC_original_1.03.01.zip           the same firmware package, for direct download
      SHA256SUMS.txt                          checksums of both files

    The MSI embeds:
      H10FirmwareManager.exe                  self-contained .NET 9 WPF application
      firmware\H10-20GC_original_1.03.01.zip  original iRiver 1.03.01 firmware package

.PARAMETER Version
    Version written to the MSI. Defaults to the Version in Directory.Build.props.

.EXAMPLE
    .\tools\Build-Release.ps1

.EXAMPLE
    .\tools\Build-Release.ps1 -Version 1.0.1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [switch]$SkipPublish,
    [switch]$SkipTests
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
$envFile = Join-Path $root 'Directory.Build.props'
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host ('== ' + $Message + ' ' + ('=' * [Math]::Max(0, 70 - $Message.Length)))
    Write-Host ''
}

if (-not $Version) {
    [xml]$props = Get-Content -LiteralPath $envFile
    $Version = $props.Project.PropertyGroup.Version
    if (-not $Version) { throw 'Could not read Version from Directory.Build.props' }
}
$msiVersion = if ($Version -match '^\d+\.\d+\.\d+\.\d+$') { $Version } else { "$Version.0" }

$publishDir = Join-Path $root 'artifacts\publish'
$releaseDir = Join-Path $root 'artifacts\release'
$exe = Join-Path $publishDir 'H10FirmwareManager.exe'
$wxs = Join-Path $root 'installer\H10FirmwareManager.wxs'
$msi = Join-Path $releaseDir "H10FirmwareManager-$Version-x64.msi"
$zip = Join-Path $root 'firmware\H10-20GC_original_1.03.01.zip'

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Write-Step '1/4 Self-test'
if ($SkipTests) {
    Write-Host 'Skipped (-SkipTests).'
} else {
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    & dotnet run --project (Join-Path $root 'tests\H10FirmwareManager.SelfTest') -c Release -- selftest
    if ($LASTEXITCODE -ne 0) { throw "Self-test failed with exit code $LASTEXITCODE." }
}

Write-Step '2/4 Publish self-contained executable'
if ($SkipPublish -and (Test-Path -LiteralPath $exe)) {
    Write-Host "Using existing publish output: $exe"
} else {
    if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
    $env:DOTNET_CLI_UI_LANGUAGE = 'en'
    & dotnet publish (Join-Path $root 'src\H10FirmwareManager\H10FirmwareManager.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:AssemblyVersion="$msiVersion" -p:FileVersion="$msiVersion" `
        -o $publishDir -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
    Get-ChildItem -LiteralPath $publishDir -Filter '*.pdb' -Recurse | Remove-Item -Force
}

Write-Step '3/4 Build MSI (firmware ZIP embedded)'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe. Run without -SkipPublish." }
if (-not (Test-Path -LiteralPath $zip)) { throw "Missing firmware package: $zip" }
$wixVersion = (wix --version) -replace '\+.*$', ''
& wix build $wxs -arch x64 -d Version="$msiVersion" -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE." }

Write-Step '4/4 Collect release assets and hashes'
Copy-Item -LiteralPath $zip -Destination (Join-Path $releaseDir (Split-Path -Leaf $zip)) -Force

$files = @($msi, (Join-Path $releaseDir (Split-Path -Leaf $zip)))
$lines = foreach ($f in $files) {
    $hash = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLowerInvariant()
    $name = Split-Path -Leaf $f
    Write-Host ("{0}  {1,12:N0} bytes  {2}" -f $hash, (Get-Item $f).Length, $name)
    "$hash  $name"
}
$lines | Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii

Write-Host ''
Write-Host "Version : $Version"
Write-Host "WiX     : $wixVersion"
Write-Host "Output  : $releaseDir"
Write-Host ''
