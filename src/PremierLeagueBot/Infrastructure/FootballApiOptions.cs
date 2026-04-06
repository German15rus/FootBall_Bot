namespace PremierLeagueBot.Infrastructure;

public sealed class FootballApiOptions
{
    public const string Section = "FootballApi";

    // ── Official Premier League API (via Pulse Live) ──────────────────────────
    /// <summary>Official PL API base URL. Requires Origin/Referer headers.</summary>
    public string PlBaseUrl { get; set; } = "https://footballapi.pulselive.com/football/";

    /// <summary>Premier League competition ID in the PL API.</summary>
    public int CompetitionId { get; set; } = 1;

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
