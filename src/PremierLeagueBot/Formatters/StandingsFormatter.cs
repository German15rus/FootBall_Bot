using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class StandingsFormatter
{
    public static string Format(IReadOnlyList<StandingDto> standings)
    {
        if (standings.Count == 0)
            return "⚠️ Таблица пока недоступна. Данные загружаются...";

        var sb = new StringBuilder();
        sb.AppendLine("🏴󠁧󠁢󠁥󠁮󠁧󠁿 <b>Английская Премьер-Лига — Таблица 2024/25</b>");
        sb.AppendLine();

        // Header inside <pre> for monospace alignment
        sb.AppendLine("<pre>");
        sb.AppendLine("     Команда              И   О   ГР");
        sb.AppendLine("─────────────────────────────────────");

        foreach (var s in standings)
        {
            var medal = s.Rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"{s.Rank,2}."
            };

            // Truncate long names to keep table aligned
            var name = s.TeamName.Length > 20
                ? s.TeamName[..19] + "…"
                : s.TeamName;

            var gd = s.GoalDifference >= 0 ? $"+{s.GoalDifference}" : s.GoalDifference.ToString();

            // Medals take 2 chars visually vs "XX." — pad accordingly
            var rankPad = s.Rank <= 3 ? " " : "";
            sb.AppendLine($"{rankPad}{medal} {name,-20} {s.Played,2} {s.Points,3} {gd,4}");
        }

        sb.AppendLine("─────────────────────────────────────");
        sb.AppendLine("</pre>");
        sb.Append("<i>И — сыграно,  О — очки,  ГР — разница мячей</i>");

        return sb.ToString();
    }
}
