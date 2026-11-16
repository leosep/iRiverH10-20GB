using H10FirmwareManager.Core.Firmware;

namespace H10FirmwareManager.Core.Devices;

/// <summary>A volume that looks like (or might be) an iRiver H10 20GB.</summary>
public sealed class H10Device
{
    public required VolumeInfo Volume { get; init; }
    public FirmwareImage? Firmware { get; init; }
    public FirmwareImage? OfFirmware { get; init; }
    public bool HasSystemFolder { get; init; }
    public bool HasFontFile { get; init; }
    public bool HasRockboxFolder { get; init; }
    public bool HasOfMi4 { get; init; }
    public int Score { get; init; }

    public string RootPath => Volume.RootPath;
    public bool IsH10 => Score >= 7;
    public bool HasFirmware => Firmware is not null;

    public bool RockboxInstalled =>
        Firmware?.IsRockbox == true || HasOfMi4 || HasRockboxFolder;

    public string FirmwareVersion => Firmware?.Header?.VersionText ?? "not installed";

    public string FirmwareStateText
    {
        get
        {
            if (Firmware is null) return "No firmware";
            if (Firmware.IsRockbox) return "Rockbox bootloader";
            if (!Firmware.IsMi4) return "Unknown file";
            if (!Firmware.CrcChecked) return $"Original {Firmware.VersionText}";
            return Firmware.CrcValid ? $"Original {Firmware.VersionText}" : "Corrupt firmware";
        }
    }

    public string StatusText
    {
        get
        {
            if (!Volume.IsReady) return "Not ready";
            if (!HasSystemFolder) return "System folder missing";
            if (Firmware is null) return "Firmware missing";
            if (Firmware.IsRockbox) return "Rockbox installed";
            if (Firmware.CrcChecked && !Firmware.CrcValid) return "Firmware CRC invalid";
            return "Ready";
        }
    }

    public string SystemPath => Path.Combine(RootPath, "System");
    public string FirmwarePath => Path.Combine(SystemPath, "H10_20GC.mi4");
    public string FontPath => Path.Combine(SystemPath, "Font", "cjk12.fnt");
    public string OfMi4Path => Path.Combine(SystemPath, "OF.mi4");
    public string RockboxPath => Path.Combine(RootPath, ".rockbox");
}

public sealed class DeviceService(VolumeService volumeService)
{
    public IReadOnlyList<H10Device> FindDevices()
    {
        var devices = new List<H10Device>();
        foreach (var volume in volumeService.EnumerateVolumes())
        {
            devices.Add(Inspect(volume));
        }
        return devices
            .OrderByDescending(d => d.IsH10)
            .ThenByDescending(d => d.Score)
            .ThenBy(d => d.Volume.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<H10Device> FindH10Devices() => FindDevices().Where(d => d.IsH10).ToList();

    public H10Device? FindBest() => FindDevices().FirstOrDefault(d => d.IsH10);

    public H10Device Inspect(VolumeInfo volume)
    {
        if (!volume.IsReady)
        {
            return new H10Device { Volume = volume };
        }

        string system = Path.Combine(volume.RootPath, "System");
        string firmwarePath = Path.Combine(system, "H10_20GC.mi4");
        string ofPath = Path.Combine(system, "OF.mi4");
        string fontPath = Path.Combine(system, "Font", "cjk12.fnt");
        string rockboxPath = Path.Combine(volume.RootPath, ".rockbox");

        bool hasSystem = Directory.Exists(system);
        bool hasFirmware = File.Exists(firmwarePath);
        bool hasOf = File.Exists(ofPath);
        bool hasFont = File.Exists(fontPath);
        bool hasRockbox = Directory.Exists(rockboxPath);

        int score = 0;
        if (string.Equals(volume.Label, "H10", StringComparison.OrdinalIgnoreCase)) score += 5;
        string hardware = $"{volume.Vendor} {volume.Product}";
        if (hardware.Contains("iriver", StringComparison.OrdinalIgnoreCase) ||
            hardware.Contains("H10", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (volume.TotalBytes is > 15_000_000_000 and < 45_000_000_000) score += 2;
        if (hasSystem) score += 2;
        if (hasFirmware) score += 2;
        if (string.Equals(volume.FileSystem, "FAT32", StringComparison.OrdinalIgnoreCase)) score += 1;
        if (string.Equals(volume.BusType, "USB", StringComparison.OrdinalIgnoreCase)) score += 1;

        FirmwareImage? firmware = null;
        FirmwareImage? of = null;
        if (hasFirmware)
        {
            try { firmware = FirmwareImage.Load(firmwarePath, computeHashes: false, detectKey: false); }
            catch { /* locked or unreadable; the health check reports it */ }
        }
        if (hasOf)
        {
            try { of = FirmwareImage.Load(ofPath, computeHashes: false, detectKey: false); }
            catch { }
        }

        return new H10Device
        {
            Volume = volume,
            Firmware = firmware,
            OfFirmware = of,
            HasSystemFolder = hasSystem,
            HasFontFile = hasFont,
            HasRockboxFolder = hasRockbox,
            HasOfMi4 = hasOf,
            Score = score
        };
    }

    public H10Device GetByRoot(string rootPath)
    {
        return Inspect(volumeService.GetVolumeInfo(rootPath));
    }
}
