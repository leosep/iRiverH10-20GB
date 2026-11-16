using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using H10FirmwareManager.Core;
using H10FirmwareManager.Core.Firmware;
using Microsoft.Win32;
using System.IO;

namespace H10FirmwareManager.Pages;

public partial class AnalyzePage : UserControl, IRefreshablePage
{
    private FirmwareImage? _image;
    private bool _initialized;
    private bool _autoAnalyzed;

    public AnalyzePage()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        if (!_initialized)
        {
            SourceCombo.SelectedIndex = 0;
            _initialized = true;
        }
        ResolveSource();

        // Analyze the player firmware once so the page is useful immediately.
        if (!_autoAnalyzed && AppServices.State.SelectedDevice?.FirmwarePath is { Length: > 0 } path
            && File.Exists(path))
        {
            _autoAnalyzed = true;
            await AnalyzePathAsync(path);
        }
    }

    private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        ResolveSource();
    }

    private void ResolveSource()
    {
        string? tag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        switch (tag)
        {
            case "device":
                var device = AppServices.State.SelectedDevice;
                string path = device?.FirmwarePath ?? string.Empty;
                SourcePathBox.Text = string.IsNullOrEmpty(path) ? "No player firmware found" : path;
                break;
            case "package":
                SourcePathBox.Text = AppServices.State.PackagePath ?? "No package selected";
                break;
            case "file":
                SourcePathBox.Text = "Choose a .mi4 file to analyze";
                break;
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        string? path = null;
        string? tag = (SourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        try
        {
            switch (tag)
            {
                case "device":
                    path = AppServices.State.SelectedDevice?.FirmwarePath;
                    break;
                case "package":
                    path = await ExtractPackageImageAsync();
                    break;
                case "file":
                    var dialog = new OpenFileDialog
                    {
                        Title = "Select an mi4 firmware image",
                        Filter = "mi4 firmware (*.mi4)|*.mi4|All files (*.*)|*.*",
                        CheckFileExists = true
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        path = dialog.FileName;
                        SourcePathBox.Text = path;
                    }
                    break;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Ui.Error("The selected source does not contain a readable file.");
                return;
            }

            await AnalyzePathAsync(path);
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("Firmware analysis failed.", ex.ToString());
            Ui.Error(ex);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task AnalyzePathAsync(string path)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var image = await Task.Run(() => FirmwareImage.Load(path));
            _image = image;
            ShowImage(image);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private async Task<string?> ExtractPackageImageAsync()
    {
        string? packagePath = AppServices.State.PackagePath;
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            return null;
        }

        using var package = FirmwarePackage.Open(packagePath);
        var entry = package.GetOperatingSystemEntry();
        if (entry is null)
        {
            return null;
        }

        string dir = Path.Combine(Path.GetTempPath(), "h10-analyze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Path.GetFileName(entry.ArchivePath));
        await package.ExtractEntryAsync(entry, path);
        SourcePathBox.Text = $"{packagePath} → {entry.ArchivePath}";
        return path;
    }

    private void ShowImage(FirmwareImage image)
    {
        FileText.Text = Path.GetFileName(image.Path);
        FileText.ToolTip = image.Path;
        KindText.Text = image.KindText;
        SizeText.Text = $"{image.Size:N0} bytes ({Humanize.Bytes(image.Size)})";
        VersionText.Text = image.VersionText;
        StatusText.Text = image.StatusText;
        StatusText.Foreground = image.IsRockbox
            ? (Brush)FindResource("WarningBrush")
            : image is { IsMi4: true, CrcValid: true }
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush");

        if (image.Header is { } header)
        {
            MagicText.Text = $"{header.Magic} ({(header.MagicValid ? "valid" : "invalid")})";
            CryptoText.Text = $"{header.CryptoText} · id {header.CryptoType}";
            KeyText.Text = image.Key is null
                ? (image.Header.CryptoType == 2 ? "not identified" : "not encrypted")
                : $"{image.Key.Name} · {image.Key.Hex}";
            StoredCrcText.Text = $"0x{header.StoredCrc32:X8}";
            ComputedCrcText.Text = image.ComputedCrc32 is null
                ? "—"
                : $"0x{image.ComputedCrc32:X8} ({(image.CrcValid ? "valid" : "INVALID")})";
            ComputedCrcText.Foreground = image.CrcValid
                ? (Brush)FindResource("SuccessBrush")
                : (Brush)FindResource("DangerBrush");
            LengthsText.Text = $"data {header.DataLength:N0} · plain {header.PlaintextLength:N0} · total {header.TotalLength:N0}";
            MagicOffsetText.Text = image.MagicOffset is null ? "—" : $"0x{image.MagicOffset:X}";
        }
        else
        {
            MagicText.Text = "No mi4 header";
            CryptoText.Text = KeyText.Text = "—";
            StoredCrcText.Text = ComputedCrcText.Text = LengthsText.Text = MagicOffsetText.Text = "—";
            ComputedCrcText.Foreground = (Brush)FindResource("MutedBrush");
        }

        Sha256Text.Text = string.IsNullOrEmpty(image.Sha256) ? "—" : image.Sha256;
        Md5Text.Text = string.IsNullOrEmpty(image.Md5) ? "—" : image.Md5;

        BuildReport();
    }

    private void BuildReport()
    {
        if (_image is null)
        {
            ReportBox.Text = "Select a source and press Analyze.";
            return;
        }

        var sb = new StringBuilder();
        sb.Append(_image.ToReport());
        if (StringsCheck.IsChecked == true)
        {
            var strings = _image.ExtractStrings();
            sb.AppendLine();
            sb.AppendLine($"Embedded strings ({strings.Count}):");
            sb.AppendLine("-----------------------------");
            foreach (string value in strings)
            {
                sb.AppendLine(value);
            }
        }
        ReportBox.Text = sb.ToString();
        ReportBox.ScrollToHome();
    }

    private void StringsCheck_Click(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            BuildReport();
        }
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ReportBox.Text))
        {
            Ui.Copy(ReportBox.Text);
        }
    }

    private void SaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_image is null)
        {
            Ui.Error("Analyze an image first.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save firmware analysis",
            Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = $"H10-analysis-{_image.VersionText.Replace('.', '_')}.txt"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, ReportBox.Text, new UTF8Encoding(false));
            Ui.Success("Report saved:\n\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }
}
