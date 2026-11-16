using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;
using H10FirmwareManager.Core.Services;

namespace H10FirmwareManager;

public static class AppServices
{
    private static bool _initialized;

    public static LogService Log { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static VolumeService Volumes { get; private set; } = null!;
    public static DeviceService Devices { get; private set; } = null!;
    public static BackupService Backups { get; private set; } = null!;
    public static InstallService Installer { get; private set; } = null!;
    public static FormatService Format { get; private set; } = null!;
    public static RockboxService Rockbox { get; private set; } = null!;
    public static AppState State { get; private set; } = null!;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        Settings = new SettingsService();
        Log = new LogService(Settings.DataDirectory);
        Volumes = new VolumeService();
        Devices = new DeviceService(Volumes);
        Backups = new BackupService(Log, Settings);
        Installer = new InstallService(Log, Backups);
        Format = new FormatService(Log);
        Rockbox = new RockboxService(Installer);
        State = new AppState(Settings);
    }
}
