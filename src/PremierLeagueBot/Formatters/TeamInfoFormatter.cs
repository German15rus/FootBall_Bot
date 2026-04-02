using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class TeamInfoFormatter
{
    private static readonly Dictionary<string, string> PositionLabels = new()
    {
        ["goalkeeper"] = "🧤 Вратари",
        ["defender"]   = "🛡 Защитники",
        ["midfielder"] = "⚙️ Полузащитники",
        ["forward"]    = "⚽ Нападающие",
    };

    public static string FormatSquad(string teamName, IReadOnlyList<PlayerDto> players)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>🏟 {teamName} — Состав</b>");
        sb.AppendLine();

        var groups = players
            .OrderBy(p => PositionOrder(p.Position))
            .ThenBy(p => p.Number)
            .GroupBy(p => p.Position);

        foreach (var g in groups)
        {
            var label = PositionLabels.GetValueOrDefault(g.Key, g.Key);
            sb.AppendLine($"<b>{label}</b>");
            foreach (var p in g)
                sb.AppendLine($"  #{p.Number,2}  {p.Name}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatRecentMatches(string teamName, IReadOnlyList<MatchDto> matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>📊 {teamName} — Последние матчи</b>");
        sb.AppendLine();

        foreach (var m in matches)
        {
            // Determine if the given team is home or away to calculate W/D/L
            var isHome    = m.HomeTeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase);
            var opponent  = isHome ? m.AwayTeamName : m.HomeTeamName;
            var teamScore = isHome ? m.HomeScore : m.AwayScore;
            var oppScore  = isHome ? m.AwayScore : m.HomeScore;
            var result    = GetResult(teamScore, oppScore);

            sb.AppendLine(
                $"{result} {m.MatchDate.ToLocalTime():d MMM}  " +
                $"vs {opponent}  " +
                $"<b>{teamScore}–{oppScore}</b>");
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetResult(int? teamScore, int? oppScore)
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
