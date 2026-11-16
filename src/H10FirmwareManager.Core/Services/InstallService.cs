using System.Security.Cryptography;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;

namespace H10FirmwareManager.Core.Services;

public sealed class InstallOptions
{
    public IReadOnlyCollection<string>? EntryIds { get; set; }
    public bool CreateBackup { get; set; } = true;
    public bool IncludeRockboxInBackup { get; set; } = true;
    public bool KeepPreviousFirmware { get; set; } = true;
    public bool VerifyAfterWrite { get; set; } = true;
    public bool AllowUnconfirmedDevice { get; set; }
    public bool IncludeRecoveryEntries { get; set; }
    public bool DeleteRockboxFolder { get; set; }
    public bool DeleteOfMi4 { get; set; }
}

public sealed record InstallProgress(int Percent, string Stage, string Message, LogLevel Level = LogLevel.Info);

public sealed class InstalledFileInfo
{
    public required string EntryId { get; init; }
    public required string Path { get; init; }
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed class InstallResult
{
    public bool Success { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? Error { get; init; }
    public BackupInfo? Backup { get; init; }
    public List<InstalledFileInfo> Files { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public FirmwareImage? InstalledFirmware { get; init; }
    public long FreeBytesBefore { get; init; }
    public long FreeBytesAfter { get; init; }
}

/// <summary>
/// Installs original firmware onto an H10. The player is a plain FAT32 mass-storage
/// volume, so no drivers or iRiver software are used. Works on a freshly formatted
/// drive because System\ and System\Font\ are created as needed.
/// </summary>
public sealed class InstallService(LogService log, BackupService backups)
{
    public async Task<InstallResult> InstallAsync(
        H10Device device,
        FirmwarePackage package,
        InstallOptions? options = null,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new InstallOptions();
        var volume = device.Volume;
        var warnings = new List<string>();
        var installed = new List<InstalledFileInfo>();
        long freeBefore = volume.FreeBytes;

        try
        {
            progress?.Report(new InstallProgress(0, "Preflight", "Checking the player and package…"));
            if (!volume.IsReady)
            {
                throw new InvalidOperationException("The selected volume is not ready.");
            }
            if (!device.IsH10 && !options.AllowUnconfirmedDevice)
            {
                throw new InvalidOperationException(
                    "The selected drive is not confirmed as an iRiver H10 20GB. Enable the override to continue.");
            }
            if (!string.Equals(volume.FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The player is formatted as {volume.FileSystem}. The H10 bootloader requires FAT32.");
            }

            var entryIds = options.EntryIds?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? package.DefaultInstallIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selected = package.Entries
                .Where(e => entryIds.Contains(e.Id))
                .Where(e => options.IncludeRecoveryEntries || !e.IsRecovery)
                .Where(e => !e.IsDocument)
                .OrderBy(e => e.Order)
                .ToList();

            if (selected.All(e => !e.IsOperatingSystem))
            {
                throw new InvalidOperationException("The selected package entries do not include an operating system image.");
            }

            progress?.Report(new InstallProgress(3, "Preflight", "Verifying package integrity…"));
            var validation = await package.ValidateAsync(verifyHashes: true, progress: null, cancellationToken);
            if (!validation.IsValid)
            {
                throw new InvalidDataException("Firmware package validation failed: " + string.Join(" ", validation.Problems));
            }

            long required = selected.Sum(e => e.Size) + 2L * 1024 * 1024;
            if (volume.FreeBytes < required)
            {
                throw new InvalidOperationException(
                    $"Not enough free space. Need {Humanize.Bytes(required)}, available {Humanize.Bytes(volume.FreeBytes)}.");
            }

            TestWriteAccess(volume.RootPath);

            if (device.RockboxInstalled)
            {
                string detail = device.Firmware is { IsRockbox: true }
                    ? "A Rockbox bootloader is installed as H10_20GC.mi4 and will be replaced."
                    : "Rockbox data was detected and will be left alone unless selected for removal.";
                warnings.Add(detail);
                log.Warning(detail);
            }

            BackupInfo? backup = null;
            if (options.CreateBackup)
            {
                progress?.Report(new InstallProgress(5, "Backup", "Creating a safety backup…"));
                var backupProgress = new Progress<BackupProgress>(p =>
                {
                    int percent = 5 + (int)(p.Percent * 0.40);
                    progress?.Report(new InstallProgress(percent, "Backup", p.CurrentFile ?? p.Stage));
                });
                backup = await backups.CreateBackupAsync(
                    device,
                    new BackupOptions
                    {
                        IncludeRockboxFolder = options.IncludeRockboxInBackup,
                        Reason = "Automatic backup before firmware installation"
                    },
                    backupProgress,
                    cancellationToken);
            }

            progress?.Report(new InstallProgress(47, "Prepare", "Creating System folders…"));
            string systemDir = Path.Combine(volume.RootPath, "System");
            string fontDir = Path.Combine(systemDir, "Font");
            Directory.CreateDirectory(systemDir);
            Directory.CreateDirectory(fontDir);

            int stepIndex = 0;
            foreach (var entry in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stepIndex++;
                int basePercent = 50 + (int)(42.0 * (stepIndex - 1) / selected.Count);
                progress?.Report(new InstallProgress(basePercent, "Install", $"Installing {entry.Name}…"));
                var info = await InstallEntryAsync(device, package, entry, options, cancellationToken);
                installed.Add(info);
                log.Success($"Installed {entry.Name}.", info.Path);
            }

            progress?.Report(new InstallProgress(94, "Verify", "Verifying installed firmware…"));
            FirmwareImage? firmware = null;
            string firmwarePath = Path.Combine(volume.RootPath, "System", "H10_20GC.mi4");
            if (File.Exists(firmwarePath))
            {
                firmware = FirmwareImage.Load(firmwarePath);
                if (!firmware.IsMi4)
                {
                    throw new InvalidDataException("The installed firmware is not a valid mi4 image.");
                }
                if (firmware.IsRockbox)
                {
                    throw new InvalidDataException("The installed file is still a Rockbox bootloader.");
                }
                if (!firmware.CrcValid)
                {
                    throw new InvalidDataException("The installed firmware failed its CRC check.");
                }
                if (firmware.Header is { } h &&
                    !string.IsNullOrWhiteSpace(package.Manifest.FirmwareVersion) &&
                    h.VersionText != package.Manifest.FirmwareVersion)
                {
                    warnings.Add($"Installed version {h.VersionText} differs from package version {package.Manifest.FirmwareVersion}.");
                }
            }

            if (options.DeleteOfMi4)
            {
                string ofPath = Path.Combine(volume.RootPath, "System", "OF.mi4");
                if (File.Exists(ofPath))
                {
                    ClearReadOnly(ofPath);
                    File.Delete(ofPath);
                    log.Info("Deleted System\\OF.mi4.");
                }
            }

            if (options.DeleteRockboxFolder)
            {
                string rockbox = Path.Combine(volume.RootPath, ".rockbox");
                if (Directory.Exists(rockbox))
                {
                    DeleteDirectory(rockbox);
                    log.Info("Deleted the .rockbox folder.");
                }
            }

            VolumeService.TryFlushVolume(volume.RootPath, out string flushMessage);
            log.Debug(flushMessage);

            var refreshed = new VolumeService().GetVolumeInfo(volume.RootPath);
            progress?.Report(new InstallProgress(100, "Done", "Firmware installed successfully.", LogLevel.Success));
            log.Success($"Firmware installation finished on {volume.DriveLetter}.",
                $"Version {(firmware?.Header?.VersionText ?? "unknown")}, {installed.Count} files.");

            return new InstallResult
            {
                Success = true,
                Summary = $"Original firmware {(firmware?.Header?.VersionText ?? package.Manifest.FirmwareVersion)} installed on {volume.DriveLetter}.",
                Backup = backup,
                Files = installed,
                Warnings = warnings,
                InstalledFirmware = firmware,
                FreeBytesBefore = freeBefore,
                FreeBytesAfter = refreshed.FreeBytes
            };
        }
        catch (OperationCanceledException)
        {
            log.Warning("Installation canceled by the user.");
            throw;
        }
        catch (Exception ex)
        {
            log.Error("Firmware installation failed.", ex.ToString());
            return new InstallResult
            {
                Success = false,
                Summary = "Firmware installation failed.",
                Error = ex.Message,
                Backup = null,
                Files = installed,
                Warnings = warnings,
                FreeBytesBefore = freeBefore
            };
        }
    }

    private async Task<InstalledFileInfo> InstallEntryAsync(
        H10Device device,
        FirmwarePackage package,
        FirmwarePackageEntry entry,
        InstallOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.DevicePath))
        {
            throw new InvalidDataException($"Package entry '{entry.Id}' has no device path.");
        }

        string destination = SafeDestination(device.RootPath, entry.DevicePath);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        string temp = destination + ".installing";
        TryDelete(temp);

        try
        {
            await package.ExtractEntryAsync(entry, temp, progress: null, cancellationToken);
            if (entry.IsOperatingSystem)
            {
                var candidate = FirmwareImage.Load(temp);
                if (!candidate.IsMi4)
                {
                    throw new InvalidDataException("The staged firmware is not a valid mi4 image.");
                }
                if (candidate.IsRockbox)
                {
                    throw new InvalidDataException("The package entry is a Rockbox bootloader, not original firmware.");
                }
                if (!candidate.CrcValid)
                {
                    throw new InvalidDataException("The staged firmware failed its CRC check.");
                }
                if (string.Equals(candidate.Header?.CryptoText, "TEA", StringComparison.Ordinal) && candidate.Key is null)
                {
                    throw new InvalidDataException("The staged firmware uses TEA but no known key could verify it.");
                }
            }

            if (File.Exists(destination) && options.KeepPreviousFirmware && entry.IsOperatingSystem)
            {
                string previous = destination + ".previous";
                try
                {
                    File.Copy(destination, previous, overwrite: true);
                }
                catch (Exception ex)
                {
                    log.Warning("Could not keep the previous firmware copy.", ex.Message);
                }
            }

            ClearReadOnly(destination);
            await MoveWithRetryAsync(temp, destination);

            if (options.VerifyAfterWrite)
            {
                string actual = await HashFileAsync(destination, cancellationToken);
                if (!string.IsNullOrWhiteSpace(entry.Sha256) &&
                    !string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Verification failed for {entry.DevicePath}.");
                }
                if (entry.IsOperatingSystem)
                {
                    var check = FirmwareImage.Load(destination);
                    if (!check.IsMi4 || check.IsRockbox || !check.CrcValid)
                    {
                        throw new InvalidDataException("Firmware verification after writing failed.");
                    }
                }
            }

            return new InstalledFileInfo
            {
                EntryId = entry.Id,
                Path = destination,
                Size = new FileInfo(destination).Length,
                Sha256 = entry.Sha256
            };
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public async Task RemoveRockboxDataAsync(H10Device device, bool deleteRockboxFolder, bool deleteOfMi4, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (!device.Volume.IsReady)
        {
            throw new InvalidOperationException("The player volume is not ready.");
        }

        if (deleteOfMi4)
        {
            string of = Path.Combine(device.RootPath, "System", "OF.mi4");
            if (File.Exists(of))
            {
                ClearReadOnly(of);
                File.Delete(of);
                log.Info("Deleted System\\OF.mi4.");
            }
        }

        if (deleteRockboxFolder)
        {
            string folder = Path.Combine(device.RootPath, ".rockbox");
            if (Directory.Exists(folder))
            {
                DeleteDirectory(folder);
                log.Info("Deleted the .rockbox folder.");
            }
        }
    }

    public static FirmwareImage? InspectFirmware(H10Device device)
    {
        try
        {
            return File.Exists(device.FirmwarePath) ? FirmwareImage.Load(device.FirmwarePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TestWriteAccess(string root)
    {
        string probe = Path.Combine(root, $".h10-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probe, "H10 Firmware Manager write test");
        }
        catch (Exception ex)
        {
            throw new IOException(
                "The player volume is read-only or locked. Close any program using it and try again. " + ex.Message, ex);
        }
        finally
        {
            TryDelete(probe);
        }
    }

    private static string SafeDestination(string root, string devicePath)
    {
        string relative = devicePath.Replace('/', '\\').TrimStart('\\');
        string normalizedRoot = Path.GetFullPath(root);
        string destination = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe device path in package: {devicePath}");
        }
        return destination;
    }

    private static async Task MoveWithRetryAsync(string source, string destination)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(200 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                await Task.Delay(200 * attempt);
            }
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        Directory.Delete(path, recursive: true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
