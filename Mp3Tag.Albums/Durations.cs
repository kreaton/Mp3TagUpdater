namespace Mp3Tag.Albums;

/// <summary>Track lengths as "m:ss" (or "h:mm:ss"), the way the APIs and players show them.</summary>
public static class Durations
{
    public static string Format(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}" : $"{(int)d.TotalMinutes}:{d.Seconds:D2}";

    public static string Format(TimeSpan? d) => d is { } value ? Format(value) : "?:??";

    /// <summary>Parses "3:05" or "1:02:03"; returns null for empty or unreadable text (Discogs often has none).</summary>
    public static TimeSpan? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3) return null;

        var numbers = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return null;

        return numbers.Length == 2
            ? new TimeSpan(0, numbers[0], numbers[1])
            : new TimeSpan(numbers[0], numbers[1], numbers[2]);
    }
}
