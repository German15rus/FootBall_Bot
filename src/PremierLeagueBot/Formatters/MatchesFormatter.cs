using System.Globalization;
using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class MatchesFormatter
{
    private static readonly CultureInfo Ru = new("ru-RU");

    // ── Upcoming matches with optional favorite-team highlight ────────────────

    /// <summary>
    /// Formats upcoming matches. If <paramref name="favoriteTeamId"/> is supplied,
    /// shows that team's nearest match in a dedicated block at the top.
    /// </summary>
    public static string FormatUpcomingWithFavorite(
        IReadOnlyList<MatchDto> matches,
        int? favoriteTeamId)
    {
        if (matches.Count == 0)
            return "📅 На ближайшую неделю матчей АПЛ не запланировано.";

        var sb = new StringBuilder();

        // ── Favorite team's next match ────────────────────────────────────────
        MatchDto? favMatch = null;
        if (favoriteTeamId.HasValue)
        {
            favMatch = matches
                .Where(m => m.HomeTeamId == favoriteTeamId || m.AwayTeamId == favoriteTeamId)
                .OrderBy(m => m.MatchDate)
                .FirstOrDefault();
        }

        if (favMatch is not null)
        {
            var favTime = favMatch.MatchDate.ToLocalTime();
            sb.AppendLine("⭐ <b>Ближайший матч вашей команды</b>");
            sb.AppendLine();
            sb.AppendLine($"⚽ <b>{favMatch.HomeTeamName} vs {favMatch.AwayTeamName}</b>");
            sb.AppendLine($"📅 {favTime:d MMM, HH:mm}");
            if (favMatch.Stadium is not null)
                sb.AppendLine($"📍 {favMatch.Stadium}");
            sb.AppendLine();
            sb.AppendLine("─────────────────────────");
            sb.AppendLine();
        }

        // ── All upcoming matches (excluding the already-shown favorite match) ──
        var rest = favMatch is not null
            ? matches.Where(m => m.MatchId != favMatch.MatchId).OrderBy(m => m.MatchDate).ToList()
            : matches.OrderBy(m => m.MatchDate).ToList();

        if (rest.Count == 0)
        {
            sb.Append("📅 <b>Больше матчей на ближайшие 7 дней нет.</b>");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("📅 <b>Ближайшие матчи Премьер-Лиги</b>");

        var grouped = rest.GroupBy(m => m.MatchDate.ToLocalTime().Date);

        foreach (var day in grouped)
        {
            sb.AppendLine();
            sb.AppendLine($"📆 <b>{Capitalize(day.Key.ToString("dddd, d MMMM yyyy", Ru))}</b>");
            sb.AppendLine("┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄┄");

            foreach (var m in day)
            {
                var time   = m.MatchDate.ToLocalTime().ToString("HH:mm");
                var isFav  = favoriteTeamId.HasValue &&
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

    // ── Legacy: kept for backward compatibility (notifications) ───────────────

    public static string FormatUpcoming(IReadOnlyList<MatchDto> matches)
        => FormatUpcomingWithFavorite(matches, favoriteTeamId: null);

    // ── Notification templates ────────────────────────────────────────────────

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
