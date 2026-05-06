namespace PremierLeagueBot.Formatters;

internal static class TimeHelper
{
    private static readonly TimeZoneInfo LondonTz = GetLondonTz();

    /// <summary>Converts UTC time to London local time (accounts for BST/GMT automatically).</summary>
    public static DateTime ToLondonTime(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            LondonTz);

    private static TimeZoneInfo GetLondonTz()
    {
        // Windows uses "GMT Standard Time", Linux/Mac uses "Europe/London"
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
    }
}
