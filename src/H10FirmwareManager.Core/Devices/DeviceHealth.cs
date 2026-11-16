namespace H10FirmwareManager.Core.Devices;

public enum HealthSeverity
{
    Info,
    Warning,
    Critical
}

public sealed record HealthIssue(HealthSeverity Severity, string Title, string Detail, string? FixHint = null);

public sealed class DeviceHealth
{
    public required H10Device Device { get; init; }
    public List<HealthIssue> Issues { get; init; } = [];

    public bool IsHealthy => Issues.All(i => i.Severity != HealthSeverity.Critical);
    public bool NeedsAttention => Issues.Any(i => i.Severity != HealthSeverity.Info);

    public HealthSeverity Overall => Issues.Any(i => i.Severity == HealthSeverity.Critical)
        ? HealthSeverity.Critical
        : Issues.Any(i => i.Severity == HealthSeverity.Warning)
            ? HealthSeverity.Warning
            : HealthSeverity.Info;

    public string Summary => Overall switch
    {
        HealthSeverity.Critical => "Needs repair",
        HealthSeverity.Warning => "Needs attention",
        _ => "Healthy"
    };

    public static DeviceHealth Check(H10Device device)
    {
        var health = new DeviceHealth { Device = device };
        var volume = device.Volume;

        if (!volume.IsReady)
        {
            health.Issues.Add(new(HealthSeverity.Critical, "Volume not available",
                volume.Error ?? "Windows cannot read this volume.", "Reconnect the player or format it."));
            return health;
        }

        if (!string.Equals(volume.FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase))
        {
            health.Issues.Add(new(HealthSeverity.Critical, "Not formatted as FAT32",
                $"The player uses {volume.FileSystem}. The H10 bootloader only reads FAT32.",
                "Use Repair → Format player (FAT32, 16 KB clusters)."));
        }

        if (!device.IsH10)
        {
            health.Issues.Add(new(HealthSeverity.Info, "Not confirmed as an H10",
                "The volume label, USB descriptor and size do not all match an H10 20GB.",
                "Select the correct drive manually."));
        }

        if (!string.Equals(volume.Label, "H10", StringComparison.OrdinalIgnoreCase))
        {
            health.Issues.Add(new(HealthSeverity.Info, "Volume label is not H10",
                $"Current label: {(string.IsNullOrWhiteSpace(volume.Label) ? "(none)" : volume.Label)}.",
                "Formatting restores the H10 label; it is not required for booting."));
        }

        if (volume.ClusterSize > 0 && volume.ClusterSize != 16384)
        {
            health.Issues.Add(new(HealthSeverity.Info, "Cluster size differs from factory",
                $"Current cluster size: {volume.ClusterSize:N0} bytes. The factory H10 uses 16,384 bytes.",
                "Only matters if you reformat the player."));
        }

        if (volume.FreeBytes < 50L * 1024 * 1024)
        {
            health.Issues.Add(new(HealthSeverity.Warning, "Low free space",
                $"{Humanize.Bytes(volume.FreeBytes)} free. Installation needs about 10 MB.",
                "Free some space before installing firmware."));
        }

        if (!device.HasSystemFolder)
        {
            health.Issues.Add(new(HealthSeverity.Critical, "System folder missing",
                @"\System does not exist. A freshly formatted drive looks like this.",
                "Install firmware: the app creates \\System and \\System\\Font automatically."));
        }

        if (!device.HasFirmware)
        {
            health.Issues.Add(new(HealthSeverity.Critical, "Firmware missing",
                @"\System\H10_20GC.mi4 does not exist.",
                "Install the original firmware from the bundled package."));
        }
        else if (device.Firmware is { IsRockbox: true })
        {
            health.Issues.Add(new(HealthSeverity.Warning, "Rockbox bootloader detected",
                "The player currently boots Rockbox. The original firmware is usually kept as System\\OF.mi4.",
                "Use the Rockbox page to replace it with the original firmware."));
        }
        else if (device.Firmware is { IsMi4: false })
        {
            health.Issues.Add(new(HealthSeverity.Critical, "Unknown firmware file",
                "System\\H10_20GC.mi4 exists but is not a valid mi4 image.",
                "Install the original firmware to overwrite it."));
        }
        else if (device.Firmware is { CrcChecked: true, CrcValid: false })
        {
            health.Issues.Add(new(HealthSeverity.Critical, "Firmware CRC is invalid",
                "The firmware file is damaged or truncated.",
                "Install the original firmware again."));
        }

        if (!device.HasFontFile)
        {
            health.Issues.Add(new(HealthSeverity.Warning, "System font missing",
                @"\System\Font\cjk12.fnt is missing. Text rendering may fail.",
                "Install the firmware with the font option enabled."));
        }

        if (device.HasOfMi4)
        {
            health.Issues.Add(new(HealthSeverity.Info, "OF.mi4 present",
                "A previous original firmware is kept as System\\OF.mi4 (typical after Rockbox).",
                "It can be removed after the original firmware is working."));
        }

        if (device.HasRockboxFolder)
        {
            health.Issues.Add(new(HealthSeverity.Info, ".rockbox folder present",
                "Rockbox data is still on the player.",
                "Use Repair → Remove Rockbox data, or keep it for dual boot."));
        }

        if (device.Firmware is { IsMi4: true, IsRockbox: false } firmware &&
            firmware.Header is { } header &&
            header.VersionText != "1.03.01")
        {
            health.Issues.Add(new(HealthSeverity.Info, "Different firmware version",
                $"Installed version: {header.VersionText}. The bundled original is 1.03.01.",
                "Install the bundled firmware if you want the known-good factory version."));
        }

        return health;
    }
}
