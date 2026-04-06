namespace PremierLeagueBot.Data.Entities;

public class Prediction
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public int MatchId { get; set; }
    public int PredictedHomeScore { get; set; }
    public int PredictedAwayScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>null = not yet scored; 0/1/3/4/5 after match finishes</summary>
    public int? PointsAwarded { get; set; }
    public bool IsScored { get; set; }

    public User User { get; set; } = null!;
    public Match Match { get; set; } = null!;
}
