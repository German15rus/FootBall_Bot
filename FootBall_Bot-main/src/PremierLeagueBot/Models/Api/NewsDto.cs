namespace PremierLeagueBot.Models.Api;

public record NewsDto(
    string Title,
    string Summary,
    string Url,
    DateTime PublishedAt,
    int? RelatedTeamId
);
