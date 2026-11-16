using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;

namespace H10FirmwareManager.Core.Services;

public sealed class RockboxStatus
{
    public required H10Device Device { get; init; }
    public bool BootloaderPresent { get; init; }
    public bool OriginalFirmwareSaved { get; init; }
    public bool FolderPresent { get; init; }
    public bool OriginalFirmwareSavedIsValid { get; init; }
    public string? SavedFirmwareVersion { get; init; }
    public long FolderBytes { get; init; }
    public int FolderFiles { get; init; }

    public bool Detected => BootloaderPresent || OriginalFirmwareSaved || FolderPresent;

    public string Summary
    {
        get
        {
            if (!Detected) return "Rockbox is not installed.";
            if (BootloaderPresent) return "Rockbox bootloader is active (RBBL signature found).";
            if (OriginalFirmwareSaved) return "Rockbox files detected, but the original firmware is active.";
            return "Rockbox data folder detected; the active firmware is original.";
        }
    }

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (BootloaderPresent) parts.Add("System\\H10_20GC.mi4 is a Rockbox bootloader");
            if (OriginalFirmwareSaved) parts.Add("System\\OF.mi4 keeps a previous original firmware");
            if (FolderPresent) parts.Add($".rockbox folder ({FolderFiles} files, {Humanize.Bytes(FolderBytes)})");
            return parts.Count == 0 ? "No Rockbox files found." : string.Join(" · ", parts);
        }
    }
}

public sealed class RockboxService(InstallService installer)
{
    public RockboxStatus Detect(H10Device device)
    {
        bool bootloader = device.Firmware is { IsRockbox: true };
        FirmwareImage? saved = null;
        if (device.HasOfMi4)
        {
            try { saved = FirmwareImage.LoadHeaderOnly(device.OfMi4Path); }
            catch { }
        }

        long bytes = 0;
        int files = 0;
        if (device.HasRockboxFolder)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(device.RockboxPath, "*", SearchOption.AllDirectories))
                {
                    files++;
                    try { bytes += new FileInfo(file).Length; } catch { }
                }
            }
            catch
            {
                // Ignore unreadable folders; detection still works.
            }
        }

        return new RockboxStatus
        {
            Device = device,
            BootloaderPresent = bootloader,
            OriginalFirmwareSaved = saved is not null,
            OriginalFirmwareSavedIsValid = saved is { IsMi4: true, IsRockbox: false, CrcValid: true },
            SavedFirmwareVersion = saved?.Header?.VersionText,
            FolderPresent = device.HasRockboxFolder,
            FolderBytes = bytes,
            FolderFiles = files
        };
    }

    public Task<InstallResult> ReplaceWithOriginalAsync(
        H10Device device,
        FirmwarePackage package,
        bool deleteRockboxFolder,
        bool deleteOfMi4,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = new InstallOptions
        {
            CreateBackup = true,
            IncludeRockboxInBackup = true,
            KeepPreviousFirmware = true,
            VerifyAfterWrite = true,
            DeleteRockboxFolder = deleteRockboxFolder,
            DeleteOfMi4 = deleteOfMi4
        };
        return installer.InstallAsync(device, package, options, progress, cancellationToken);
    }
}
