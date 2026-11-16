namespace H10FirmwareManager.Core;

public static class Humanize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long value)
    {
        if (value < 0)
        {
            return "—";
        }
        double size = value;
        int unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:N0} B" : $"{size:0.##} {Units[unit]}";
    }

    public static string Percent(double value) => $"{value:0.#}%";

    public static string Relative(DateTimeOffset? value)
    {
        if (value is null)
        {
            return "—";
        }
        var delta = DateTimeOffset.UtcNow - value.Value;
        if (delta.TotalMinutes < 1) return "just now";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes} min ago";
        if (delta.TotalDays < 1) return $"{(int)delta.TotalHours} h ago";
        if (delta.TotalDays < 31) return $"{(int)delta.TotalDays} d ago";
        return value.Value.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
