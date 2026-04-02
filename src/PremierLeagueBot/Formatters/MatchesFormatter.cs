using System.Globalization;
using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class MatchesFormatter
{
    private static readonly CultureInfo Ru = new("ru-RU");

    public static string FormatUpcoming(IReadOnlyList<MatchDto> matches)
    {
        if (matches.Count == 0)
            return "📅 На ближайшую неделю матчей АПЛ не запланировано.";

        var sb = new StringBuilder();
        sb.AppendLine("📅 <b>Ближайшие матчи АПЛ — следующие 7 дней</b>");

        var grouped = matches
            .OrderBy(m => m.MatchDate)
            .GroupBy(m => m.MatchDate.ToLocalTime().Date);

        foreach (var day in grouped)
        {
            sb.AppendLine();
            // Day header
            var dayName = day.Key.ToString("dddd, d MMMM", Ru);
            dayName = char.ToUpper(dayName[0]) + dayName[1..];
            sb.AppendLine($"📆 <b>{dayName}</b>");
            sb.AppendLine("┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄");

            foreach (var m in day)
            {
                var time = m.MatchDate.ToLocalTime().ToString("HH:mm");
                sb.AppendLine();
                sb.AppendLine($"⚽ <b>{m.HomeTeamName}  vs  {m.AwayTeamName}</b>");
                sb.AppendLine($"    ⏰ {time}" + (m.Stadium != null ? $"   📍 {m.Stadium}" : ""));
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatResult(MatchDto m)
    {
        var date = m.MatchDate.ToLocalTime().ToString("d MMMM, HH:mm", Ru);
        return $"🏁 <b>Матч завершён!</b>\n\n" +
               $"⚽ <b>{m.HomeTeamName}  {m.HomeScore} – {m.AwayScore}  {m.AwayTeamName}</b>\n\n" +
               $"📅 {date}";
    }

    public static string FormatReminder(MatchDto m)
    {
        var time = m.MatchDate.ToLocalTime().ToString("HH:mm");
        return $"🔔 <b>Матч начинается через 15 минут!</b>\n\n" +
               $"⚽ <b>{m.HomeTeamName}  vs  {m.AwayTeamName}</b>\n" +
               $"⏰ Начало в {time}" +
               (m.Stadium != null ? $"\n📍 {m.Stadium}" : "");
    }
}
