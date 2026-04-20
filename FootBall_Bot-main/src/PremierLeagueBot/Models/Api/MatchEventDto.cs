namespace PremierLeagueBot.Models.Api;

public enum MatchEventType
{
    Goal,
    YellowCard,
    RedCard,
    HalfTime
}

public record MatchEventDto(
    int MatchId,
    string EventKey,
    MatchEventType Type,
    int Minute,
    string? PlayerName,
    int? TeamId,
    int? HomeScore,
    int? AwayScore
);
