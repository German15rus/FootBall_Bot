namespace PremierLeagueBot.Infrastructure;

public sealed class PlApiOptions
{
    public const string Section = "PremierLeagueApi";

    /// <summary>Official PL Pulselive API base URL (undocumented but public)</summary>
    public string BaseUrl { get; set; } = "https://footballapi.pulselive.com/football/";

    /// <summary>Required Origin header to bypass CORS check</summary>
    public string Origin { get; set; } = "https://www.premierleague.com";
}
