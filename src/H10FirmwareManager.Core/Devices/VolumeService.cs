using System.Security.Principal;

namespace H10FirmwareManager.Core.Devices;

public sealed class VolumeInfo
{
    public string RootPath { get; init; } = string.Empty;
    public string DriveLetter { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public uint SerialNumber { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public int ClusterSize { get; init; }
    public DriveType DriveType { get; init; }
    public bool IsReady { get; init; }
    public string Vendor { get; init; } = string.Empty;
    public string Product { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string Serial { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public bool IsRemovable { get; init; }
    public string? Error { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? $"{DriveLetter} (unlabeled)"
        : $"{DriveLetter} · {Label}";

    public string ManufacturerText => string.Join(' ', new[] { Vendor, Product }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string VolumeText => $"{Humanize.Bytes(TotalBytes)} {FileSystem}".Trim();

    public string SerialText => SerialNumber == 0 ? "—" : $"0x{SerialNumber:X8}";
}

public sealed class VolumeService
{
    public IReadOnlyList<VolumeInfo> EnumerateVolumes()
    {
        var result = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                {
                    continue;
                }
                if (!drive.IsReady)
                {
                    continue;
                }
                result.Add(GetVolumeInfo(drive.RootDirectory.FullName));
            }
            catch
            {
                // A volume can disappear between enumeration and query; skip it.
            }
        }
        return result;
    }

    public VolumeInfo GetVolumeInfo(string rootPath)
    {
        string root = NormalizeRoot(rootPath);
        var drive = new DriveInfo(root);

        if (!drive.IsReady)
        {
            return new VolumeInfo
            {
                RootPath = root,
                DriveLetter = root,
                DriveType = drive.DriveType,
                IsReady = false,
                Error = "The volume is not ready."
            };
        }

        var label = new System.Text.StringBuilder(261);
        var fs = new System.Text.StringBuilder(261);
        uint serial = 0, maxComponent = 0, flags = 0;
        bool gotVolume = NativeMethods.GetVolumeInformationW(
            root, label, label.Capacity, out serial, out maxComponent, out flags, fs, fs.Capacity);

        long total = 0, free = 0;
        if (NativeMethods.GetDiskFreeSpaceExW(root, out ulong freeToCaller, out ulong totalBytes, out ulong totalFree))
        {
            total = (long)totalBytes;
            free = (long)totalFree;
        }

        int clusterSize = 0;
        if (NativeMethods.GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _))
        {
            clusterSize = checked((int)(sectorsPerCluster * bytesPerSector));
        }

        var descriptor = TryGetStorageDescriptor(root);

        return new VolumeInfo
        {
            RootPath = root,
            DriveLetter = root.TrimEnd('\\'),
            Label = gotVolume ? label.ToString() : drive.VolumeLabel,
            FileSystem = gotVolume ? fs.ToString() : drive.DriveFormat,
            SerialNumber = serial,
            TotalBytes = total > 0 ? total : drive.TotalSize,
            FreeBytes = total > 0 ? free : drive.AvailableFreeSpace,
            ClusterSize = clusterSize,
            DriveType = drive.DriveType,
            IsReady = true,
            Vendor = descriptor?.Vendor ?? string.Empty,
            Product = descriptor?.Product ?? string.Empty,
            Revision = descriptor?.Revision ?? string.Empty,
            Serial = descriptor?.Serial ?? string.Empty,
            BusType = descriptor?.BusType ?? string.Empty,
            IsRemovable = descriptor?.Removable ?? drive.DriveType == DriveType.Removable
        };
    }

    private static StorageDescriptor? TryGetStorageDescriptor(string root)
    {
        string devicePath = @"\\.\" + root.TrimEnd('\\');
        IntPtr handle = NativeMethods.CreateFileW(
            devicePath, 0, NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return null;
        }

        try
        {
            var query = new NativeMethods.StoragePropertyQuery
            {
                PropertyId = 0, // StorageDeviceProperty
                QueryType = 0,  // PropertyStandardQuery
                AdditionalParameters = 0
            };
            byte[] buffer = new byte[1024];
            if (!NativeMethods.DeviceIoControl(
                    handle, NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
                    ref query, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.StoragePropertyQuery>(),
                    buffer, buffer.Length, out _, IntPtr.Zero))
            {
                return null;
            }

            var descriptor = System.Runtime.InteropServices.MemoryMarshal.Read<NativeMethods.StorageDeviceDescriptor>(buffer);
            if (descriptor.Size < 36)
            {
                return null;
            }

            return new StorageDescriptor
            {
                Vendor = NativeMethods.ReadAnsiString(buffer, descriptor.VendorIdOffset),
                Product = NativeMethods.ReadAnsiString(buffer, descriptor.ProductIdOffset),
                Revision = NativeMethods.ReadAnsiString(buffer, descriptor.ProductRevisionOffset),
                Serial = NativeMethods.ReadAnsiString(buffer, descriptor.SerialNumberOffset),
                Removable = descriptor.RemovableMedia != 0,
                BusType = BusTypeName(descriptor.BusType)
            };
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static string BusTypeName(uint busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown"
    };

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryFlushVolume(string rootPath, out string message)
    {
        string devicePath = @"\\.\" + rootPath.TrimEnd('\\');
        IntPtr handle = NativeMethods.CreateFileW(
            devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            message = "Volume flush needs administrator rights; per-file flushes were used instead.";
            return false;
        }

        try
        {
            bool ok = NativeMethods.FlushFileBuffers(handle);
            message = ok ? "Volume cache flushed." : "FlushFileBuffers failed; per-file flushes were used.";
            return ok;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A drive root is required.", nameof(rootPath));
        }

        string root = rootPath.Trim();
        if (root.Length == 2 && root[1] == ':')
        {
            root += "\\";
        }
        if (!root.EndsWith('\\'))
        {
            root += "\\";
        }
        return Path.GetFullPath(root);
    }

    private sealed class StorageDescriptor
    {
        public string Vendor { get; init; } = string.Empty;
        public string Product { get; init; } = string.Empty;
        public string Revision { get; init; } = string.Empty;
        public string Serial { get; init; } = string.Empty;
        public bool Removable { get; init; }
        public string BusType { get; init; } = string.Empty;
    }
}
