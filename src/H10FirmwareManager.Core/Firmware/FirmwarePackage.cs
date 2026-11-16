using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace H10FirmwareManager.Core.Firmware;

public sealed class FirmwarePackageManifest
{
    public int Schema { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public List<string> DefaultInstall { get; set; } = [];
    public List<FirmwarePackageEntry> Entries { get; set; } = [];
}

public sealed class FirmwarePackageEntry
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public int Order { get; set; }
    public Mi4Metadata? Mi4 { get; set; }

    public bool IsOperatingSystem => string.Equals(Role, "os", StringComparison.OrdinalIgnoreCase);
    public bool IsFont => string.Equals(Role, "font", StringComparison.OrdinalIgnoreCase);
    public bool IsRecovery => Role.StartsWith("recovery", StringComparison.OrdinalIgnoreCase);
    public bool IsDocument => string.Equals(Role, "document", StringComparison.OrdinalIgnoreCase);
}

public sealed class Mi4Metadata
{
    public string? VersionField { get; set; }
    public string? Version { get; set; }
    public long DataLength { get; set; }
    public long TotalLength { get; set; }
    public long PlainLength { get; set; }
    public long Crypto { get; set; }
    public string? CryptoName { get; set; }
    public string? Crc32 { get; set; }
    public bool Crc32Valid { get; set; }
    public string? Crc32Computed { get; set; }
    public bool Rockbox { get; set; }
    public long Size { get; set; }
    public string? Sha256 { get; set; }
    public string? Md5 { get; set; }
}

public sealed class PackageValidationResult
{
    public bool IsValid => Problems.Count == 0;
    public List<string> Problems { get; } = [];
    public List<string> Notes { get; } = [];
}

/// <summary>
/// A verified firmware package: a ZIP with manifest.json, firmware images and optional
/// recovery assets. The class keeps the ZIP open so entries can be streamed to the player.
/// </summary>
public sealed class FirmwarePackage : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ZipArchive _archive;
    private bool _disposed;

    private FirmwarePackage(string path, ZipArchive archive, FirmwarePackageManifest manifest)
    {
        PackagePath = path;
        _archive = archive;
        Manifest = manifest;
    }

    public string PackagePath { get; }
    public FirmwarePackageManifest Manifest { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Manifest.PackageName)
        ? Path.GetFileName(PackagePath)
        : Manifest.PackageName;

    public IReadOnlyList<FirmwarePackageEntry> Entries =>
        Manifest.Entries.OrderBy(e => e.Order).ToList();

    public IReadOnlyList<string> DefaultInstallIds => Manifest.DefaultInstall.Count > 0
        ? Manifest.DefaultInstall
        : Entries.Where(e => e.IsOperatingSystem || e.IsFont).Select(e => e.Id).ToList();

    public static FirmwarePackage Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Firmware package not found.", path);
        }

        var archive = ZipFile.OpenRead(path);
        try
        {
            var manifestEntry = FindArchiveEntry(archive, "manifest.json")
                ?? throw new InvalidDataException("The package does not contain manifest.json.");
            using var stream = manifestEntry.Open();
            var manifest = JsonSerializer.Deserialize<FirmwarePackageManifest>(stream, JsonOptions)
                ?? throw new InvalidDataException("manifest.json is empty or invalid.");

            if (manifest.Schema != 1)
            {
                throw new InvalidDataException($"Unsupported package schema {manifest.Schema}. Expected schema 1.");
            }
            if (manifest.Entries.Count == 0)
            {
                throw new InvalidDataException("The package manifest contains no entries.");
            }

            return new FirmwarePackage(Path.GetFullPath(path), archive, manifest);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public FirmwarePackageEntry? GetEntry(string id) =>
        Manifest.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public FirmwarePackageEntry? GetOperatingSystemEntry() =>
        Manifest.Entries.FirstOrDefault(e => e.IsOperatingSystem);

    public Stream OpenEntry(FirmwarePackageEntry entry)
    {
        ThrowIfDisposed();
        var archiveEntry = FindArchiveEntry(_archive, entry.ArchivePath)
            ?? throw new InvalidDataException($"Package entry '{entry.ArchivePath}' is missing.");
        return archiveEntry.Open();
    }

    public async Task ExtractEntryAsync(
        FirmwarePackageEntry entry,
        string destinationPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var input = OpenEntry(entry);
        await using var output = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.WriteThrough | FileOptions.Asynchronous);

        using var sha = SHA256.Create();
        byte[] buffer = new byte[1024 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
            progress?.Report(total);
        }
        sha.TransformFinalBlock([], 0, 0);
        await output.FlushAsync(cancellationToken);
        output.Flush(true);

        if (entry.Size > 0 && total != entry.Size)
        {
            throw new InvalidDataException($"'{entry.ArchivePath}' has {total:N0} bytes, expected {entry.Size:N0}.");
        }

        string actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(entry.Sha256) &&
            !string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 mismatch for '{entry.ArchivePath}'. The package is corrupt.");
        }
    }

    public async Task<PackageValidationResult> ValidateAsync(
        bool verifyHashes = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = new PackageValidationResult();

        if (string.IsNullOrWhiteSpace(Manifest.FirmwareVersion))
        {
            result.Problems.Add("Manifest has no firmware version.");
        }
        if (Manifest.Entries.All(e => !e.IsOperatingSystem))
        {
            result.Problems.Add("Manifest has no operating system entry.");
        }

        foreach (var entry in Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archiveEntry = FindArchiveEntry(_archive, entry.ArchivePath);
            if (archiveEntry is null)
            {
                result.Problems.Add($"Missing archive entry: {entry.ArchivePath}");
                continue;
            }

            if (entry.Size > 0 && archiveEntry.Length != entry.Size)
            {
                result.Problems.Add($"{entry.ArchivePath}: size {archiveEntry.Length:N0}, expected {entry.Size:N0}.");
            }

            if (verifyHashes && !string.IsNullOrWhiteSpace(entry.Sha256))
            {
                progress?.Report($"Verifying {entry.ArchivePath}…");
                await using var stream = archiveEntry.Open();
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.Problems.Add($"{entry.ArchivePath}: SHA-256 mismatch.");
                }
            }

            if (entry.Mi4 is { } meta && !meta.Crc32Valid)
            {
                result.Problems.Add($"{entry.ArchivePath}: stored mi4 CRC is not valid.");
            }
            if (entry.Mi4 is { Rockbox: true })
            {
                result.Problems.Add($"{entry.ArchivePath}: entry is a Rockbox bootloader, not original firmware.");
            }
        }

        if (result.Problems.Count == 0)
        {
            result.Notes.Add($"Package {Manifest.PackageId} is complete and verified.");
        }
        return result;
    }

    private static ZipArchiveEntry? FindArchiveEntry(ZipArchive archive, string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/');
        return archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _archive.Dispose();
    }
}

public static class FirmwarePackageLocator
{
    public const string DefaultFileName = "H10-20GC_original_1.03.01.zip";

    /// <summary>Finds the package bundled next to the app, or in a development checkout.</summary>
    public static string? FindBundledPackage()
    {
        var candidates = new List<string>();
        string baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "firmware", DefaultFileName));
        candidates.Add(Path.Combine(baseDir, DefaultFileName));

        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 7 && dir is not null; i++, dir = dir.Parent)
        {
            candidates.Add(Path.Combine(dir.FullName, "firmware", DefaultFileName));
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}
