namespace PremierLeagueBot.Infrastructure;

public sealed class FootballApiOptions
{
    public const string Section = "FootballApi";

    // ── Official Premier League API (via Pulse Live) ──────────────────────────
    /// <summary>Official PL API base URL. Requires Origin/Referer headers.</summary>
    public string PlBaseUrl { get; set; } = "https://footballapi.pulselive.com/football/";

    /// <summary>Premier League competition ID in the PL API.</summary>
    public int CompetitionId { get; set; } = 1;

    /// <summary>
    /// Override the PL season ID. Set to 0 to auto-detect (recommended).
    /// If auto-detection returns the wrong season, set this manually.
    /// Example: 719 = 2024/25. Find the current season ID in the bot logs on startup.
    /// </summary>
    public int SeasonId { get; set; } = 0;

    // ── Champions League ──────────────────────────────────────────────────────
    /// <summary>Champions League competition ID in the PL API.</summary>
    public int ClCompetitionId { get; set; } = 2;

    /// <summary>
    /// Override the CL season ID. Set to 0 to auto-detect.
    /// Example: 813 = 2025/26.
    /// </summary>
    public int ClSeasonId { get; set; } = 0;

    // ── TheSportsDB (fallback for squad data) ─────────────────────────────────
    /// <summary>TheSportsDB base URL (free, no key required).</summary>
    public string SportsDbBaseUrl { get; set; } = "https://www.thesportsdb.com/api/v1/json/3/";

    /// <summary>TheSportsDB league ID: 4328 = English Premier League.</summary>
    public int LeagueId { get; set; } = 4328;

    /// <summary>Current EPL season string for TheSportsDB (e.g. "2025-2026").</summary>
    public string Season { get; set; } = "2025-2026";

    // ── News ──────────────────────────────────────────────────────────────────
    /// <summary>BBC Sport EPL RSS feed (free, no key required).</summary>
    public string NewsRssUrl { get; set; } =
        "https://feeds.bbci.co.uk/sport/football/premier-league/rss.xml";
}
