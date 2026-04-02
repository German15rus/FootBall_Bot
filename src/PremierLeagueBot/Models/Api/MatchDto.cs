namespace PremierLeagueBot.Models.Api;

public record MatchDto(
    int MatchId,
    int HomeTeamId,
    string HomeTeamName,
    int AwayTeamId,
    string AwayTeamName,
    DateTime MatchDate,
    string? Stadium,
    int? HomeScore,
    int? AwayScore,
    string Status
);
