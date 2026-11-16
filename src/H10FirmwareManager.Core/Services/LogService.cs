using System.Text;

namespace H10FirmwareManager.Core.Services;

public enum LogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message, string? Detail = null)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>Thread-safe application log with a live event and a daily text file.</summary>
public sealed class LogService
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];
    private readonly string _logDirectory;
    private readonly string _logFile;

    public LogService(string? dataDirectory = null)
    {
        string root = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "H10FirmwareManager");
        _logDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(_logDirectory);
        _logFile = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public event EventHandler<LogEntry>? EntryWritten;

    public string LogDirectory => _logDirectory;
    public string LogFile => _logFile;

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    public void Debug(string message, string? detail = null) => Write(LogLevel.Debug, message, detail);
    public void Info(string message, string? detail = null) => Write(LogLevel.Info, message, detail);
    public void Success(string message, string? detail = null) => Write(LogLevel.Success, message, detail);
    public void Warning(string message, string? detail = null) => Write(LogLevel.Warning, message, detail);
    public void Error(string message, string? detail = null) => Write(LogLevel.Error, message, detail);

    public void Write(LogLevel level, string message, string? detail = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message, detail);
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > 3000)
            {
                _entries.RemoveRange(0, _entries.Count - 3000);
            }

            try
            {
                var line = new StringBuilder()
                    .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
                    .Append(message);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    line.Append(" | ").Append(detail.ReplaceLineEndings(" "));
                }
                File.AppendAllText(_logFile, line.AppendLine().ToString(), Encoding.UTF8);
            }
            catch
            {
                // Logging must never break an installation.
            }
        }
        EntryWritten?.Invoke(this, entry);
    }

    public void ClearMemory()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public string BuildReport()
    {
        var sb = new StringBuilder();
        foreach (var entry in Snapshot())
        {
            sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(entry.Level.ToString().ToUpperInvariant()).Append("] ")
              .Append(entry.Message);
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                sb.Append(" | ").Append(entry.Detail);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
