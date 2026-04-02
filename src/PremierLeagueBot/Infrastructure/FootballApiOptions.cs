namespace PremierLeagueBot.Infrastructure;

public sealed class FootballApiOptions
{
    public const string Section = "FootballApi";

    /// <summary>TheSportsDB base URL (free, no key required)</summary>
    public string BaseUrl { get; set; } = "https://www.thesportsdb.com/api/v1/json/3/";

    /// <summary>TheSportsDB league ID: 4328 = English Premier League</summary>
    public int LeagueId { get; set; } = 4328;

    /// <summary>Season string, e.g. "2024-2025"</summary>
    public string Season { get; set; } = "2024-2025";

    /// <summary>BBC Sport EPL RSS feed (free, no key required)</summary>
    public string NewsRssUrl { get; set; } =
        "https://feeds.bbci.co.uk/sport/football/premier-league/rss.xml";
}
