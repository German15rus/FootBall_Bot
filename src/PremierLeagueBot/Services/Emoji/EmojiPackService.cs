using Telegram.Bot;
using Telegram.Bot.Types;

namespace PremierLeagueBot.Services.Emoji;

/// <summary>
/// Fetches the football clubs custom emoji pack from Telegram at startup,
/// then maps each Premier League club name to its <c>custom_emoji_id</c>.
///
/// Usage in HTML messages:
///   <tg-emoji emoji-id="CUSTOM_EMOJI_ID">⚽</tg-emoji>
///
/// The pack short name (e.g. "FootballClubs") is configured in
/// appsettings.json under <c>EmojiPackName</c>.
/// </summary>
public sealed class EmojiPackService(
    ITelegramBotClient bot,
    IConfiguration configuration,
    ILogger<EmojiPackService> logger)
{
    // PL team name → custom_emoji_id string
    private readonly Dictionary<string, string> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsReady { get; private set; }

    // ── Explicit mapping: sticker emoji character → PL club name ─────────────
    // Each sticker in the pack has a "fallback emoji" character.
    // This table maps that character to the correct PL team name.
    // Update this if the pack uses different characters.
    private static readonly Dictionary<string, string> EmojiToClub =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Common football emoji characters used in club packs
            // (will be refined automatically after first run — check the logs)
            ["🔴"] = "Arsenal",
            ["❤️"] = "Liverpool",
            ["😈"] = "Manchester United",
            ["🩵"] = "Manchester City",
            ["💙"] = "Chelsea",
            ["🟣"] = "Aston Villa",
            ["🍒"] = "Bournemouth",
            ["🟠"] = "Brentford",
            ["🦅"] = "Crystal Palace",
            ["💎"] = "Everton",
            ["⚫"] = "Fulham",
            ["🔷"] = "Ipswich Town",
            ["🦊"] = "Leicester City",
            ["🌳"] = "Nottingham Forest",
            ["⚓"] = "Southampton",
            ["🐓"] = "Tottenham Hotspur",
            ["⚡"] = "Newcastle United",
            ["⚒️"] = "West Ham United",
            ["🐺"] = "Wolves",
            ["🔵"] = "Brighton",
        };

    // ── Startup initialisation ────────────────────────────────────────────────

    /// <summary>
    /// Loads the emoji pack from Telegram. Call once at application startup.
    /// Safe to call even if the pack name is not configured (no-op).
    /// </summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        var packName = configuration["EmojiPackName"]?.Trim();
        if (string.IsNullOrEmpty(packName))
        {
            logger.LogInformation(
                "EmojiPackName not configured — using standard emoji in standings table. " +
                "Set EmojiPackName in appsettings.json to enable custom emoji.");
            return;
        }

        await LoadPackAsync(packName, ct);
    }

    private async Task LoadPackAsync(string packName, CancellationToken ct)
    {
        try
        {
            StickerSet set = await bot.GetStickerSet(packName, ct);

            logger.LogInformation(
                "Emoji pack '{Name}' loaded: {Count} stickers", set.Name, set.Stickers.Length);

            // Log all stickers so the operator can build the mapping if needed
            for (int i = 0; i < set.Stickers.Length; i++)
            {
                var sticker = set.Stickers[i];
                logger.LogInformation(
                    "  [{Index}] emoji={Emoji}  custom_emoji_id={Id}",
                    i, sticker.Emoji, sticker.CustomEmojiId ?? "(none)");
            }

            // Try to auto-map using the emoji character
            int mapped = 0;
            foreach (var sticker in set.Stickers)
            {
                if (string.IsNullOrEmpty(sticker.CustomEmojiId)) continue;

                if (sticker.Emoji is not null &&
                    EmojiToClub.TryGetValue(sticker.Emoji, out var club))
                {
                    _map[club] = sticker.CustomEmojiId;
                    mapped++;
                    logger.LogDebug("Mapped {Club} → emoji_id={Id}", club, sticker.CustomEmojiId);
                }
            }

            logger.LogInformation(
                "Auto-mapped {Mapped}/{Total} clubs from pack '{Pack}'",
                mapped, set.Stickers.Length, packName);

            IsReady = _map.Count > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not load emoji pack '{Pack}' — check EmojiPackName in appsettings.json. " +
                "Falling back to standard emoji.", packName);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Telegram custom emoji ID for a club, or null if not mapped.
    /// </summary>
    public string? GetCustomEmojiId(string clubName)
        => _map.TryGetValue(clubName, out var id) ? id : null;

    /// <summary>
    /// Renders the club emblem as an inline custom emoji (HTML mode).
    /// Falls back to a standard emoji character if the club is not in the pack.
    /// </summary>
    /// <param name="clubName">Official PL club name (e.g. "Arsenal").</param>
    /// <param name="fallbackEmoji">Standard emoji to show if pack not available.</param>
    public string RenderEmblem(string clubName, string fallbackEmoji)
    {
        var id = GetCustomEmojiId(clubName);
        // <tg-emoji> is shown to Premium users; fallback shown to others
        return id is not null
            ? $"<tg-emoji emoji-id=\"{id}\">{fallbackEmoji}</tg-emoji>"
            : fallbackEmoji;
    }
}
