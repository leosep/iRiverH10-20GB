using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H10FirmwareManager.Core.Devices;
using H10FirmwareManager.Pages;

namespace H10FirmwareManager;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = [];
    private readonly string? _startPage;
    private readonly Dictionary<string, (string Title, string Subtitle)> _meta = new()
    {
        ["dashboard"] = ("Dashboard", "Player status, firmware and quick actions."),
        ["install"] = ("Install firmware", "Write the original iRiver 1.03.01 firmware to the player."),
        ["backups"] = ("Backups", "Create, verify and restore complete system backups."),
        ["rockbox"] = ("Rockbox", "Detect and replace the Rockbox bootloader with the original firmware."),
        ["analyze"] = ("Firmware analysis", "Inspect mi4 headers, CRC-32, TEA encryption and embedded strings."),
        ["repair"] = ("Repair & format", "Health checks, structure repair and FAT32 formatting."),
        ["log"] = ("Activity log", "Everything the app did, including hashes and errors."),
        ["settings"] = ("Settings & about", "Paths, defaults and legal information.")
    };

    private string _currentPage = "dashboard";
    private bool _ready;

    public MainWindow(string? startPage = null)
    {
        InitializeComponent();
        Instance = this;
        _startPage = startPage;

        // Keep the window inside the usable desktop area (small laptops and 125% scaling).
        var area = SystemParameters.WorkArea;
        MaxWidth = area.Width;
        MaxHeight = area.Height;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 20));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 20));

        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        Loaded += MainWindow_Loaded;
    }

    public static MainWindow? Instance { get; private set; }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppServices.State.RefreshAsync();
            UpdateDeviceUi();
            _ready = true;

            string start = _startPage is not null && _meta.ContainsKey(_startPage) ? _startPage : "dashboard";
            var startButton = FindNavButton(start);
            if (startButton is not null && startButton.IsChecked != true)
            {
                startButton.IsChecked = true;
            }
            else
            {
                await ShowPageAsync(start);
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
    }

    public void NavigateTo(string key)
    {
        if (!_meta.ContainsKey(key))
        {
            return;
        }

        var button = FindNavButton(key);
        if (button is not null)
        {
            button.IsChecked = true;
        }
        else
        {
            _ = ShowPageAsync(key);
        }
    }

    private RadioButton? FindNavButton(string key)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(this))
        {
            if (FindInTree(child, key) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static RadioButton? FindInTree(object node, string key)
    {
        if (node is RadioButton radio && string.Equals(radio.Tag as string, key, StringComparison.OrdinalIgnoreCase))
        {
            return radio;
        }
        if (node is DependencyObject dependencyObject)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
            {
                if (FindInTree(child, key) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string key })
        {
            return;
        }
        await ShowPageAsync(key);
    }

    private async Task ShowPageAsync(string key)
    {
        if (!_meta.TryGetValue(key, out var meta))
        {
            return;
        }

        _currentPage = key;
        PageTitle.Text = meta.Title;
        PageSubtitle.Text = meta.Subtitle;

        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "dashboard" => new DashboardPage(),
                "install" => new InstallPage(),
                "backups" => new BackupsPage(),
                "rockbox" => new RockboxPage(),
                "analyze" => new AnalyzePage(),
                "repair" => new RepairPage(),
                "log" => new LogPage(),
                "settings" => new SettingsPage(),
                _ => new DashboardPage()
            };
            _pages[key] = page;
        }

        PageHost.Content = page;
        if (page is IRefreshablePage refreshable)
        {
            try
            {
                await refreshable.RefreshAsync();
            }
            catch (Exception ex)
            {
                AppServices.Log.Error($"Could not refresh page '{key}'.", ex.ToString());
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await AppServices.State.RefreshAsync();
            UpdateDeviceUi();
            if (_pages.TryGetValue(_currentPage, out var page) && page is IRefreshablePage refreshable)
            {
                await refreshable.RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            Ui.Error(ex);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void DeviceCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        NavigateTo("dashboard");
    }

    public void UpdateDeviceUi()
    {
        var device = AppServices.State.SelectedDevice;
        if (device is null)
        {
            HeaderDeviceText.Text = "No player detected";
            SideDeviceText.Text = "No player detected";
            SideStatusText.Text = "Connect the H10 in USB mode";
            SideFirmwareText.Text = "Read the guide for Emergency Connect";
            HeaderDot.Fill = SideDot.Fill = (Brush)FindResource("MutedBrush");
            return;
        }

        HeaderDeviceText.Text = $"{device.Volume.DriveLetter} · {device.Volume.Label}";
        SideDeviceText.Text = $"{device.Volume.DriveLetter} · {device.Volume.Label}";
        SideStatusText.Text = device.StatusText;
        SideFirmwareText.Text = $"Firmware {device.FirmwareVersion}";
        var brush = device.StatusText == "Ready"
            ? (Brush)FindResource("SuccessBrush")
            : device.StatusText is "No firmware" or "System folder missing" or "Firmware CRC invalid"
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("WarningBrush");
        HeaderDot.Fill = brush;
        SideDot.Fill = brush;
    }
}
