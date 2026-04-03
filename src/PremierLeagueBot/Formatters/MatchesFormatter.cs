using System.Globalization;
using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class MatchesFormatter
{
    private static readonly CultureInfo Ru = new("ru-RU");

    /// <summary>
    /// Formats upcoming EPL matches.
    /// If favoriteTeamId is provided, that team's nearest match is shown first
    /// in a dedicated highlighted block.
    /// </summary>
    public static string FormatUpcoming(
        IReadOnlyList<MatchDto> matches,
        int? favoriteTeamId = null)
    {
        if (matches.Count == 0)
            return "📅 На ближайшую неделю матчей АПЛ не запланировано.";

        var sb = new StringBuilder();

        // ── Favorite team's next match ────────────────────────────────────────
        if (favoriteTeamId.HasValue)
        {
            var favMatch = matches
                .Where(m => m.HomeTeamId == favoriteTeamId || m.AwayTeamId == favoriteTeamId)
                .OrderBy(m => m.MatchDate)
                .FirstOrDefault();

            if (favMatch is not null)
            {
                var kickoff  = favMatch.MatchDate.ToLocalTime();
                var dateStr  = Capitalize(kickoff.ToString("dddd, d MMMM", Ru));
                var timeStr  = kickoff.ToString("HH:mm");

                sb.AppendLine("⭐ <b>Ближайший матч вашей команды</b>");
                sb.AppendLine("┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄");
                sb.AppendLine($"⚽ <b>{favMatch.HomeTeamName}  vs  {favMatch.AwayTeamName}</b>");
                sb.Append($"📆 <b>{dateStr}</b>   ⏰ <b>{timeStr}</b>");
                if (favMatch.Stadium is not null)
                    sb.Append($"\n📍 {favMatch.Stadium}");
                sb.AppendLine();
                sb.AppendLine();
            }
        }

        // ── All upcoming matches grouped by day ───────────────────────────────
        sb.AppendLine("📅 <b>Ближайшие матчи АПЛ (7 дней)</b>");

        var grouped = matches
            .OrderBy(m => m.MatchDate)
            .GroupBy(m => m.MatchDate.ToLocalTime().Date);

        foreach (var day in grouped)
        {
            sb.AppendLine();
            sb.AppendLine($"📆 <b>{Capitalize(day.Key.ToString("dddd, d MMMM yyyy", Ru))}</b>");
            sb.AppendLine("┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄");

            foreach (var m in day)
            {
                var time  = m.MatchDate.ToLocalTime().ToString("HH:mm");
                var isFav = favoriteTeamId.HasValue &&
                            (m.HomeTeamId == favoriteTeamId || m.AwayTeamId == favoriteTeamId);
                var prefix = isFav ? "⭐ " : "⚽ ";

                sb.AppendLine();
                sb.AppendLine($"{prefix}<b>{m.HomeTeamName}  vs  {m.AwayTeamName}</b>");
                sb.Append($"    ⏰ <b>{time}</b>");
                if (m.Stadium is not null) sb.Append($"   📍 {m.Stadium}");
                sb.AppendLine();
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
               (m.Stadium is not null ? $"\n📍 {m.Stadium}" : "");
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
