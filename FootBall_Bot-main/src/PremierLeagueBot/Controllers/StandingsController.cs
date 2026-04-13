using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Services.Football;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/standings")]
public sealed class StandingsController(IFootballApiClient football) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var standings = await football.GetStandingsAsync(ct);
        return Ok(standings.Select(s => new
        {
            rank           = s.Rank,
            teamId         = s.TeamId,
            teamName       = s.TeamName,
            shortName      = s.ShortName,
            emblemUrl      = s.EmblemUrl,
            played         = s.Played,
            won            = s.Won,
            drawn          = s.Drawn,
            lost           = s.Lost,
            goalsFor       = s.GoalsFor,
            goalsAgainst   = s.GoalsAgainst,
            goalDifference = s.GoalDifference,
            points         = s.Points
        }));
    }
}
