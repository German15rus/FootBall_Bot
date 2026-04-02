namespace PremierLeagueBot.Data.Entities;

public class NotificationLog
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
