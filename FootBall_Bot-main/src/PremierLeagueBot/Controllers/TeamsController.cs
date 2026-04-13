using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Services.Football;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/teams")]
public sealed class TeamsController(IFootballApiClient football) : ControllerBase
{
    /// <summary>Returns squad and last 5 matches for a team.</summary>
    [HttpGet("{teamId:int}")]
    public async Task<IActionResult> GetTeam(int teamId, CancellationToken ct)
    {
        var squadTask  = football.GetTeamSquadAsync(teamId, ct);
        var recentTask = football.GetRecentMatchesAsync(teamId, 5, ct);
        await Task.WhenAll(squadTask, recentTask);

        var squad  = squadTask.Result;
        var recent = recentTask.Result;

        return Ok(new
        {
            squad = squad
                .OrderBy(p => p.Position)
                .ThenBy(p => p.Number)
                .Select(p => new
                {
                    playerId = p.PlayerId,
                    name     = p.Name,
                    number   = p.Number,
                    position = p.Position
                }),
            recentMatches = recent.Select(m => new
            {
                matchId   = m.MatchId,
                matchDate = m.MatchDate,
                homeTeamId   = m.HomeTeamId,
                homeTeamName = m.HomeTeamName,
                awayTeamId   = m.AwayTeamId,
                awayTeamName = m.AwayTeamName,
                homeScore = m.HomeScore,
                awayScore = m.AwayScore,
                status    = m.Status,
                // Result from the perspective of the requested team
                result = m.HomeScore.HasValue && m.AwayScore.HasValue
                    ? (m.HomeTeamId == teamId
                        ? (m.HomeScore > m.AwayScore ? "W" : m.HomeScore < m.AwayScore ? "L" : "D")
                        : (m.AwayScore > m.HomeScore ? "W" : m.AwayScore < m.HomeScore ? "L" : "D"))
                    : null
            })
        });
    }
}
