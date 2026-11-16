using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Devices;

namespace H10FirmwareManager.Pages;

public partial class DashboardPage : UserControl, IRefreshablePage
{
    private bool _updating;

    public DashboardPage()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        if (AppServices.State.Devices.Count == 0)
        {
            await AppServices.State.RefreshAsync();
        }
        PopulateDevices();
        UpdateContent();
    }

    private void PopulateDevices()
    {
        _updating = true;
        var devices = AppServices.State.Devices;
        DeviceCombo.ItemsSource = devices;
        DeviceCombo.SelectedItem = AppServices.State.SelectedDevice ?? devices.FirstOrDefault();
        _updating = false;
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || DeviceCombo.SelectedItem is not H10Device device)
        {
            return;
        }
        AppServices.State.SelectDevice(device);
        MainWindow.Instance?.UpdateDeviceUi();
        UpdateContent();
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppServices.State.RefreshAsync();
            PopulateDevices();
            UpdateContent();
            MainWindow.Instance?.UpdateDeviceUi();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    private void UpdateContent()
    {
        var device = AppServices.State.SelectedDevice;
        if (device is null)
        {
            SetEmpty();
            return;
        }

        var volume = device.Volume;
        var firmware = device.Firmware;
        var health = DeviceHealth.Check(device);

        FirmwareVersionText.Text = device.FirmwareVersion;
        FirmwareStateText.Text = device.FirmwareStateText;
        FirmwareStateText.Foreground = firmware is { IsRockbox: true }
            ? (Brush)FindResource("WarningBrush")
            : firmware is null || firmware is { CrcChecked: true, CrcValid: false }
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("MutedBrush");

        StorageText.Text = $"{Humanize.Bytes(volume.UsedBytes)} used";
        StorageBar.Value = volume.UsedPercent;
        StorageFreeText.Text = $"{Humanize.Bytes(volume.FreeBytes)} free of {Humanize.Bytes(volume.TotalBytes)}";

        HealthText.Text = health.Summary;
        HealthText.Foreground = Ui.SeverityBrush(health.Overall);
        int issueCount = health.Issues.Count(i => i.Severity != HealthSeverity.Info);
        HealthDetailText.Text = issueCount == 0 ? "No problems detected" : $"{issueCount} item(s) need attention";

        HeroStatusText.Text = device.StatusText;
        HeroStatusText.Foreground = Ui.SeverityBrush(health.Overall);
        HeroDot.Fill = Ui.SeverityBrush(health.Overall);
        HeroStatusDetail.Text = $"Firmware {device.FirmwareVersion}";
        HeroBadge.Background = health.Overall switch
        {
            HealthSeverity.Critical => new SolidColorBrush(Color.FromRgb(0x2A, 0x14, 0x20)),
            HealthSeverity.Warning => new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x12)),
            _ => new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x24))
        };
        HeroBadge.BorderBrush = health.Overall switch
        {
            HealthSeverity.Critical => new SolidColorBrush(Color.FromRgb(0x6A, 0x2A, 0x3C)),
            HealthSeverity.Warning => new SolidColorBrush(Color.FromRgb(0x6A, 0x52, 0x1F)),
            _ => new SolidColorBrush(Color.FromRgb(0x1E, 0x5C, 0x4C))
        };

        DetailDrive.Text = $"{volume.DriveLetter} ({volume.DriveType})";
        DetailLabel.Text = string.IsNullOrWhiteSpace(volume.Label) ? "(none)" : volume.Label;
        DetailFileSystem.Text = volume.FileSystem;
        DetailCapacity.Text = $"{Humanize.Bytes(volume.TotalBytes)} · {Humanize.Bytes(volume.FreeBytes)} free";
        DetailCluster.Text = volume.ClusterSize > 0 ? $"{volume.ClusterSize:N0} bytes" : "—";
        DetailUsb.Text = string.IsNullOrWhiteSpace(volume.ManufacturerText)
            ? volume.BusType
            : $"{volume.ManufacturerText} · {volume.BusType}";
        DetailSerial.Text = volume.SerialText;

        SetFileStatus(FirmwareFileStatus, firmware,
            firmware is null ? "missing" :
            firmware.IsRockbox ? "Rockbox bootloader" :
            !firmware.CrcChecked ? $"{firmware.VersionText} · not checked" :
            firmware.CrcValid ? $"{firmware.VersionText} · CRC valid" : "CRC invalid",
            firmware is { IsMi4: true, IsRockbox: false, CrcValid: true });
        SetFileStatus(FontFileStatus, device.HasFontFile ? "present" : null,
            device.HasFontFile ? "present" : "missing", device.HasFontFile);
        SetFileStatus(OfFileStatus, device.HasOfMi4 ? "saved" : null,
            device.HasOfMi4 ? "previous firmware saved" : "not present", device.HasOfMi4);
        SetFileStatus(RockboxFolderStatus, device.HasRockboxFolder ? "present" : null,
            device.HasRockboxFolder ? "Rockbox data present" : "not present", !device.HasRockboxFolder);

        var notices = health.Issues.Select(issue => new NoticeItem(
            issue.Title,
            issue.Detail,
            Ui.SeverityBrush(issue.Severity))).ToList();

        if (firmware is { IsRockbox: true })
        {
            notices.Add(new NoticeItem("Rockbox can be replaced",
                "Use Rockbox → Replace with original firmware. A backup is created automatically.",
                (Brush)FindResource("AccentBrush")));
        }

        NoticesList.ItemsSource = notices;
        NoNoticesText.Visibility = notices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetEmpty()
    {
        FirmwareVersionText.Text = "—";
        FirmwareStateText.Text = "No player selected";
        FirmwareStateText.Foreground = (Brush)FindResource("MutedBrush");
        StorageText.Text = "—";
        StorageBar.Value = 0;
        StorageFreeText.Text = "Connect the player in Emergency Connect (UMS) mode.";
        HealthText.Text = "Unknown";
        HealthText.Foreground = (Brush)FindResource("MutedBrush");
        HealthDetailText.Text = "No device detected";
        HeroStatusText.Text = "No player";
        HeroStatusText.Foreground = (Brush)FindResource("MutedBrush");
        HeroDot.Fill = (Brush)FindResource("MutedBrush");
        HeroStatusDetail.Text = "Connect the H10";
        HeroBadge.Background = (Brush)FindResource("CardAltBrush");
        HeroBadge.BorderBrush = (Brush)FindResource("BorderBrush");

        DetailDrive.Text = DetailLabel.Text = DetailFileSystem.Text = "—";
        DetailCapacity.Text = DetailCluster.Text = DetailUsb.Text = DetailSerial.Text = "—";
        SetFileStatus(FirmwareFileStatus, null, "—", false);
        SetFileStatus(FontFileStatus, null, "—", false);
        SetFileStatus(OfFileStatus, null, "—", false);
        SetFileStatus(RockboxFolderStatus, null, "—", false);

        NoticesList.ItemsSource = new[]
        {
            new NoticeItem("No iRiver H10 detected",
                "Reset the player with a paperclip, connect USB, then hold O + Power until Emergency Connect appears. See the installation guide.",
                (Brush)FindResource("AccentBrush"))
        };
        NoNoticesText.Visibility = Visibility.Collapsed;
    }

    private void SetFileStatus(TextBlock target, object? _, string text, bool good)
    {
        target.Text = text;
        target.Foreground = good ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush");
    }

    private void Install_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.NavigateTo("install");
    private void Backup_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.NavigateTo("backups");
    private void Rockbox_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.NavigateTo("rockbox");
    private void Repair_Click(object sender, RoutedEventArgs e) => MainWindow.Instance?.NavigateTo("repair");

    private sealed record NoticeItem(string Title, string Detail, Brush Brush);
}
