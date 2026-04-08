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
    // Used when stickers have unique emoji characters per club.
    private static readonly Dictionary<string, string> EmojiToClub =
        new(StringComparer.OrdinalIgnoreCase)
        {
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

    // ── Manual override: custom_emoji_id → PL club name ───────────────────────
    // Used when multiple clubs share the same fallback emoji (e.g. 🏴󠁧󠁢󠁥󠁮󠁧󠁿 for all EPL clubs).
    // Populated from configuration key "EmojiIdToClub" in appsettings.json.
    private readonly Dictionary<string, string> _idToClub =
        new(StringComparer.OrdinalIgnoreCase);

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

        // Load manual overrides from config (custom_emoji_id → club name)
        var overrides = configuration.GetSection("EmojiIdToClub").Get<Dictionary<string, string>>();
        if (overrides is { Count: > 0 })
        {
            foreach (var (id, club) in overrides)
                _idToClub[id] = club;
            logger.LogInformation("Loaded {Count} manual emoji_id overrides from config", _idToClub.Count);
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

            // Map stickers: manual override takes priority, then auto by emoji character
            int mapped = 0;
            foreach (var sticker in set.Stickers)
            {
                if (string.IsNullOrEmpty(sticker.CustomEmojiId)) continue;

                string? club = null;

                // 1) Check manual id→club override
                if (_idToClub.TryGetValue(sticker.CustomEmojiId, out var manualClub))
                    club = manualClub;
                // 2) Fallback: auto-map by emoji character
                else if (sticker.Emoji is not null &&
                         EmojiToClub.TryGetValue(sticker.Emoji, out var autoClub))
                    club = autoClub;

                if (club is not null)
                {
                    _map[club] = sticker.CustomEmojiId;
                    mapped++;
                    logger.LogDebug("Mapped {Club} → emoji_id={Id}", club, sticker.CustomEmojiId);
                }
            }

            logger.LogInformation(
                "Mapped {Mapped}/{Total} clubs from pack '{Pack}'",
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
    public string RenderEmblem(string clubName, string fallbackEmoji)
    {
        var id = GetCustomEmojiId(clubName);
        return id is not null
            ? $"<tg-emoji emoji-id=\"{id}\">{fallbackEmoji}</tg-emoji>"
            : fallbackEmoji;
    }

    /// <summary>
    /// Renders a zone indicator for the given league position.
    /// Uses competition logos from the pack when configured; falls back to coloured squares.
    /// Relegation zone always stays 🟥 (no competition logo needed).
    /// </summary>
    public string RenderZone(int rank)
    {
        var (key, fallback) = rank switch
        {
            <= 4  => ("ChampionsLeague",  "🟦"),
            5     => ("EuropaLeague",     "🟨"),
            6     => ("ConferenceLeague", "🟩"),
            >= 18 => (null,               "🟥"),
            _     => ("PremierLeague",    "⬜"),
        };

        if (key is null) return fallback;
        var id = configuration[$"ZoneEmojiIds:{key}"];
        return string.IsNullOrEmpty(id)
            ? fallback
            : $"<tg-emoji emoji-id=\"{id}\">{fallback}</tg-emoji>";
    }
}
