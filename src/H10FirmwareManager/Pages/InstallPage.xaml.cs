using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Firmware;
using H10FirmwareManager.Core.Services;
using Microsoft.Win32;
using System.IO;

namespace H10FirmwareManager.Pages;

public partial class InstallPage : UserControl, IRefreshablePage
{
    private bool _updating;
    private bool _running;
    private string? _packageVersion;

    public InstallPage()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        PopulateTargets();
        LoadPackageInfo();
        UpdateSummary();
        return Task.CompletedTask;
    }

    private void PopulateTargets()
    {
        _updating = true;
        TargetCombo.ItemsSource = AppServices.State.Devices;
        TargetCombo.SelectedItem = AppServices.State.SelectedDevice ?? AppServices.State.Devices.FirstOrDefault();
        _updating = false;
        UpdatePlayerInfo();
    }

    private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || TargetCombo.SelectedItem is not H10Device device)
        {
            return;
        }
        AppServices.State.SelectDevice(device);
        MainWindow.Instance?.UpdateDeviceUi();
        UpdatePlayerInfo();
        UpdateSummary();
    }

    private void UpdatePlayerInfo()
    {
        var device = TargetCombo.SelectedItem as H10Device ?? AppServices.State.SelectedDevice;
        if (device is null)
        {
            CurrentFirmwareText.Text = "No player selected";
            CurrentFirmwareText.Foreground = (Brush)FindResource("MutedBrush");
            DeviceHintText.Text = "Connect the H10 in Emergency Connect (UMS) mode and press Rescan in the header.";
            return;
        }

        CurrentFirmwareText.Text = $"Current firmware: {device.FirmwareStateText} · {device.Volume.DriveLetter}";
        CurrentFirmwareText.Foreground = device.Firmware is { IsRockbox: true }
            ? (Brush)FindResource("WarningBrush")
            : device.Firmware is null or { CrcChecked: true, CrcValid: false }
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("SuccessBrush");
        DeviceHintText.Text = $"{device.Volume.FileSystem}, {Humanize.Bytes(device.Volume.TotalBytes)} total, " +
                              $"{Humanize.Bytes(device.Volume.FreeBytes)} free · {device.StatusText}";
    }

    private void LoadPackageInfo()
    {
        string? path = AppServices.State.PackagePath;
        PackagePathBox.Text = path ?? "No package selected";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _packageVersion = null;
            PackageNameText.Text = "Package not found";
            PackageVersionText.Text = "Choose the bundled firmware ZIP.";
            EntriesList.ItemsSource = null;
            return;
        }

        try
        {
            using var package = FirmwarePackage.Open(path);
            _packageVersion = package.Manifest.FirmwareVersion;
            PackageNameText.Text = package.DisplayName;
            PackageVersionText.Text = $"Firmware {package.Manifest.FirmwareVersion} · {package.Manifest.Target}";
            EntriesList.ItemsSource = package.Entries
                .Where(e => !e.IsDocument)
                .Select(e => new PackageEntryView(
                    e.Name,
                    string.IsNullOrWhiteSpace(e.DevicePath) ? e.ArchivePath : e.DevicePath,
                    Humanize.Bytes(e.Size),
                    e.IsRecovery))
                .ToList();
        }
        catch (Exception ex)
        {
            _packageVersion = null;
            PackageNameText.Text = "Invalid package";
            PackageVersionText.Text = ex.Message;
            EntriesList.ItemsSource = null;
            AppServices.Log.Error("Could not read firmware package.", ex.ToString());
        }
    }

    private void UpdateSummary()
    {
        var device = TargetCombo.SelectedItem as H10Device ?? AppServices.State.SelectedDevice;
        bool hasPackage = !string.IsNullOrWhiteSpace(AppServices.State.PackagePath) &&
                          File.Exists(AppServices.State.PackagePath) &&
                          _packageVersion is not null;

        if (device is null)
        {
            SummaryText.Text = "Select a player to continue.";
        }
        else if (!hasPackage)
        {
            SummaryText.Text = "Load a valid firmware package to continue.";
        }
        else
        {
            string backup = CreateBackupCheck.IsChecked == true ? "a safety backup will be created" : "no backup will be created";
            SummaryText.Text = $"{device.Volume.DriveLetter} · {device.Volume.Label} — original firmware {_packageVersion}. " +
                               $"The font will {(IncludeFontCheck.IsChecked == true ? "also be installed" : "not be changed")}; {backup}.";
        }

        InstallButton.IsEnabled = !_running && device is not null && hasPackage;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var device = TargetCombo.SelectedItem as H10Device ?? AppServices.State.SelectedDevice;
        string? path = AppServices.State.PackagePath;
        if (device is null || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Ui.Error("Select a player and a valid firmware package first.");
            return;
        }

        string operations = "• Replace System\\H10_20GC.mi4 with the original iRiver firmware\n" +
                            (IncludeFontCheck.IsChecked == true ? "• Install System\\Font\\cjk12.fnt\n" : "") +
                            (CreateBackupCheck.IsChecked == true ? "• Create a full backup first\n" : "") +
                            (DeleteRockboxFolderCheck.IsChecked == true ? "• Delete the .rockbox folder\n" : "") +
                            (DeleteOfCheck.IsChecked == true ? "• Delete System\\OF.mi4\n" : "");
        if (!Ui.Confirm($"Install original firmware on {device.Volume.DriveLetter}?\n\n{operations}\n" +
                        "Existing firmware will be replaced. Do not unplug the player while the operation is running.",
                        "Install firmware", dangerous: true))
        {
            return;
        }

        _running = true;
        InstallButton.IsEnabled = false;
        InstallLogList.Items.Clear();
        InstallProgressBar.Value = 0;
        ProgressStageText.Text = "Starting";
        ProgressMessageText.Text = "Preparing…";
        AddLog(LogLevel.Info, $"Installing {path} on {device.Volume.DriveLetter}.");

        try
        {
            using var package = FirmwarePackage.Open(path);
            var entryIds = new List<string>();
            var osEntry = package.GetOperatingSystemEntry()
                ?? throw new InvalidDataException("The package does not contain an operating system image.");
            entryIds.Add(osEntry.Id);
            if (IncludeFontCheck.IsChecked == true)
            {
                entryIds.AddRange(package.Entries.Where(e => e.IsFont).Select(e => e.Id));
            }

            var options = new InstallOptions
            {
                EntryIds = entryIds,
                CreateBackup = CreateBackupCheck.IsChecked == true,
                IncludeRockboxInBackup = true,
                KeepPreviousFirmware = KeepPreviousCheck.IsChecked == true,
                VerifyAfterWrite = VerifyCheck.IsChecked == true,
                DeleteRockboxFolder = DeleteRockboxFolderCheck.IsChecked == true,
                DeleteOfMi4 = DeleteOfCheck.IsChecked == true,
                AllowUnconfirmedDevice = AllowOverrideCheck.IsChecked == true
            };

            var progress = new Progress<InstallProgress>(p =>
            {
                InstallProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
                ProgressStageText.Text = p.Stage;
                ProgressMessageText.Text = p.Message;
                AddLog(p.Level, p.Message);
            });

            var result = await AppServices.Installer.InstallAsync(device, package, options, progress);
            if (result.Success)
            {
                ProgressStageText.Text = "Complete";
                ProgressMessageText.Text = "Firmware installed and verified.";
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
                PopulateTargets();
                UpdateSummary();

                string message = result.Summary + "\n\n" +
                                 (result.Backup is not null ? $"Backup: {result.Backup.FileName}\n\n" : "") +
                                 "Safely eject the player, unplug USB and power it on.";
                Ui.Success(message, "Firmware installed");
            }
            else
            {
                ProgressStageText.Text = "Failed";
                ProgressMessageText.Text = result.Error ?? "Unknown error";
                AddLog(LogLevel.Error, result.Error ?? "Installation failed.");
                Ui.Error(result.Error ?? "Installation failed.");
            }
        }
        catch (OperationCanceledException)
        {
            ProgressStageText.Text = "Canceled";
            AddLog(LogLevel.Warning, "Installation canceled.");
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Install failed.", ex.ToString());
            AddLog(LogLevel.Error, ex.Message);
            Ui.Error(ex);
        }
        finally
        {
            _running = false;
            UpdateSummary();
        }
    }

    private void BrowsePackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a firmware package",
            Filter = "Firmware package (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true
        };
        string? current = AppServices.State.PackagePath;
        if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }
        if (dialog.ShowDialog() == true)
        {
            AppServices.State.SetPackagePath(dialog.FileName);
            LoadPackageInfo();
            UpdateSummary();
        }
    }

    private void ReloadPackage_Click(object sender, RoutedEventArgs e)
    {
        LoadPackageInfo();
        UpdateSummary();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var lines = InstallLogList.Items.OfType<ListBoxItem>()
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
        InstallLogList.Items.Add(item);
        InstallLogList.ScrollIntoView(item);
    }

    private sealed record PackageEntryView(string Name, string DevicePath, string SizeText, bool IsRecovery);
}
