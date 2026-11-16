using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using H10FirmwareManager.Core.Devices;

namespace H10FirmwareManager.Core.Services;

public sealed class FormatOptions
{
    public string Label { get; set; } = "H10";
    public int ClusterSize { get; set; } = 16384;
    public bool Quick { get; set; } = true;
}

public sealed class FormatResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public VolumeInfo? VolumeAfter { get; init; }
    public bool Canceled { get; init; }
}

/// <summary>
/// Formats the player as FAT32 using Windows' own format.com under elevation.
/// The H10 bootloader only understands FAT32, and the factory cluster size is 16 KB.
/// </summary>
public sealed class FormatService(LogService log)
{
    public async Task<FormatResult> FormatAsync(
        H10Device device,
        FormatOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FormatOptions();
        var volume = device.Volume;
        string drive = volume.DriveLetter.TrimEnd('\\');
        if (drive.Length != 2 || drive[1] != ':')
        {
            throw new ArgumentException("A drive letter is required for formatting.");
        }
        if (!volume.IsReady)
        {
            throw new InvalidOperationException("The selected volume is not ready.");
        }
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        if (string.Equals(drive + "\\", systemRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to format the Windows system drive.");
        }
        if (options.ClusterSize is < 512 or > 65536 || (options.ClusterSize & (options.ClusterSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Cluster size must be a power of two between 512 and 65536.");
        }

        string label = (options.Label ?? string.Empty).Trim();
        if (label.Length > 11)
        {
            label = label[..11];
        }
        if (label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The volume label contains invalid characters.");
        }

        string logPath = Path.Combine(Path.GetTempPath(), $"h10-format-{Guid.NewGuid():N}.log");
        string batchPath = Path.Combine(Path.GetTempPath(), $"h10-format-{Guid.NewGuid():N}.cmd");
        string command = BuildCommand(drive, label, options);
        string batch = $"""
            @echo off
            {command} > "{logPath}" 2>&1
            echo EXITCODE=%ERRORLEVEL%>> "{logPath}"
            """;
        await File.WriteAllTextAsync(batchPath, batch, new UTF8Encoding(false), cancellationToken);

        log.Warning($"Formatting {drive} as FAT32 ({options.ClusterSize:N0}-byte clusters, label {label}). All data will be erased.");
        progress?.Report("Waiting for administrator permission…");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batchPath}\"\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                log.Warning("Format canceled at the elevation prompt.");
                return new FormatResult
                {
                    Success = false,
                    Canceled = true,
                    Summary = "Format canceled: administrator permission was not granted."
                };
            }

            progress?.Report("Formatting…");
            await process.WaitForExitAsync(cancellationToken);
            string output = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath, cancellationToken) : string.Empty;
            int exitCode = ParseExitCode(output);
            progress?.Report("Refreshing volume information…");

            VolumeInfo? after = null;
            for (int i = 0; i < 60; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var candidate = new VolumeService().GetVolumeInfo(volume.RootPath);
                    if (candidate.IsReady && string.Equals(candidate.FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase))
                    {
                        after = candidate;
                        break;
                    }
                }
                catch
                {
                    // Windows is still remounting the volume.
                }
                await Task.Delay(500, cancellationToken);
            }

            bool success = exitCode == 0 && after is not null;
            if (success)
            {
                log.Success($"Formatted {drive} as FAT32.", $"{after!.TotalBytes:N0} bytes, {after.ClusterSize:N0}-byte clusters, label {after.Label}.");
            }
            else
            {
                log.Error($"Format failed with exit code {exitCode}.", output);
            }

            return new FormatResult
            {
                Success = success,
                ExitCode = exitCode,
                Output = output,
                VolumeAfter = after,
                Summary = success
                    ? $"{drive} formatted as FAT32 with {after!.ClusterSize:N0}-byte clusters."
                    : $"Format did not complete. Exit code {exitCode}."
            };
        }
        finally
        {
            TryDelete(batchPath);
            TryDelete(logPath);
        }
    }

    private static string BuildCommand(string drive, string label, FormatOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("format.com ").Append(drive).Append(" /FS:FAT32");
        if (!string.IsNullOrWhiteSpace(label))
        {
            sb.Append(" /V:").Append(label);
        }
        if (options.Quick)
        {
            sb.Append(" /Q");
        }
        if (options.ClusterSize > 0)
        {
            sb.Append(" /A:").Append(options.ClusterSize);
        }
        sb.Append(" /Y");
        return sb.ToString();
    }

    private static int ParseExitCode(string output)
    {
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("EXITCODE=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line["EXITCODE=".Length..].Trim(), out int code))
            {
                return code;
            }
        }
        return -1;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
