namespace PremierLeagueBot.Data.Entities;

public class User
{
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? LanguageCode { get; set; }
    public int? FavoriteTeamId { get; set; }
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public string? SessionToken { get; set; }

    public Team? FavoriteTeam { get; set; }
    public ICollection<Prediction> Predictions { get; set; } = [];
    public ICollection<UserAchievement> UserAchievements { get; set; } = [];
}
