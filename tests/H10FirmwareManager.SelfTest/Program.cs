using System.Text;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;
using H10FirmwareManager.Core.Services;

Console.OutputEncoding = Encoding.UTF8;
string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "selftest";
var failures = new List<string>();

try
{
    switch (mode)
    {
        case "selftest":
            await RunSelfTestAsync();
            break;
        case "analyze":
            Analyze(args.Skip(1).ToArray());
            break;
        case "volumes":
            PrintVolumes();
            break;
        case "devices":
            PrintDevices();
            break;
        case "package":
            await PrintPackageAsync(args.ElementAtOrDefault(1) ?? FirmwarePackageLocator.FindBundledPackage() ?? "");
            break;
        case "install-test":
            await RunInstallTestAsync(args.ElementAtOrDefault(1) ?? FirmwarePackageLocator.FindBundledPackage() ?? "");
            break;
        case "backup-test":
            await RunBackupTestAsync();
            break;
        default:
            Console.Error.WriteLine($"Unknown mode '{mode}'. Use: selftest | analyze <file> | volumes | devices | package <zip> | install-test <zip> | backup-test");
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAILED: " + ex);
    return 1;
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("FAILED CHECKS:");
    foreach (string failure in failures)
    {
        Console.WriteLine("  - " + failure);
    }
    return 1;
}

Console.WriteLine();
Console.WriteLine("All checks passed.");
return 0;

void Check(bool condition, string name, string? detail = null)
{
    if (condition)
    {
        Console.WriteLine($"  PASS  {name}" + (detail is null ? "" : $"  ({detail})"));
    }
    else
    {
        Console.WriteLine($"  FAIL  {name}" + (detail is null ? "" : $"  ({detail})"));
        failures.Add(name);
    }
}

void Analyze(string[] files)
{
    if (files.Length == 0)
    {
        files = [Path.Combine("F:\\", "System", "H10_20GC.mi4")];
    }
    foreach (string file in files)
    {
        if (!File.Exists(file))
        {
            Console.WriteLine($"Missing: {file}");
            continue;
        }
        var image = FirmwareImage.Load(file);
        Console.WriteLine(image.ToReport());
    }
}

void PrintVolumes()
{
    foreach (var volume in new VolumeService().EnumerateVolumes())
    {
        Console.WriteLine($"{volume.DriveLetter,-4} {volume.Label,-16} {volume.FileSystem,-6} " +
                          $"{Humanize.Bytes(volume.TotalBytes),10} free {Humanize.Bytes(volume.FreeBytes),10} " +
                          $"cluster {volume.ClusterSize,6:N0}  {volume.Vendor} {volume.Product} [{volume.BusType}] " +
                          $"serial {volume.SerialText}");
    }
}

void PrintDevices()
{
    foreach (var device in new DeviceService(new VolumeService()).FindDevices())
    {
        Console.WriteLine($"{device.Volume.DriveLetter,-4} H10={device.IsH10,-5} score={device.Score,2} " +
                          $"status={device.StatusText,-24} firmware={device.FirmwareVersion,-8} rockbox={device.RockboxInstalled}");
    }
}

async Task PrintPackageAsync(string path)
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        Console.WriteLine("Package not found: " + path);
        return;
    }
    using var package = FirmwarePackage.Open(path);
    Console.WriteLine($"Package: {package.DisplayName}");
    Console.WriteLine($"Id:      {package.Manifest.PackageId}");
    Console.WriteLine($"Version: {package.Manifest.FirmwareVersion}");
    Console.WriteLine($"Target:  {package.Manifest.Target}");
    Console.WriteLine($"Default: {string.Join(", ", package.DefaultInstallIds)}");
    Console.WriteLine("Entries:");
    foreach (var entry in package.Entries)
    {
        Console.WriteLine($"  {entry.Id,-18} {entry.Role,-18} {entry.Size,12:N0}  {entry.DevicePath}");
    }
    var validation = await package.ValidateAsync();
    Console.WriteLine(validation.IsValid ? "Validation: OK" : "Validation: FAILED");
    foreach (string problem in validation.Problems)
    {
        Console.WriteLine("  ! " + problem);
    }
}

async Task RunSelfTestAsync()
{
    Console.WriteLine("H10 Firmware Manager self-test");
    Console.WriteLine("==============================");

    Console.WriteLine();
    Console.WriteLine("CRC-32");
    uint crcCheck = Crc32.Compute("123456789"u8);
    Check(crcCheck == 0x2DFD2D88, "CRC-32 known vector (init 0, no final xor)", $"0x{crcCheck:X8}");

    Console.WriteLine();
    Console.WriteLine("TEA round-trip");
    var key = Mi4Crypto.FindByName("20gc_eng")!;
    byte[] block = Enumerable.Range(0, 8).Select(i => (byte)(i * 37 + 11)).ToArray();
    byte[] original = block.ToArray();
    Mi4Crypto.EncryptBlock(block, key.ToArray());
    Mi4Crypto.DecryptBlock(block, key.ToArray());
    Check(block.SequenceEqual(original), "TEA encrypt/decrypt round-trip");

    string bundled = FirmwarePackageLocator.FindBundledPackage() ?? "";
    Console.WriteLine();
    Console.WriteLine("Bundled package");
    Check(File.Exists(bundled), "Package found", bundled);
    if (!File.Exists(bundled))
    {
        return;
    }

    using var package = FirmwarePackage.Open(bundled);
    var validation = await package.ValidateAsync();
    Check(validation.IsValid, "Package manifest and hashes", string.Join(" ", validation.Problems));
    var osEntry = package.GetOperatingSystemEntry();
    Check(osEntry is not null, "Operating system entry present");
    if (osEntry is null)
    {
        return;
    }
    Check(osEntry.Mi4 is { Crc32Valid: true }, "Package OS metadata has valid CRC", osEntry.Mi4?.Crc32);
    Check(!osEntry.Mi4!.Rockbox, "Package OS is not Rockbox");

    string tempRoot = Path.Combine(Path.GetTempPath(), "h10-selftest-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempRoot);
    try
    {
        string extracted = Path.Combine(tempRoot, "H10_20GC.mi4");
        await package.ExtractEntryAsync(osEntry, extracted);
        var image = FirmwareImage.Load(extracted);
        Check(image.IsMi4, "Extracted image is mi4");
        Check(image.CrcValid, "Extracted image CRC is valid", $"0x{image.ComputedCrc32:X8}");
        Check(image.Header?.VersionText == "1.03.01", "Extracted version is 1.03.01", image.Header?.VersionText);
        Check(image.Key?.Name == "20gc_eng", "TEA key identified", image.Key?.Name);
        Check(!image.IsRockbox, "Not a Rockbox bootloader");

        Console.WriteLine();
        Console.WriteLine("Install into a simulated player volume");
        string player = Path.Combine(tempRoot, "player");
        Directory.CreateDirectory(player);
        var volume = new VolumeInfo
        {
            RootPath = player,
            DriveLetter = "T:",
            Label = "H10",
            FileSystem = "FAT32",
            TotalBytes = 20L * 1024 * 1024 * 1024,
            FreeBytes = 1024L * 1024 * 1024,
            ClusterSize = 16384,
            DriveType = DriveType.Fixed,
            IsReady = true
        };
        var device = new H10Device
        {
            Volume = volume,
            HasSystemFolder = false,
            Score = 10
        };

        var log = new LogService(Path.Combine(tempRoot, "data"));
        var settings = new SettingsService(Path.Combine(tempRoot, "data"));
        var backups = new BackupService(log, settings, Path.Combine(tempRoot, "backups"));
        var installer = new InstallService(log, backups);

        var result = await installer.InstallAsync(device, package, new InstallOptions
        {
            CreateBackup = false,
            AllowUnconfirmedDevice = true,
            KeepPreviousFirmware = true,
            VerifyAfterWrite = true
        });
        Check(result.Success, "Install reported success", result.Error);
        string installedFirmware = Path.Combine(player, "System", "H10_20GC.mi4");
        string installedFont = Path.Combine(player, "System", "Font", "cjk12.fnt");
        Check(File.Exists(installedFirmware), "Firmware written");
        Check(File.Exists(installedFont), "Font written");
        if (File.Exists(installedFirmware))
        {
            var installed = FirmwareImage.Load(installedFirmware);
            Check(installed.Sha256 == osEntry.Sha256, "Installed firmware hash matches package");
            Check(installed.CrcValid, "Installed firmware CRC is valid");
        }

        Console.WriteLine();
        Console.WriteLine("Backup and restore");
        var backup = await backups.CreateBackupAsync(device, new BackupOptions
        {
            IncludeRockboxFolder = false,
            Reason = "Self-test backup"
        });
        Check(File.Exists(backup.FilePath), "Backup ZIP created", backup.FileName);
        var backupValidation = await backups.VerifyBackupAsync(backup.FilePath);
        Check(backupValidation.IsValid, "Backup verifies", string.Join(" ", backupValidation.Problems));
        var listed = backups.ListBackups();
        Check(listed.Count == 1 && listed[0].FileCount >= 2, "Backup appears in list", $"{listed.FirstOrDefault()?.ContentText}");

        File.Delete(installedFirmware);
        int restored = await backups.RestoreAsync(backup, device);
        Check(restored >= 2, "Restore wrote files", restored.ToString());
        Check(File.Exists(installedFirmware), "Firmware restored");
        if (File.Exists(installedFirmware))
        {
            Check(FirmwareImage.Load(installedFirmware).Sha256 == osEntry.Sha256, "Restored firmware hash matches");
        }

        Console.WriteLine();
        Console.WriteLine("Rockbox replacement");
        File.WriteAllBytes(installedFirmware, new byte[4096]);
        var rockboxBytes = File.ReadAllBytes(installedFirmware);
        rockboxBytes[0x1F8] = (byte)'R'; rockboxBytes[0x1F9] = (byte)'B';
        rockboxBytes[0x1FA] = (byte)'B'; rockboxBytes[0x1FB] = (byte)'L';
        File.WriteAllBytes(installedFirmware, rockboxBytes);
        File.WriteAllText(Path.Combine(player, "System", "OF.mi4"), "old original");
        Directory.CreateDirectory(Path.Combine(player, ".rockbox"));
        File.WriteAllText(Path.Combine(player, ".rockbox", "config.cfg"), "test");

        var rockboxDevice = new DeviceService(new VolumeService()).Inspect(volume);
        var rockboxService = new RockboxService(installer);
        var status = rockboxService.Detect(rockboxDevice);
        Check(status.BootloaderPresent, "Rockbox bootloader detected");
        Check(status.FolderPresent, ".rockbox folder detected");

        var replace = await installer.InstallAsync(rockboxDevice, package, new InstallOptions
        {
            CreateBackup = false,
            AllowUnconfirmedDevice = true,
            DeleteRockboxFolder = true,
            DeleteOfMi4 = true,
            VerifyAfterWrite = true
        });
        Check(replace.Success, "Replacement install succeeded", replace.Error);
        var after = FirmwareImage.Load(installedFirmware);
        Check(!after.IsRockbox && after.CrcValid, "Original firmware replaced Rockbox");
        Check(!File.Exists(Path.Combine(player, "System", "OF.mi4")), "OF.mi4 removed");
        Check(!Directory.Exists(Path.Combine(player, ".rockbox")), ".rockbox folder removed");
    }
    finally
    {
        try { Directory.Delete(tempRoot, recursive: true); } catch { }
    }
}

async Task RunInstallTestAsync(string packagePath)
{
    if (!File.Exists(packagePath))
    {
        throw new FileNotFoundException("Package not found.", packagePath);
    }
    using var package = FirmwarePackage.Open(packagePath);
    string player = Path.Combine(Path.GetTempPath(), "h10-install-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(player);
    try
    {
        var volume = new VolumeInfo
        {
            RootPath = player, DriveLetter = "T:", Label = "H10", FileSystem = "FAT32",
            TotalBytes = 20L * 1024 * 1024 * 1024, FreeBytes = 1024L * 1024 * 1024,
            ClusterSize = 16384, DriveType = DriveType.Fixed, IsReady = true
        };
        var device = new H10Device { Volume = volume, Score = 10 };
        var log = new LogService(Path.Combine(player, ".testdata"));
        var installer = new InstallService(log, new BackupService(log, new SettingsService(Path.Combine(player, ".testdata"))));
        var result = await installer.InstallAsync(device, package, new InstallOptions
        {
            CreateBackup = false, AllowUnconfirmedDevice = true, VerifyAfterWrite = true
        });
        Console.WriteLine(result.Success ? "PASS: install test" : "FAIL: " + result.Error);
        foreach (string warning in result.Warnings)
        {
            Console.WriteLine("  warning: " + warning);
        }
    }
    finally
    {
        try { Directory.Delete(player, recursive: true); } catch { }
    }
}

Task RunBackupTestAsync()
{
    Console.WriteLine("Use selftest; it covers backup and restore in a temporary directory.");
    return Task.CompletedTask;
}
