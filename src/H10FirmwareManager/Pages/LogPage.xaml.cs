using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H10FirmwareManager.Core.Services;

namespace H10FirmwareManager.Pages;

public partial class LogPage : UserControl, IRefreshablePage
{
    private readonly ObservableCollection<LogRow> _rows = [];

    public LogPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _rows;
        AppServices.Log.EntryWritten += OnEntryWritten;
        Unloaded += (_, _) => AppServices.Log.EntryWritten -= OnEntryWritten;
    }

    public Task RefreshAsync()
    {
        Reload();
        return Task.CompletedTask;
    }

    private void OnEntryWritten(object? sender, LogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnEntryWritten(sender, entry));
            return;
        }
        if (Matches(entry))
        {
            _rows.Add(ToRow(entry));
            TrimRows();
            UpdateCount();
            if (AutoScrollCheck.IsChecked == true && _rows.Count > 0)
            {
                LogList.ScrollIntoView(_rows[^1]);
            }
        }
    }

    private void Reload()
    {
        _rows.Clear();
        foreach (var entry in AppServices.Log.Snapshot().Where(Matches))
        {
            _rows.Add(ToRow(entry));
        }
        UpdateCount();
        if (_rows.Count > 0)
        {
            LogList.ScrollIntoView(_rows[^1]);
        }
    }

    private bool Matches(LogEntry entry)
    {
        string mode = (FilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        return mode switch
        {
            "info" => entry.Level != LogLevel.Debug,
            "warning" => entry.Level is LogLevel.Warning or LogLevel.Error,
            "error" => entry.Level == LogLevel.Error,
            _ => true
        };
    }

    private LogRow ToRow(LogEntry entry) => new(
        entry.TimeText,
        entry.Level.ToString().ToUpperInvariant(),
        LevelBrush(entry.Level),
        entry.Message,
        entry.Detail ?? string.Empty,
        string.IsNullOrWhiteSpace(entry.Detail) ? Visibility.Collapsed : Visibility.Visible);

    private Brush LevelBrush(LogLevel level) => level switch
    {
        LogLevel.Success => (Brush)FindResource("SuccessBrush"),
        LogLevel.Warning => (Brush)FindResource("WarningBrush"),
        LogLevel.Error => (Brush)FindResource("DangerBrush"),
        LogLevel.Debug => (Brush)FindResource("MutedBrush"),
        _ => (Brush)FindResource("TextBrush")
    };

    private void TrimRows()
    {
        while (_rows.Count > 5000)
        {
            _rows.RemoveAt(0);
        }
    }

    private void UpdateCount() => CountText.Text = $"{_rows.Count:N0} entries";

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Reload();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Ui.Copy(string.Join(Environment.NewLine,
            _rows.Select(r => $"{r.TimeText} [{r.LevelText}] {r.Message}" +
                              (string.IsNullOrWhiteSpace(r.Detail) ? "" : " | " + r.Detail))));
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Ui.OpenFolder(AppServices.Log.LogDirectory);

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm("Clear the activity log shown in this window?\n\nThe log file on disk is kept.", "Clear log"))
        {
            return;
        }
        AppServices.Log.ClearMemory();
        _rows.Clear();
        UpdateCount();
    }

    private sealed record LogRow(
        string TimeText,
        string LevelText,
        Brush LevelBrush,
        string Message,
        string Detail,
        Visibility DetailVisibility);
}
