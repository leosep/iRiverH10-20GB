using System.Text.Json;

namespace H10FirmwareManager.Core.Services;

public sealed class AppSettings
{
    public string? LastVolumeRoot { get; set; }
    public string? PackagePath { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public bool BackupIncludesRockbox { get; set; }
    public bool KeepPreviousFirmware { get; set; } = true;
    public bool VerifyAfterWrite { get; set; } = true;
    public bool CreateBackupBeforeInstall { get; set; } = true;
    public bool DeleteRockboxDataByDefault { get; set; }
    public bool ConfirmBeforeFormat { get; set; } = true;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "H10FirmwareManager");
        Directory.CreateDirectory(DataDirectory);
        _path = Path.Combine(DataDirectory, "settings.json");
        Settings = Load();
    }

    public string DataDirectory { get; }
    public string SettingsPath => _path;
    public AppSettings Settings { get; private set; }

    public string BackupDirectory
    {
        get
        {
            string dir = string.IsNullOrWhiteSpace(Settings.BackupDirectory)
                ? Path.Combine(DataDirectory, "Backups")
                : Environment.ExpandEnvironmentVariables(Settings.BackupDirectory);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // A corrupt settings file should not prevent the app from starting.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(_path, JsonSerializer.Serialize(Settings, JsonOptions));
    }
}
