using System.Windows;
using System.Windows.Controls;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Core.Services;

namespace H10FirmwareManager.Pages;

public partial class BackupsPage : UserControl, IRefreshablePage
{
    private bool _busy;

    public BackupsPage()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        var device = AppServices.State.SelectedDevice;
        BackupTargetText.Text = device is null
            ? "No player selected"
            : $"Target: {device.Volume.DriveLetter} · {device.Volume.Label}";
        LoadBackups();
        return Task.CompletedTask;
    }

    private void LoadBackups()
    {
        string? selectedPath = (BackupsList.SelectedItem as BackupInfo)?.FilePath;
        var backups = AppServices.Backups.ListBackups();
        BackupsList.ItemsSource = backups;
        EmptyPanel.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (selectedPath is not null)
        {
            BackupsList.SelectedItem = backups.FirstOrDefault(b => b.FilePath == selectedPath);
        }
        if (BackupsList.SelectedItem is null && backups.Count > 0)
        {
            BackupsList.SelectedIndex = 0;
        }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        bool hasSelection = BackupsList.SelectedItem is BackupInfo { IsValid: true };
        RestoreButton.IsEnabled = hasSelection && !_busy;
        VerifyButton.IsEnabled = hasSelection && !_busy;
        DeleteButton.IsEnabled = BackupsList.SelectedItem is BackupInfo && !_busy;

        if (BackupsList.SelectedItem is not BackupInfo backup)
        {
            SelectedNameText.Text = "Select a backup";
            SelectedCreatedText.Text = SelectedDeviceText.Text = "—";
            SelectedFirmwareText.Text = SelectedContentText.Text = "—";
            SelectedRockboxText.Text = "—";
            SelectedPathText.Text = "—";
            return;
        }

        SelectedNameText.Text = backup.FileName;
        SelectedCreatedText.Text = backup.CreatedText;
        SelectedDeviceText.Text = string.IsNullOrWhiteSpace(backup.DeviceLabel) ? "—" : backup.DeviceLabel;
        SelectedFirmwareText.Text = string.IsNullOrWhiteSpace(backup.FirmwareVersion) ? "—" : backup.FirmwareVersion;
        SelectedContentText.Text = backup.IsValid ? backup.ContentText : backup.Error ?? "Invalid backup";
        SelectedRockboxText.Text = backup.RockboxInstalled ? "Installed at backup time" : "Not installed";
        SelectedPathText.Text = backup.FilePath;
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        var device = AppServices.State.SelectedDevice;
        if (device is null)
        {
            Ui.Error("Connect the player and select its drive first.");
            return;
        }
        if (!device.Volume.IsReady)
        {
            Ui.Error("The player volume is not ready.");
            return;
        }

        _busy = true;
        ResetButtons();
        ProgressPanel.Visibility = Visibility.Visible;
        BackupProgressBar.Value = 0;
        ProgressStageText.Text = "Starting";
        ProgressPercentText.Text = "0%";
        ProgressFileText.Text = string.Empty;

        try
        {
            var progress = new Progress<BackupProgress>(p =>
            {
                BackupProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
                ProgressStageText.Text = p.Stage;
                ProgressPercentText.Text = $"{p.Percent}%";
                ProgressFileText.Text = p.CurrentFile ?? string.Empty;
            });

            var backup = await AppServices.Backups.CreateBackupAsync(device, new BackupOptions
            {
                IncludeRockboxFolder = IncludeRockboxCheck.IsChecked == true,
                IncludeMusicDatabase = true,
                Reason = "Manual backup"
            }, progress);

            LoadBackups();
            BackupsList.SelectedItem = ((IEnumerable<BackupInfo>)BackupsList.ItemsSource)
                .FirstOrDefault(b => b.FilePath == backup.FilePath);
            Ui.Success($"Backup created:\n\n{backup.FilePath}\n\n{backup.FileCount} files · {backup.SizeText}", "Backup complete");
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Backup failed.", ex.ToString());
            Ui.Error(ex);
        }
        finally
        {
            _busy = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
            UpdateSelection();
        }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupInfo backup)
        {
            return;
        }

        _busy = true;
        ResetButtons();
        ProgressPanel.Visibility = Visibility.Visible;
        BackupProgressBar.Value = 0;
        ProgressStageText.Text = "Verifying backup";
        ProgressPercentText.Text = "0%";
        ProgressFileText.Text = backup.FileName;

        try
        {
            var progress = new Progress<BackupProgress>(p =>
            {
                BackupProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
                ProgressStageText.Text = p.Stage;
                ProgressPercentText.Text = $"{p.Percent}%";
                ProgressFileText.Text = p.CurrentFile ?? string.Empty;
            });

            var result = await AppServices.Backups.VerifyBackupAsync(backup.FilePath, progress);
            if (result.IsValid)
            {
                Ui.Success($"The backup is intact.\n\n{result.FilesChecked} files verified against their SHA-256 hashes.", "Backup verified");
            }
            else
            {
                Ui.Error("The backup has problems:\n\n" + string.Join("\n", result.Problems.Take(15)), "Backup verification failed");
            }
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Backup verification failed.", ex.ToString());
            Ui.Error(ex);
        }
        finally
        {
            _busy = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
            UpdateSelection();
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupInfo backup)
        {
            return;
        }
        var device = AppServices.State.SelectedDevice;
        if (device is null || !device.Volume.IsReady)
        {
            Ui.Error("Connect the player and select its drive before restoring.");
            return;
        }

        string warning = $"Restore {backup.FileName} to {device.Volume.DriveLetter} · {device.Volume.Label}?\n\n" +
                         $"This writes {backup.FileCount} files ({Humanize.Bytes(backup.TotalBytes)}) and overwrites matching files on the player.";
        if (!Ui.Confirm(warning, "Restore backup", dangerous: true))
        {
            return;
        }

        _busy = true;
        ResetButtons();
        ProgressPanel.Visibility = Visibility.Visible;
        BackupProgressBar.Value = 0;
        ProgressStageText.Text = "Starting";
        ProgressPercentText.Text = "0%";
        ProgressFileText.Text = string.Empty;

        try
        {
            // A restore is itself a destructive operation: keep the current state first unless
            // the user disabled it explicitly.
            if (SafetyBackupCheck.IsChecked == true)
            {
                ProgressStageText.Text = "Creating safety backup";
                var safety = await AppServices.Backups.CreateBackupAsync(device, new BackupOptions
                {
                    IncludeRockboxFolder = true,
                    Reason = "Automatic backup before restoring " + backup.FileName
                });
                AppServices.Log.Info("Safety backup created before restore.", safety.FilePath);
            }

            var progress = new Progress<BackupProgress>(p =>
            {
                BackupProgressBar.Value = Math.Clamp(p.Percent, 0, 100);
                ProgressStageText.Text = p.Stage;
                ProgressPercentText.Text = $"{p.Percent}%";
                ProgressFileText.Text = p.CurrentFile ?? string.Empty;
            });

            int restored = await AppServices.Backups.RestoreAsync(backup, device, new RestoreOptions
            {
                OverwriteExisting = true,
                IncludeRockboxFolder = true
            }, progress);

            await AppServices.State.RefreshAsync();
            MainWindow.Instance?.UpdateDeviceUi();
            LoadBackups();
            Ui.Success($"{restored} files restored to {device.Volume.DriveLetter}.\n\n" +
                       "Safely eject the player and power it on to verify the firmware.", "Restore complete");
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Restore failed.", ex.ToString());
            Ui.Error(ex);
        }
        finally
        {
            _busy = false;
            ProgressPanel.Visibility = Visibility.Collapsed;
            UpdateSelection();
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupInfo backup)
        {
            return;
        }
        if (!Ui.Confirm($"Delete {backup.FileName}?\n\nThe ZIP file is removed from disk. This cannot be undone.",
                "Delete backup", dangerous: true))
        {
            return;
        }

        try
        {
            AppServices.Backups.DeleteBackup(backup.FilePath);
            LoadBackups();
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.State.RefreshAsync();
        await RefreshAsync();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Ui.OpenFolder(AppServices.Backups.BackupRoot);

    private void BackupsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

    private void ResetButtons()
    {
        RestoreButton.IsEnabled = VerifyButton.IsEnabled = DeleteButton.IsEnabled = false;
    }
}
