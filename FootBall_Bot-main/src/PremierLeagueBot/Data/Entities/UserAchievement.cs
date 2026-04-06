namespace PremierLeagueBot.Data.Entities;

public class UserAchievement
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string AchievementCode { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Achievement Achievement { get; set; } = null!;
}
