using System.Globalization;
using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class TeamInfoFormatter
{
    private static readonly CultureInfo Ru = new("ru-RU");

    private static readonly Dictionary<string, string> PositionLabels = new()
    {
        ["goalkeeper"] = "🧤 Вратари",
        ["defender"]   = "🛡 Защитники",
        ["midfielder"] = "⚙️ Полузащитники",
        ["forward"]    = "⚽ Нападающие",
    };

    // ── Squad ─────────────────────────────────────────────────────────────────

    public static string FormatSquad(string teamName, IReadOnlyList<PlayerDto> players)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🏟 <b>{teamName} — Основной состав</b>");
        sb.AppendLine();

        if (players.Count == 0)
        {
            sb.Append("⚠️ Данные о составе временно недоступны.\n" +
                      "Попробуйте повторить запрос через несколько секунд.");
            return sb.ToString();
        }

        var groups = players
            .GroupBy(p => p.Position)
            .OrderBy(g => PositionOrder(g.Key));

        foreach (var g in groups)
        {
            var label = PositionLabels.GetValueOrDefault(g.Key, g.Key);
            sb.AppendLine($"<b>{label}</b>");

            foreach (var p in g.OrderBy(x => x.Number == 0 ? 99 : x.Number))
            {
                var num = p.Number > 0 ? $"#{p.Number}" : " —";
                sb.AppendLine($"  <code>{num,3}</code>  {p.Name}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ── Recent matches ────────────────────────────────────────────────────────

    /// <summary>
    /// Formats the last N matches in the style requested:
    ///
    /// Arsenal VS Manchester City
    /// Поражение: 0 - 2
    /// Играли дома: Arsenal
    /// Матч сыгран: 22 марта 2026
    /// </summary>
    public static string FormatRecentMatches(
        string teamName, int teamId, IReadOnlyList<MatchDto> matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"📊 <b>{teamName} — Последние матчи</b>");
        sb.AppendLine();

        if (matches.Count == 0)
        {
            sb.Append("⚠️ История матчей временно недоступна.");
            return sb.ToString();
        }

        // Show most recent first
        foreach (var m in matches.OrderByDescending(m => m.MatchDate))
        {
            // Determine if selected team played home or away
            // Use both ID and name comparison to handle different data sources
            var isHome = m.HomeTeamId == teamId
                      || m.HomeTeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase)
                      || teamName.Contains(m.HomeTeamName.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
                      || m.HomeTeamName.Contains(teamName.Split(' ')[0], StringComparison.OrdinalIgnoreCase);

            var ourScore  = isHome ? m.HomeScore : m.AwayScore;
            var oppScore  = isHome ? m.AwayScore : m.HomeScore;
            var homeLabel = m.HomeTeamName;

            var resultWord = GetResultWord(ourScore, oppScore);
            var resultEmoji = GetResultEmoji(ourScore, oppScore);

            var dateStr = m.MatchDate.ToLocalTime().ToString("d MMMM yyyy", Ru);
            var score   = (m.HomeScore.HasValue && m.AwayScore.HasValue)
                ? $"{m.HomeScore} - {m.AwayScore}"
                : "— - —";

            sb.AppendLine($"<b>{m.HomeTeamName} VS {m.AwayTeamName}</b>");
            sb.AppendLine($"{resultEmoji} {resultWord}: {score}");
            sb.AppendLine($"🏠 Играли дома: {homeLabel}");
            sb.AppendLine($"📅 Матч сыгран: {dateStr}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetResultWord(int? teamScore, int? oppScore)
    {
        if (teamScore is null || oppScore is null) return "Данные недоступны";
        if (teamScore > oppScore)  return "Победа";
        if (teamScore == oppScore) return "Ничья";
        return "Поражение";
    }

    private static string GetResultEmoji(int? teamScore, int? oppScore)
    {
        if (teamScore is null || oppScore is null) return "❓";
        if (teamScore > oppScore)  return "✅";
        if (teamScore == oppScore) return "🟡";
        return "❌";
    }

    private static int PositionOrder(string pos) => pos switch
    {
        "goalkeeper" => 0,
        "defender"   => 1,
        "midfielder" => 2,
        "forward"    => 3,
        _            => 4
    };
}
