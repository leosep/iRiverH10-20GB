using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;
using H10FirmwareManager.Core.Services;
using System.IO;

namespace H10FirmwareManager.Pages;

public partial class RockboxPage : UserControl, IRefreshablePage
{
    private RockboxStatus? _status;
    private bool _running;

    public RockboxPage()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        var device = AppServices.State.SelectedDevice;
        _status = device is null ? null : AppServices.Rockbox.Detect(device);
        UpdateStatus();
        return Task.CompletedTask;
    }

    private void UpdateStatus()
    {
        ReplaceButton.IsEnabled = _status is { Device.Volume.IsReady: true } && !_running;

        if (_status is null)
        {
            StatusSummaryText.Text = "No player selected";
            StatusSummaryText.Foreground = (Brush)FindResource("MutedBrush");
            StatusDot.Fill = (Brush)FindResource("MutedBrush");
            StatusDetailText.Text = "Connect the player in Emergency Connect (UMS) mode and select its drive.";
            BootloaderText.Text = SavedOriginalText.Text = RockboxFolderText.Text = "—";
            RockboxFolderSizeText.Text = string.Empty;
            return;
        }

        StatusSummaryText.Text = _status.Summary;
        StatusSummaryText.Foreground = _status.BootloaderPresent
            ? (Brush)FindResource("WarningBrush")
            : _status.Detected
                ? (Brush)FindResource("AccentHoverBrush")
                : (Brush)FindResource("SuccessBrush");
        StatusDot.Fill = _status.BootloaderPresent
            ? (Brush)FindResource("WarningBrush")
            : _status.Detected
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("SuccessBrush");
        StatusBadge.Background = _status.BootloaderPresent
            ? new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x12))
            : _status.Detected
                ? new SolidColorBrush(Color.FromRgb(0x14, 0x22, 0x3C))
                : new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x24));
        StatusBadge.BorderBrush = _status.BootloaderPresent
            ? new SolidColorBrush(Color.FromRgb(0x6A, 0x52, 0x1F))
            : _status.Detected
                ? new SolidColorBrush(Color.FromRgb(0x33, 0x50, 0x8A))
                : new SolidColorBrush(Color.FromRgb(0x1E, 0x5C, 0x4C));
        StatusDetailText.Text = _status.Detail;

        BootloaderText.Text = _status.BootloaderPresent ? "Rockbox bootloader" : "Original firmware";
        BootloaderText.Foreground = _status.BootloaderPresent
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("SuccessBrush");

        SavedOriginalText.Text = _status.OriginalFirmwareSaved
            ? (_status.OriginalFirmwareSavedIsValid
                ? $"Valid {_status.SavedFirmwareVersion}"
                : "Present, not a valid mi4")
            : "Not present";
        SavedOriginalText.Foreground = _status.OriginalFirmwareSaved
            ? (_status.OriginalFirmwareSavedIsValid
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("WarningBrush"))
            : (Brush)FindResource("MutedBrush");

        RockboxFolderText.Text = _status.FolderPresent ? $"{_status.FolderFiles} files" : "Not present";
        RockboxFolderText.Foreground = _status.FolderPresent
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("MutedBrush");
        RockboxFolderSizeText.Text = _status.FolderPresent ? Humanize.Bytes(_status.FolderBytes) : string.Empty;
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.State.RefreshAsync();
        await RefreshAsync();
        MainWindow.Instance?.UpdateDeviceUi();
    }

    private async void Replace_Click(object sender, RoutedEventArgs e)
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
        if (_status is null || !_status.Detected)
        {
            if (!Ui.Confirm("Rockbox was not detected on this player. Install the original firmware anyway?",
                    "Rockbox not detected"))
            {
                return;
            }
        }

        string steps = "• Create a full safety backup\n" +
                       "• Replace System\\H10_20GC.mi4 with iRiver 1.03.01\n" +
                       (DeleteRockboxCheck.IsChecked == true ? "• Delete the .rockbox folder\n" : "") +
                       (DeleteOfCheck.IsChecked == true ? "• Delete System\\OF.mi4\n" : "");
        if (!Ui.Confirm($"Replace Rockbox on {device.Volume.DriveLetter} · {device.Volume.Label}?\n\n{steps}\n" +
                        "Do not unplug the player while the operation is running.",
                "Replace Rockbox", dangerous: true))
        {
            return;
        }

        _running = true;
        ReplaceButton.IsEnabled = false;
        ReplaceLogList.Items.Clear();
        ReplaceProgressBar.Value = 0;
        ProgressStageText.Text = "Starting";
        ProgressMessageText.Text = "Preparing…";

        try
        {
            using var package = FirmwarePackage.Open(packagePath);
            var options = new InstallOptions
            {
                CreateBackup = true,
                IncludeRockboxInBackup = true,
                KeepPreviousFirmware = true,
                VerifyAfterWrite = true,
                DeleteRockboxFolder = DeleteRockboxCheck.IsChecked == true,
                DeleteOfMi4 = DeleteOfCheck.IsChecked == true,
                AllowUnconfirmedDevice = true
            };

            var progress = new Progress<InstallProgress>(p =>
            {
                ReplaceProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
                ProgressStageText.Text = p.Stage;
                ProgressMessageText.Text = p.Message;
                AddLog(p.Level, p.Message);
            });

            var result = await AppServices.Installer.InstallAsync(device, package, options, progress);
            if (result.Success)
            {
                ProgressStageText.Text = "Complete";
                ProgressMessageText.Text = "Original firmware restored.";
                AddLog(LogLevel.Success, result.Summary);
                if (result.Backup is not null)
                {
                    AddLog(LogLevel.Info, "Backup: " + result.Backup.FilePath);
                }
                foreach (string warning in result.Warnings)
                {
                    AddLog(LogLevel.Warning, warning);
                }

                await AppServices.State.RefreshAsync();
                MainWindow.Instance?.UpdateDeviceUi();
                _status = AppServices.State.SelectedDevice is { } refreshed
                    ? AppServices.Rockbox.Detect(refreshed)
                    : null;
                UpdateStatus();

                Ui.Success(result.Summary + "\n\nSafely eject the player, unplug USB and power it on.",
                    "Rockbox replaced");
            }
            else
            {
                ProgressStageText.Text = "Failed";
                ProgressMessageText.Text = result.Error ?? "Unknown error";
                AddLog(LogLevel.Error, result.Error ?? "Replacement failed.");
                Ui.Error(result.Error ?? "Replacement failed.");
            }
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Rockbox replacement failed.", ex.ToString());
            AddLog(LogLevel.Error, ex.Message);
            Ui.Error(ex);
        }
        finally
        {
            _running = false;
            UpdateStatus();
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var lines = ReplaceLogList.Items.OfType<ListBoxItem>()
            .Select(i => (i.Content as TextBlock)?.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        Ui.Copy(string.Join(Environment.NewLine, lines));
    }

    private void AddLog(LogLevel level, string message)
    {
        Brush brush = level switch
        {
            LogLevel.Success => (Brush)FindResource("SuccessBrush"),
            LogLevel.Warning => (Brush)FindResource("WarningBrush"),
            LogLevel.Error => (Brush)FindResource("DangerBrush"),
            LogLevel.Debug => (Brush)FindResource("MutedBrush"),
            _ => (Brush)FindResource("TextBrush")
        };
        var text = new TextBlock
        {
            Text = $"{DateTime.Now:HH:mm:ss}  {message}",
            Foreground = brush,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2, 4, 2)
        };
        var item = new ListBoxItem { Content = text };
        ReplaceLogList.Items.Add(item);
        ReplaceLogList.ScrollIntoView(item);
    }
}
