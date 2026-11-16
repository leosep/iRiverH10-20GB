using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;
using H10FirmwareManager.Core.Services;
using System.IO;

namespace H10FirmwareManager.Pages;

public partial class RepairPage : UserControl, IRefreshablePage
{
    private bool _busy;

    public RepairPage()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        RunCheck();
        return Task.CompletedTask;
    }

    private void RunCheck()
    {
        var device = AppServices.State.SelectedDevice;
        if (device is null)
        {
            SummaryText.Text = "No player selected";
            SummaryDot.Fill = (Brush)FindResource("MutedBrush");
            SummaryBadge.Background = (Brush)FindResource("CardAltBrush");
            SummaryBadge.BorderBrush = (Brush)FindResource("BorderBrush");
            IssuesList.ItemsSource = new[]
            {
                new IssueView("INFO", "No player detected",
                    "Connect the player in Emergency Connect (UMS) mode and select its drive on the Dashboard.",
                    null, (Brush)FindResource("MutedBrush"))
            };
            NoIssuesText.Visibility = Visibility.Collapsed;
            UpdateButtons();
            return;
        }

        var health = DeviceHealth.Check(device);
        SummaryText.Text = health.Summary;
        SummaryText.Foreground = Ui.SeverityBrush(health.Overall);
        SummaryDot.Fill = Ui.SeverityBrush(health.Overall);
        SummaryBadge.Background = health.Overall switch
        {
            HealthSeverity.Critical => new SolidColorBrush(Color.FromRgb(0x2A, 0x14, 0x20)),
            HealthSeverity.Warning => new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x12)),
            _ => new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x24))
        };
        SummaryBadge.BorderBrush = health.Overall switch
        {
            HealthSeverity.Critical => new SolidColorBrush(Color.FromRgb(0x6A, 0x2A, 0x3C)),
            HealthSeverity.Warning => new SolidColorBrush(Color.FromRgb(0x6A, 0x52, 0x1F)),
            _ => new SolidColorBrush(Color.FromRgb(0x1E, 0x5C, 0x4C))
        };

        var issues = health.Issues
            .OrderByDescending(i => i.Severity)
            .Select(i => new IssueView(
                Ui.SeverityLabel(i.Severity).ToUpperInvariant(),
                i.Title,
                i.Detail,
                i.FixHint,
                Ui.SeverityBrush(i.Severity)))
            .ToList();
        IssuesList.ItemsSource = issues;
        NoIssuesText.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool ready = AppServices.State.SelectedDevice is { Volume.IsReady: true } && !_busy;
        RepairButton.IsEnabled = ready;
        RemoveRockboxButton.IsEnabled = ready;
        FormatButton.IsEnabled = ready;
    }

    private void RunCheck_Click(object sender, RoutedEventArgs e) => RunCheck();

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        var device = AppServices.State.SelectedDevice;
        string? packagePath = AppServices.State.PackagePath;
        if (device is null || !device.Volume.IsReady)
        {
            Ui.Error("Connect the player and select its drive first.");
            return;
        }
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            Ui.Error("No firmware package is available. Open the Install page and select the bundled package.");
            return;
        }

        if (!Ui.Confirm(
                $"Repair {device.Volume.DriveLetter} · {device.Volume.Label}?\n\n" +
                "• Create missing System and Font folders\n" +
                "• Reinstall the original iRiver 1.03.01 firmware\n" +
                "• Create a safety backup first\n\n" +
                "Do not unplug the player while the operation is running.",
                "Repair player", dangerous: true))
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        ResultBox.Text = "Repairing…";

        try
        {
            using var package = FirmwarePackage.Open(packagePath);
            var result = await AppServices.Installer.InstallAsync(device, package, new InstallOptions
            {
                CreateBackup = true,
                IncludeRockboxInBackup = true,
                KeepPreviousFirmware = true,
                VerifyAfterWrite = true,
                AllowUnconfirmedDevice = true
            });

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(result.Success ? "Repair completed." : "Repair failed.");
            sb.AppendLine(result.Summary);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                sb.AppendLine("Error: " + result.Error);
            }
            foreach (var file in result.Files)
            {
                sb.AppendLine($"  wrote {file.Path} ({Humanize.Bytes(file.Size)})");
            }
            foreach (string warning in result.Warnings)
            {
                sb.AppendLine("  note: " + warning);
            }
            if (result.Backup is not null)
            {
                sb.AppendLine("Backup: " + result.Backup.FilePath);
            }
            ResultBox.Text = sb.ToString();

            await AppServices.State.RefreshAsync();
            MainWindow.Instance?.UpdateDeviceUi();
            RunCheck();

            if (result.Success)
            {
                Ui.Success(result.Summary + "\n\n" + (result.Backup is not null
                    ? "Backup: " + result.Backup.FileName + "\n\n"
                    : string.Empty) + "Safely eject the player and power it on.", "Repair complete");
            }
            else
            {
                Ui.Error(result.Error ?? "Repair failed.");
            }
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Repair failed.", ex.ToString());
            ResultBox.Text = ex.ToString();
            Ui.Error(ex);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async void RemoveRockbox_Click(object sender, RoutedEventArgs e)
    {
        var device = AppServices.State.SelectedDevice;
        if (device is null || !device.Volume.IsReady)
        {
            Ui.Error("Connect the player and select its drive first.");
            return;
        }
        if (!device.HasOfMi4 && !device.HasRockboxFolder)
        {
            Ui.Info("There are no Rockbox leftovers to remove.");
            return;
        }
        if (!Ui.Confirm("Remove System\\OF.mi4 and the .rockbox folder?\n\n" +
                        "The active firmware (System\\H10_20GC.mi4) is not touched. A backup is created first.",
                "Remove Rockbox leftovers", dangerous: true))
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            var backup = await AppServices.Backups.CreateBackupAsync(device, new BackupOptions
            {
                IncludeRockboxFolder = true,
                Reason = "Automatic backup before removing Rockbox leftovers"
            });

            await AppServices.Installer.RemoveRockboxDataAsync(device, deleteRockboxFolder: true, deleteOfMi4: true);
            await AppServices.State.RefreshAsync();
            MainWindow.Instance?.UpdateDeviceUi();
            RunCheck();

            ResultBox.Text = "Removed Rockbox leftovers.\nBackup: " + backup.FilePath;
            Ui.Success("Rockbox leftovers removed.\n\nBackup: " + backup.FileName, "Done");
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Could not remove Rockbox leftovers.", ex.ToString());
            ResultBox.Text = ex.ToString();
            Ui.Error(ex);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private async void Format_Click(object sender, RoutedEventArgs e)
    {
        var device = AppServices.State.SelectedDevice;
        if (device is null || !device.Volume.IsReady)
        {
            Ui.Error("Connect the player and select its drive first.");
            return;
        }

        int cluster = 16384;
        if (ClusterCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int parsed))
        {
            cluster = parsed;
        }
        string label = string.IsNullOrWhiteSpace(LabelBox.Text) ? "H10" : LabelBox.Text.Trim();

        if (!Ui.Confirm(
                $"FORMAT {device.Volume.DriveLetter} · {device.Volume.Label}\n\n" +
                $"File system: FAT32\nCluster size: {cluster:N0} bytes\nLabel: {label}\n" +
                $"Capacity: {Humanize.Bytes(device.Volume.TotalBytes)}\n\n" +
                "EVERYTHING ON THE PLAYER WILL BE ERASED. Firmware, music and backups stored on the player are lost.\n\n" +
                "After formatting, install the firmware again.",
                "Format player", dangerous: true))
        {
            return;
        }

        if (!Ui.Confirm("This is the last confirmation.\n\nFormat " + device.Volume.DriveLetter + " now?",
                "Confirm format", dangerous: true))
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        ResultBox.Text = "Waiting for administrator permission…";

        try
        {
            var progress = new Progress<string>(message => ResultBox.Text = message);
            var result = await AppServices.Format.FormatAsync(device, new FormatOptions
            {
                Label = label,
                ClusterSize = cluster,
                Quick = QuickFormatCheck.IsChecked == true
            }, progress);

            if (result.Success)
            {
                ResultBox.Text = result.Summary + "\n\n" +
                                 $"Label: {result.VolumeAfter?.Label}\n" +
                                 $"Cluster: {result.VolumeAfter?.ClusterSize:N0} bytes\n" +
                                 $"Capacity: {Humanize.Bytes(result.VolumeAfter?.TotalBytes ?? 0)}\n\n" +
                                 "Next: install the original firmware from the Install page.";
                await AppServices.State.RefreshAsync();
                MainWindow.Instance?.UpdateDeviceUi();
                RunCheck();
                Ui.Success(result.Summary + "\n\nThe player is empty. Install the firmware now.",
                    "Format complete");
            }
            else if (result.Canceled)
            {
                ResultBox.Text = result.Summary;
            }
            else
            {
                ResultBox.Text = result.Summary + "\n\n" + result.Output;
                Ui.Error(result.Summary + "\n\nSee the result panel for details.");
            }
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Format failed.", ex.ToString());
            ResultBox.Text = ex.ToString();
            Ui.Error(ex);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    private sealed record IssueView(
        string SeverityText,
        string Title,
        string Detail,
        string? FixHint,
        Brush BadgeBrush)
    {
        public Visibility HasHint => string.IsNullOrWhiteSpace(FixHint) ? Visibility.Collapsed : Visibility.Visible;
    }
}
