namespace PremierLeagueBot.Data.Entities;

public class User
{
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public int? FavoriteTeamId { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public Team? FavoriteTeam { get; set; }
}
