namespace PremierLeagueBot.Data.Entities;

/// <summary>
/// Tracks which in-match events (goals, cards, half-time) were already broadcast
/// to subscribers. Composite PK (MatchId, EventKey) prevents duplicate sends across
/// LiveMatchNotificationService polls.
/// </summary>
public class MatchEventNotification
{
    public int MatchId { get; set; }

    /// <summary>Stable id from PL API if present, else composite string.</summary>
    public string EventKey { get; set; } = "";

    public DateTime SentAt { get; set; }
}
