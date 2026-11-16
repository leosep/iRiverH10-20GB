using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using H10FirmwareManager.Core.Devices;

namespace H10FirmwareManager.Core.Services;

public sealed class BackupManifest
{
    public int Schema { get; set; } = 1;
    public string BackupId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public BackupDeviceInfo Device { get; set; } = new();
    public List<BackupFileInfo> Files { get; set; } = [];
}

public sealed class BackupDeviceInfo
{
    public string Label { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public uint SerialNumber { get; set; }
    public string Product { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string FirmwareSha256 { get; set; } = string.Empty;
    public bool RockboxInstalled { get; set; }
}

public sealed class BackupFileInfo
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class BackupOptions
{
    public bool IncludeRockboxFolder { get; set; }
    public bool IncludeMusicDatabase { get; set; } = true;
    public string Reason { get; set; } = "Manual backup";
}

public sealed record BackupProgress(int Percent, string Stage, string? CurrentFile = null);

public sealed class BackupInfo
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public long ArchiveSize { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string DeviceLabel { get; init; } = string.Empty;
    public string FirmwareVersion { get; init; } = string.Empty;
    public bool RockboxInstalled { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public bool IsValid { get; init; } = true;
    public string? Error { get; init; }

    public string CreatedText => CreatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string SizeText => Humanize.Bytes(ArchiveSize);
    public string ContentText => $"{FileCount} files · {Humanize.Bytes(TotalBytes)}";
}

public sealed class BackupValidation
{
    public bool IsValid => Problems.Count == 0;
    public List<string> Problems { get; } = [];
    public int FilesChecked { get; set; }
}

public sealed class RestoreOptions
{
    public bool OverwriteExisting { get; set; } = true;
    public bool IncludeRockboxFolder { get; set; } = true;
}

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly LogService _log;
    private readonly SettingsService _settings;
    private readonly string _backupRoot;

    public BackupService(LogService log, SettingsService settings, string? backupRoot = null)
    {
        _log = log;
        _settings = settings;
        _backupRoot = backupRoot ?? settings.BackupDirectory;
        Directory.CreateDirectory(_backupRoot);
    }

    public string BackupRoot => _backupRoot;

    public async Task<BackupInfo> CreateBackupAsync(
        H10Device device,
        BackupOptions? options = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BackupOptions();
        var volume = device.Volume;
        if (!volume.IsReady)
        {
            throw new InvalidOperationException("The player volume is not ready.");
        }

        Directory.CreateDirectory(_backupRoot);
        var files = CollectFiles(device, options);
        long totalBytes = files.Sum(f => f.Size);

        var manifest = new BackupManifest
        {
            BackupId = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            Reason = options.Reason,
            AppVersion = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Device = new BackupDeviceInfo
            {
                Label = volume.Label,
                FileSystem = volume.FileSystem,
                TotalBytes = volume.TotalBytes,
                SerialNumber = volume.SerialNumber,
                Product = volume.ManufacturerText,
                FirmwareVersion = device.FirmwareVersion,
                FirmwareSha256 = device.Firmware?.Sha256 ?? string.Empty,
                RockboxInstalled = device.RockboxInstalled
            }
        };

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string finalPath = Path.Combine(_backupRoot, $"H10-backup-{stamp}.zip");
        for (int i = 2; File.Exists(finalPath); i++)
        {
            finalPath = Path.Combine(_backupRoot, $"H10-backup-{stamp}-{i}.zip");
        }
        string tempPath = finalPath + ".partial";

        _log.Info($"Creating backup of {volume.DriveLetter} ({files.Count} files, {Humanize.Bytes(totalBytes)}).", options.Reason);
        progress?.Report(new BackupProgress(0, "Scanning player"));

        try
        {
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                             1024 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                long done = 0;
                int index = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    index++;
                    progress?.Report(new BackupProgress(
                        5 + (int)(85.0 * done / Math.Max(1, totalBytes)), "Backing up files", file.RelativePath));

                    var entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(file.FullPath);

                    string hash;
                    await using (var source = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                     1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous))
                    await using (var target = entry.Open())
                    using (var sha = SHA256.Create())
                    {
                        byte[] buffer = new byte[1024 * 1024];
                        int read;
                        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            done += read;
                        }
                        sha.TransformFinalBlock([], 0, 0);
                        hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                    }

                    manifest.Files.Add(new BackupFileInfo
                    {
                        Path = file.RelativePath.Replace('\\', '/'),
                        Size = file.Size,
                        Sha256 = hash
                    });

                    if (index % 10 == 0)
                    {
                        _log.Debug($"Backed up {index}/{files.Count} files.");
                    }
                }

                progress?.Report(new BackupProgress(92, "Writing manifest"));
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var stream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                }

                var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
                await using (var stream = readmeEntry.Open())
                await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    await writer.WriteAsync(BuildReadme(manifest));
                }

                progress?.Report(new BackupProgress(98, "Flushing backup"));
            }

            File.Move(tempPath, finalPath, overwrite: true);
            progress?.Report(new BackupProgress(100, "Backup complete"));

            _log.Success($"Backup created: {finalPath}", $"{manifest.Files.Count} files, {Humanize.Bytes(totalBytes)}");
            return new BackupInfo
            {
                FilePath = finalPath,
                ArchiveSize = new FileInfo(finalPath).Length,
                CreatedAt = manifest.CreatedAt,
                Reason = manifest.Reason,
                DeviceLabel = manifest.Device.Label,
                FirmwareVersion = manifest.Device.FirmwareVersion,
                RockboxInstalled = manifest.Device.RockboxInstalled,
                FileCount = manifest.Files.Count,
                TotalBytes = totalBytes
            };
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(_backupRoot))
        {
            return [];
        }

        var result = new List<BackupInfo>();
        foreach (string file in Directory.EnumerateFiles(_backupRoot, "*.zip").OrderByDescending(f => f))
        {
            result.Add(ReadInfo(file));
        }
        return result.OrderByDescending(b => b.CreatedAt ?? DateTimeOffset.MinValue).ToList();
    }

    public BackupInfo ReadInfo(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var manifest = ReadManifest(archive);
            if (manifest is null)
            {
                return new BackupInfo
                {
                    FilePath = path,
                    ArchiveSize = new FileInfo(path).Length,
                    IsValid = false,
                    Error = "manifest.json is missing or invalid"
                };
            }

            return new BackupInfo
            {
                FilePath = path,
                ArchiveSize = new FileInfo(path).Length,
                CreatedAt = manifest.CreatedAt,
                Reason = manifest.Reason,
                DeviceLabel = manifest.Device.Label,
                FirmwareVersion = manifest.Device.FirmwareVersion,
                RockboxInstalled = manifest.Device.RockboxInstalled,
                FileCount = manifest.Files.Count,
                TotalBytes = manifest.Files.Sum(f => f.Size)
            };
        }
        catch (Exception ex)
        {
            return new BackupInfo
            {
                FilePath = path,
                ArchiveSize = new FileInfo(path).Length,
                IsValid = false,
                Error = ex.Message
            };
        }
    }

    public async Task<BackupValidation> VerifyBackupAsync(
        string path,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BackupValidation();
        using var archive = ZipFile.OpenRead(path);
        var manifest = ReadManifest(archive);
        if (manifest is null)
        {
            result.Problems.Add("manifest.json is missing or invalid.");
            return result;
        }

        long total = Math.Max(1, manifest.Files.Sum(f => f.Size));
        long done = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry(file.Path.Replace('\\', '/'));
            if (entry is null)
            {
                result.Problems.Add($"Missing file: {file.Path}");
                continue;
            }
            if (entry.Length != file.Size)
            {
                result.Problems.Add($"Size mismatch: {file.Path}");
            }

            await using var stream = entry.Open();
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Problems.Add($"SHA-256 mismatch: {file.Path}");
            }

            done += file.Size;
            result.FilesChecked++;
            progress?.Report(new BackupProgress(
                (int)(100.0 * done / total), "Verifying backup", file.Path));
        }

        return result;
    }

    public async Task<int> RestoreAsync(
        BackupInfo backup,
        H10Device device,
        RestoreOptions? options = null,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RestoreOptions();
        if (!device.Volume.IsReady)
        {
            throw new InvalidOperationException("The player volume is not ready.");
        }

        using var archive = ZipFile.OpenRead(backup.FilePath);
        var manifest = ReadManifest(archive)
            ?? throw new InvalidDataException("The backup has no valid manifest.json.");

        var files = manifest.Files
            .Where(f => options.IncludeRockboxFolder ||
                        !f.Path.StartsWith(".rockbox/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        long total = Math.Max(1, files.Sum(f => f.Size));
        long done = 0;
        int restored = 0;

        _log.Info($"Restoring backup {backup.FileName} to {device.Volume.DriveLetter}.");
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = file.Path.Replace('/', '\\').TrimStart('\\');
            string destination = GetSafeDestination(device.RootPath, relative);
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(destination) && !options.OverwriteExisting)
            {
                done += file.Size;
                continue;
            }

            var entry = archive.GetEntry(file.Path.Replace('\\', '/'))
                ?? throw new InvalidDataException($"Backup entry missing: {file.Path}");
            string temp = destination + ".restoring";
            TryDelete(temp);

            progress?.Report(new BackupProgress(
                (int)(100.0 * done / total), "Restoring files", relative));

            try
            {
                await using (var source = entry.Open())
                await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                 1024 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous))
                using (var sha = SHA256.Create())
                {
                    byte[] buffer = new byte[1024 * 1024];
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                    }
                    sha.TransformFinalBlock([], 0, 0);
                    target.Flush(true);

                    string actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                    if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Checksum mismatch while restoring {file.Path}.");
                    }
                }

                ClearReadOnly(destination);
                File.Move(temp, destination, overwrite: true);
                restored++;
            }
            catch
            {
                TryDelete(temp);
                throw;
            }

            done += file.Size;
        }

        VolumeService.TryFlushVolume(device.RootPath, out _);
        progress?.Report(new BackupProgress(100, "Restore complete"));
        _log.Success($"Restored {restored} files from {backup.FileName}.");
        return restored;
    }

    public void DeleteBackup(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            _log.Info($"Deleted backup {Path.GetFileName(path)}.");
        }
    }

    private static BackupManifest? ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json");
        if (entry is null)
        {
            return null;
        }
        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private List<BackupSourceFile> CollectFiles(H10Device device, BackupOptions options)
    {
        var files = new List<BackupSourceFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFile(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return;
            }
            string relative = Path.GetRelativePath(device.RootPath, fullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal) || !seen.Add(relative))
            {
                return;
            }
            var info = new FileInfo(fullPath);
            if (info.Length > 2L * 1024 * 1024 * 1024)
            {
                _log.Warning($"Skipping very large file in backup: {relative}");
                return;
            }
            files.Add(new BackupSourceFile(fullPath, relative, info.Length));
        }

        if (Directory.Exists(device.SystemPath))
        {
            foreach (string file in Directory.EnumerateFiles(device.SystemPath, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".installing", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".restoring", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                AddFile(file);
            }
        }

        foreach (string file in Directory.EnumerateFiles(device.RootPath, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            string ext = Path.GetExtension(file);
            if (ext.Equals(".mi4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".hex", StringComparison.OrdinalIgnoreCase) ||
                (options.IncludeMusicDatabase &&
                 name.StartsWith("H10DB", StringComparison.OrdinalIgnoreCase)))
            {
                AddFile(file);
            }
        }

        if (options.IncludeRockboxFolder && Directory.Exists(device.RockboxPath))
        {
            foreach (string file in Directory.EnumerateFiles(device.RockboxPath, "*", SearchOption.AllDirectories))
            {
                AddFile(file);
            }
        }

        return files;
    }

    private static string GetSafeDestination(string root, string relative)
    {
        string normalizedRoot = Path.GetFullPath(root);
        string destination = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsafe path in backup: {relative}");
        }
        return destination;
    }

    private static string BuildReadme(BackupManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("iRiver H10 20GB — Firmware Manager backup");
        sb.AppendLine("==========================================");
        sb.AppendLine();
        sb.AppendLine($"Created:  {manifest.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Reason:   {manifest.Reason}");
        sb.AppendLine($"Device:   {manifest.Device.Label} ({manifest.Device.Product})");
        sb.AppendLine($"Firmware: {manifest.Device.FirmwareVersion}");
        sb.AppendLine($"Files:    {manifest.Files.Count}");
        sb.AppendLine();
        sb.AppendLine("Restore it from the Backups page of H10 Firmware Manager.");
        sb.AppendLine("Every file is checked against its SHA-256 hash before it is written.");
        sb.AppendLine("Do not edit the ZIP manually.");
        return sb.ToString();
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record BackupSourceFile(string FullPath, string RelativePath, long Size);
}
