using System.Globalization;
using System.Text;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Formatters;

public static class StandingsFormatter
{
    public static string Format(IReadOnlyList<StandingDto> standings)
    {
        if (standings.Count == 0)
            return "⚠️ Таблица временно недоступна. Данные загружаются...";

        var updated = DateTime.UtcNow.ToString("d MMMM yyyy, HH:mm", new CultureInfo("ru-RU")) + " UTC";
        var sb      = new StringBuilder();

        sb.AppendLine("🏴󠁧󠁢󠁥󠁮󠁧󠁿 <b>Английская Премьер-Лига 2025/26</b>");
        sb.AppendLine($"<i>Обновлено: {updated}</i>");
        sb.AppendLine();

        foreach (var s in standings)
        {
            var medal = s.Rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"{s.Rank,2}."
            };

            var gd = s.GoalDifference >= 0 ? $"+{s.GoalDifference}" : s.GoalDifference.ToString();

            sb.AppendLine(
                $"{medal} {s.TeamName}" +
                $" — <b>{s.Points} pts</b>" +
                $"  <i>{s.Played}M  {gd}</i>");
        }

        sb.AppendLine();
        sb.Append("<i>pts — очки  ·  M — матчи  ·  ГР — разница мячей</i>");
        return sb.ToString();
    }
}
