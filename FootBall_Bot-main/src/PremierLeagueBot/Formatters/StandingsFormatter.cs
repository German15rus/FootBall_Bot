using System.Text;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Services.Emoji;

namespace PremierLeagueBot.Formatters;

public static class StandingsFormatter
{
    // ── Fallback emoji: shown when the custom emoji pack is not configured ────
    // Each club gets a unique character representing their identity/colours.
    // Premier League 2025/26 season — 20 clubs
    private static readonly Dictionary<string, string> FallbackEmoji =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arsenal"]                    = "🔴",
            ["Manchester City"]            = "🩵",
            ["Manchester United"]          = "😈",
            ["Aston Villa"]                = "🟣",
            ["Liverpool"]                  = "❤️",
            ["Chelsea"]                    = "💙",
            ["Brentford"]                  = "🟠",
            ["Everton"]                    = "💎",
            ["Fulham"]                     = "⚫",
            ["Brighton"]                   = "🔵",
            ["Brighton & Hove Albion"]     = "🔵",
            ["Brighton and Hove Albion"]   = "🔵",
            ["Sunderland"]                 = "🏴",
            ["Newcastle"]                  = "⚡",
            ["Newcastle United"]           = "⚡",
            ["Bournemouth"]                = "🍒",
            ["Crystal Palace"]             = "🦅",
            ["Leeds"]                      = "🦚",
            ["Leeds United"]               = "🦚",
            ["Nottingham Forest"]          = "🌳",
            ["Nottm Forest"]               = "🌳",
            ["Tottenham"]                  = "🐓",
            ["Tottenham Hotspur"]          = "🐓",
            ["West Ham"]                   = "⚒️",
            ["West Ham United"]            = "⚒️",
            ["Burnley"]                    = "🔥",
            ["Wolves"]                     = "🐺",
            ["Wolverhampton"]              = "🐺",
            ["Wolverhampton Wanderers"]    = "🐺",
        };

    // ── Zone colours ──────────────────────────────────────────────────────────
    // Premier League 2025/26 European/relegation zones:
    //   1–4  Champions League   🟦
    //   5    Europa League      🟨
    //   6    Conference League  🟩
    //   7–17 No Europe          ⬜
    //  18–20 Relegation         🟥
    private static string ZoneSquare(int rank) => rank switch
    {
        <= 4  => "🟦",
        5     => "🟨",
        6     => "🟩",
        >= 18 => "🟥",
        _     => "⬜"
    };

    // ── Main formatter ────────────────────────────────────────────────────────

    /// <summary>
    /// Formats the league table with zone indicators.
    /// When <paramref name="emojiService"/> is provided and has a loaded pack,
    /// club names render with Premium custom emoji; otherwise standard emoji.
    /// </summary>
    public static string Format(
        IReadOnlyList<StandingDto> standings,
        EmojiPackService? emojiService = null)
    {
        if (standings.Count == 0)
            return "⚠️ Таблица временно недоступна. Данные загружаются...";

        var sb = new StringBuilder();
        sb.AppendLine($"🏴󠁧󠁢󠁥󠁮󠁧󠁿 <b>АПЛ — Таблица 2025/26</b>");
        sb.AppendLine($"<i>Тур {standings.Max(s => s.Played)}</i>");
        sb.AppendLine();

        var renderZone = emojiService is { IsReady: true }
            ? (Func<int, string>)(r => emojiService.RenderZone(r))
            : ZoneSquare;

        foreach (var s in standings)
        {
            var zone   = renderZone(s.Rank);
            var rank   = $"{s.Rank}.";

            // Use short name, truncate if needed
            var name = (string.IsNullOrWhiteSpace(s.ShortName) ? s.TeamName : s.ShortName);
            if (name.Length > 14) name = name[..13] + "…";

            var fallback = GetFallbackEmoji(s.TeamName);
            var emblem   = emojiService is { IsReady: true }
                ? emojiService.RenderEmblem(s.TeamName, fallback)
                : fallback;

            var gd = s.GoalDifference >= 0 ? $"+{s.GoalDifference}" : s.GoalDifference.ToString();

            sb.AppendLine($"{zone}<b>{rank,-3}</b>{emblem} {name,-14} {s.Played,2}  <b>{s.Points,3}</b>  {gd,4}");
        }

        sb.AppendLine();
        var z1  = renderZone(1);
        var z5  = renderZone(5);
        var z6  = renderZone(6);
        var z10 = renderZone(10);
        sb.AppendLine(
            $"{z1} Лига чемпионов (1–4)\n" +
            $"{z5} Лига Европы (5)\n" +
            $"{z6} Лига конференций (6)\n" +
            $"🟥 Вылет (18–20)\n" +
            $"{z10} Без еврокубков");
        sb.AppendLine();
        sb.Append("<i>И — игры · О — очки · ГР — разница голов</i>");

        return sb.ToString();
    }

    private static string GetFallbackEmoji(string teamName)
        => FallbackEmoji.TryGetValue(teamName, out var e) ? e : "⚽";
}
