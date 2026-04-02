namespace PremierLeagueBot.Models.Api;

public record PlayerDto(
    int PlayerId,
    int TeamId,
    string Name,
    int Number,
    string Position
);
