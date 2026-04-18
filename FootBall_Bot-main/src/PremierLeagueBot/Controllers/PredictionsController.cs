using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using PremierLeagueBot.Infrastructure;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/predictions")]
[ServiceFilter(typeof(TelegramAuthFilter))]
public sealed class PredictionsController(IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
{
    private User CurrentUser => (User)HttpContext.Items[TelegramAuthFilter.CurrentUserKey]!;

    /// <summary>Returns all predictions for the current user (scored + unscored).</summary>
    [HttpGet]
    public async Task<IActionResult> GetMy(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var predictions = await db.Predictions
            .Include(p => p.Match).ThenInclude(m => m.HomeTeam)
            .Include(p => p.Match).ThenInclude(m => m.AwayTeam)
            .Where(p => p.TelegramId == CurrentUser.TelegramId)
            .OrderByDescending(p => p.Match.MatchDate)
            .ToListAsync(ct);

        return Ok(predictions.Select(MapPrediction));
    }

    /// <summary>Creates or updates a prediction. Rejected if past the deadline.</summary>
    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] SavePredictionRequest req, CancellationToken ct)
    {
        if (req.HomeScore < 0 || req.AwayScore < 0 || req.HomeScore > 20 || req.AwayScore > 20)
            return BadRequest(new { error = "Score must be between 0 and 20" });

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var match = await db.Matches.FindAsync([req.MatchId], ct);
        if (match is null)
            return NotFound(new { error = "Match not found" });

        if (match.Status != "scheduled")
            return UnprocessableEntity(new { error = "Match has already started or finished" });

        var deadline = match.MatchDate;
        if (DateTime.UtcNow >= deadline)
            return UnprocessableEntity(new { error = "Prediction deadline has passed", deadline });

        var existing = await db.Predictions
            .FirstOrDefaultAsync(p => p.TelegramId == CurrentUser.TelegramId && p.MatchId == req.MatchId, ct);

        if (existing is null)
        {
            existing = new Prediction
            {
                TelegramId           = CurrentUser.TelegramId,
                MatchId              = req.MatchId,
                PredictedHomeScore   = req.HomeScore,
                PredictedAwayScore   = req.AwayScore,
                CreatedAt            = DateTime.UtcNow,
                UpdatedAt            = DateTime.UtcNow
            };
            db.Predictions.Add(existing);
        }
        else
        {
            existing.PredictedHomeScore = req.HomeScore;
            existing.PredictedAwayScore = req.AwayScore;
            existing.UpdatedAt          = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // Reload with navigation properties
        await db.Entry(existing).Reference(p => p.Match).LoadAsync(ct);
        await db.Entry(existing.Match).Reference(m => m.HomeTeam).LoadAsync(ct);
        await db.Entry(existing.Match).Reference(m => m.AwayTeam).LoadAsync(ct);

        return Ok(MapPrediction(existing));
    }

    private static object MapPrediction(Prediction p) => new
    {
        id             = p.Id,
        matchId        = p.MatchId,
        matchDate      = p.Match.MatchDate,
        deadlineUtc    = p.Match.MatchDate,
        homeTeam       = new { id = p.Match.HomeTeamId, name = p.Match.HomeTeam.Name, emblemUrl = p.Match.HomeTeam.EmblemUrl },
        awayTeam       = new { id = p.Match.AwayTeamId, name = p.Match.AwayTeam.Name, emblemUrl = p.Match.AwayTeam.EmblemUrl },
        predictedHome  = p.PredictedHomeScore,
        predictedAway  = p.PredictedAwayScore,
        actualHome     = p.Match.HomeScore,
        actualAway     = p.Match.AwayScore,
        matchStatus    = p.Match.Status,
        pointsAwarded  = p.PointsAwarded,
        isScored       = p.IsScored,
        updatedAt      = p.UpdatedAt
    };
}

public sealed record SavePredictionRequest(int MatchId, int HomeScore, int AwayScore);
