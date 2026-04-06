namespace PremierLeagueBot.Models.Api;

public record StandingDto(
    int Rank,
    int TeamId,
    string TeamName,
    string ShortName,
    string? EmblemUrl,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points
);
