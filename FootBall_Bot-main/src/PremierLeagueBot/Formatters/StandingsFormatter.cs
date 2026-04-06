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

    // ── Main formatter ────────────────────────────────────────────────────────

    /// <summary>
    /// Formats the league table.
    /// When <paramref name="emojiService"/> is provided and has a loaded pack,
    /// club names render with Premium custom emoji
    /// (<c>&lt;tg-emoji emoji-id="..."&gt;</c>);
    /// otherwise standard emoji characters are used as fallback.
    /// </summary>
    public static string Format(
        IReadOnlyList<StandingDto> standings,
        EmojiPackService? emojiService = null)
    {
        if (standings.Count == 0)
            return "⚠️ Таблица временно недоступна. Данные загружаются...";

        var sb = new StringBuilder();
        sb.AppendLine($"🏴󠁧󠁢󠁥󠁮󠁧󠁿 <b>АПЛ — Таблица 2025/26 (тур {standings.Max(s => s.Played)})</b>");
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

            // Truncate long names to keep table aligned
            var name = s.TeamName.Length > 20
                ? s.TeamName[..19] + "…"
                : s.TeamName;

            // Get team emblem emoji (custom if pack loaded, fallback otherwise)
            var fallback = GetFallbackEmoji(s.TeamName);
            var emblem   = emojiService is { IsReady: true }
                ? emojiService.RenderEmblem(s.TeamName, fallback)
                : fallback;

            var gd = s.GoalDifference >= 0 ? $"+{s.GoalDifference}" : s.GoalDifference.ToString();

            // Medals take 2 chars visually vs "XX." — pad accordingly
            var rankPad = s.Rank <= 3 ? " " : "";
            sb.AppendLine($"{rankPad}{medal} {emblem} {name,-20} {s.Played,2} {s.Points,3} {gd,4}");
        }

        sb.AppendLine();
        sb.AppendLine("─────────────────────────────────────");
        sb.Append("<i>И — сыграно,  О — очки,  ГР — разница мячей</i>");

        return sb.ToString();
    }

    private static string GetFallbackEmoji(string teamName)
        => FallbackEmoji.TryGetValue(teamName, out var e) ? e : "⚽";
}
