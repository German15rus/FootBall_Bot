using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Services.Football;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/matches")]
public sealed class MatchesController(
    IDbContextFactory<AppDbContext> dbFactory,
    IFootballApiClient football) : ControllerBase
{
    /// <summary>Returns matches available for predictions (next 7 days, EPL only).</summary>
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming(CancellationToken ct)
    {
        var standings  = await football.GetStandingsAsync(ct);
        var eplTeamIds = standings.Select(s => s.TeamId).ToHashSet();

        var from = DateTime.UtcNow.Date;
        var to   = from.AddDays(7);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var matches = await db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Where(m => m.MatchDate >= from && m.MatchDate <= to
                     && m.Status == "scheduled"
                     && (eplTeamIds.Contains(m.HomeTeamId) || eplTeamIds.Contains(m.AwayTeamId)))
            .OrderBy(m => m.MatchDate)
            .ToListAsync(ct);

        return Ok(matches.Select(m => new
        {
            matchId      = m.MatchId,
            matchDate    = m.MatchDate,
            stadium      = m.Stadium,
            status       = m.Status,
            deadlineUtc  = m.MatchDate.AddMinutes(-5),
            homeTeam     = new { id = m.HomeTeamId, name = m.HomeTeam.Name, emblemUrl = m.HomeTeam.EmblemUrl },
            awayTeam     = new { id = m.AwayTeamId, name = m.AwayTeam.Name, emblemUrl = m.AwayTeam.EmblemUrl },
            homeScore    = m.HomeScore,
            awayScore    = m.AwayScore
        }));
    }
}
