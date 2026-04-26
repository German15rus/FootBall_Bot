using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Services.Football;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(
    MatchRepository matchRepo,
    TeamRepository teamRepo,
    IFootballApiClient football) : ControllerBase
{
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming(
        [FromQuery] string league = "epl",
        CancellationToken ct = default)
    {
        var from          = DateTime.UtcNow;
        var to            = from.AddDays(14);
        var competitionId = league.Equals("ucl", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

        var matches = await matchRepo.GetUpcomingAsync(from, to, "scheduled", competitionId, ct);

        if (competitionId == 1)
        {
            var standings  = await football.GetStandingsAsync(ct);
            var eplTeamIds = standings.Select(s => s.TeamId).ToHashSet();
            if (eplTeamIds.Count > 0)
                matches = matches
                    .Where(m => eplTeamIds.Contains(m.HomeTeamId) || eplTeamIds.Contains(m.AwayTeamId))
                    .ToList();
        }

        // Batch-load all teams needed
        var teamIds = matches.SelectMany(m => new[] { m.HomeTeamId, m.AwayTeamId }).Distinct();
        var teams   = await teamRepo.GetManyAsync(teamIds, ct);

        return Ok(matches.Select(m =>
        {
            teams.TryGetValue(m.HomeTeamId, out var homeTeam);
            teams.TryGetValue(m.AwayTeamId, out var awayTeam);
            return new
            {
                matchId       = m.MatchId,
                matchDate     = m.MatchDate,
                stadium       = m.Stadium,
                status        = m.Status,
                competitionId = m.CompetitionId,
                deadlineUtc   = m.MatchDate,
                homeTeam      = new { id = m.HomeTeamId, name = homeTeam?.Name ?? "?", emblemUrl = homeTeam?.EmblemUrl },
                awayTeam      = new { id = m.AwayTeamId, name = awayTeam?.Name ?? "?", emblemUrl = awayTeam?.EmblemUrl },
                homeScore     = m.HomeScore,
                awayScore     = m.AwayScore
            };
        }));
    }
}
