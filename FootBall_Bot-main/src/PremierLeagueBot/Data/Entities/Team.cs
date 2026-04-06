namespace PremierLeagueBot.Data.Entities;

public class Team
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? EmblemUrl { get; set; }
    public int? Position { get; set; }

    public ICollection<Player> Players { get; set; } = [];
    public ICollection<Match> HomeMatches { get; set; } = [];
    public ICollection<Match> AwayMatches { get; set; } = [];
    public ICollection<User> Followers { get; set; } = [];
}
