using System.Windows;
using System.Windows.Controls;
using H10FirmwareManager.Core.Services;
using Microsoft.Win32;

namespace H10FirmwareManager.Pages;

public partial class SettingsPage : UserControl, IRefreshablePage
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public Task RefreshAsync()
    {
        var settings = AppServices.Settings.Settings;
        PackageBox.Text = AppServices.State.PackagePath ?? "No package selected";
        BackupBox.Text = AppServices.Settings.BackupDirectory;
        DataBox.Text = AppServices.Settings.DataDirectory;
        AboutVersionText.Text = $"H10 Firmware Manager {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

        BackupBeforeInstallCheck.IsChecked = settings.CreateBackupBeforeInstall;
        KeepPreviousCheck.IsChecked = settings.KeepPreviousFirmware;
        VerifyCheck.IsChecked = settings.VerifyAfterWrite;
        DeleteRockboxCheck.IsChecked = settings.DeleteRockboxDataByDefault;
        ConfirmFormatCheck.IsChecked = settings.ConfirmBeforeFormat;
        return Task.CompletedTask;
    }

    private void ChangePackage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a firmware package",
            Filter = "Firmware package (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            AppServices.State.SetPackagePath(dialog.FileName);
            PackageBox.Text = dialog.FileName;
        }
    }

    private void ChangeBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the backup folder",
            InitialDirectory = AppServices.Settings.BackupDirectory
        };
        if (dialog.ShowDialog() == true)
        {
            AppServices.Settings.Settings.BackupDirectory = dialog.FolderName;
            AppServices.Settings.Save();
            BackupBox.Text = AppServices.Settings.BackupDirectory;
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => Ui.OpenFolder(AppServices.Settings.DataDirectory);

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Ui.OpenFolder(AppServices.Log.LogDirectory);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppServices.Settings.Settings;
        settings.CreateBackupBeforeInstall = BackupBeforeInstallCheck.IsChecked == true;
        settings.KeepPreviousFirmware = KeepPreviousCheck.IsChecked == true;
        settings.VerifyAfterWrite = VerifyCheck.IsChecked == true;
        settings.DeleteRockboxDataByDefault = DeleteRockboxCheck.IsChecked == true;
        settings.ConfirmBeforeFormat = ConfirmFormatCheck.IsChecked == true;

        try
        {
            AppServices.Settings.Save();
            AppServices.Log.Info("Settings saved.");
            Ui.Success("Settings saved.");
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }
}
