using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using H10FirmwareManager.Core.Devices;
using System.IO;

namespace H10FirmwareManager;

public static class Ui
{
    public static void Info(string message, string title = "H10 Firmware Manager") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Success(string message, string title = "H10 Firmware Manager") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string message, string title = "H10 Firmware Manager") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Error(Exception exception, string title = "H10 Firmware Manager")
    {
        AppServices.Log.Error("UI error: " + exception.Message, exception.ToString());
        MessageBox.Show(exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static bool Confirm(string message, string title = "H10 Firmware Manager", bool dangerous = false) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel,
            dangerous ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.OK;

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    public static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch { }
    }

    public static Brush SeverityBrush(HealthSeverity severity) => severity switch
    {
        HealthSeverity.Critical => (Brush)Application.Current.Resources["DangerBrush"],
        HealthSeverity.Warning => (Brush)Application.Current.Resources["WarningBrush"],
        _ => (Brush)Application.Current.Resources["SuccessBrush"]
    };

    public static string SeverityLabel(HealthSeverity severity) => severity switch
    {
        HealthSeverity.Critical => "Critical",
        HealthSeverity.Warning => "Warning",
        _ => "OK"
    };

    public static string DeviceSummary(H10Device? device)
    {
        if (device is null)
        {
            return "No player detected";
        }
        return $"{device.Volume.DriveLetter} · {device.Volume.Label}";
    }

    public static string DeviceDetail(H10Device? device)
    {
        if (device is null)
        {
            return "Connect the H10 in Emergency Connect (UMS) mode.";
        }
        return $"{device.StatusText} · firmware {device.FirmwareVersion}";
    }
}
