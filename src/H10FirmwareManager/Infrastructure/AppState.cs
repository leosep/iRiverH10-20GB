using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;
using H10FirmwareManager.Core.Services;
using System.IO;

namespace H10FirmwareManager;

public sealed class AppState
{
    private readonly SettingsService _settings;

    public AppState(SettingsService settings)
    {
        _settings = settings;
        PackagePath = ResolvePackagePath();
    }

    public IReadOnlyList<H10Device> Devices { get; private set; } = [];
    public H10Device? SelectedDevice { get; private set; }
    public string? PackagePath { get; private set; }
    public event EventHandler? Changed;

    public bool HasH10 => Devices.Any(d => d.IsH10);
    public bool HasDevice => SelectedDevice is not null;

    public async Task RefreshAsync()
    {
        var devices = await Task.Run(() => AppServices.Devices.FindDevices());
        string? preferred = SelectedDevice?.RootPath ?? _settings.Settings.LastVolumeRoot;
        var selected = devices.FirstOrDefault(d => string.Equals(d.RootPath, preferred, StringComparison.OrdinalIgnoreCase))
                       ?? devices.FirstOrDefault(d => d.IsH10)
                       ?? devices.FirstOrDefault();

        Devices = devices;
        SelectedDevice = selected;
        if (selected is not null)
        {
            _settings.Settings.LastVolumeRoot = selected.RootPath;
            _settings.Save();
        }

        AppServices.Log.Info($"Detected {devices.Count} candidate volume(s); selected {selected?.Volume.DisplayName ?? "none"}.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectDevice(H10Device? device)
    {
        if (device is null)
        {
            return;
        }
        SelectedDevice = device;
        _settings.Settings.LastVolumeRoot = device.RootPath;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public H10Device? FindDevice(string rootPath) =>
        Devices.FirstOrDefault(d => string.Equals(d.RootPath, rootPath, StringComparison.OrdinalIgnoreCase));

    public void SetPackagePath(string path)
    {
        PackagePath = path;
        _settings.Settings.PackagePath = path;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public FirmwarePackage OpenPackage()
    {
        string path = PackagePath ?? throw new InvalidOperationException("No firmware package is selected.");
        return FirmwarePackage.Open(path);
    }

    private string? ResolvePackagePath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Settings.PackagePath) && File.Exists(_settings.Settings.PackagePath))
        {
            return _settings.Settings.PackagePath;
        }
        string? bundled = FirmwarePackageLocator.FindBundledPackage();
        if (bundled is not null)
        {
            _settings.Settings.PackagePath = bundled;
            _settings.Save();
        }
        return bundled;
    }
}
