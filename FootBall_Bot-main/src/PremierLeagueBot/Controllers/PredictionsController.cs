using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Infrastructure;
using Serilog;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/predictions")]
[ServiceFilter(typeof(TelegramAuthFilter))]
public sealed class PredictionsController(
    PredictionRepository predRepo,
    MatchRepository matchRepo,
    TeamRepository teamRepo) : ControllerBase
{
    private UserDoc CurrentUser => (UserDoc)HttpContext.Items[TelegramAuthFilter.CurrentUserKey]!;

    [HttpGet]
    public async Task<IActionResult> GetMy(CancellationToken ct)
    {
        var predictions = await predRepo.GetByUserAsync(CurrentUser.TelegramId, ct);
        return Ok(predictions.Select(MapPrediction));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] SavePredictionRequest req, CancellationToken ct)
    {
        if (req.HomeScore < 0 || req.AwayScore < 0 || req.HomeScore > 20 || req.AwayScore > 20)
            return BadRequest(new { error = "Score must be between 0 and 20" });

        try
        {
            var match = await matchRepo.GetByIdAsync(req.MatchId, ct);
            if (match is null)
                return NotFound(new { error = "Match not found" });

            if (match.Status != "scheduled")
                return UnprocessableEntity(new { error = "Match has already started or finished" });

            if (DateTime.UtcNow >= match.MatchDate)
                return UnprocessableEntity(new { error = "Prediction deadline has passed", deadline = match.MatchDate });

            // Load team names for denormalization (only on create/update)
            var teams = await teamRepo.GetManyAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);
            teams.TryGetValue(match.HomeTeamId, out var homeTeam);
            teams.TryGetValue(match.AwayTeamId, out var awayTeam);

            var existing = await predRepo.GetAsync(CurrentUser.TelegramId, req.MatchId, ct);

            if (existing is null)
            {
                existing = new PredictionDoc
                {
                    TelegramId          = CurrentUser.TelegramId,
                    MatchId             = req.MatchId,
                    PredictedHomeScore  = req.HomeScore,
                    PredictedAwayScore  = req.AwayScore,
                    CreatedAt           = DateTime.UtcNow,
                    UpdatedAt           = DateTime.UtcNow,
                    MatchDate           = match.MatchDate,
                    MatchStatus         = match.Status,
                    MatchHomeScore      = match.HomeScore,
                    MatchAwayScore      = match.AwayScore,
                    HomeTeamId          = match.HomeTeamId,
                    AwayTeamId          = match.AwayTeamId,
                    HomeTeamName        = homeTeam?.Name ?? "?",
                    AwayTeamName        = awayTeam?.Name ?? "?",
                    HomeTeamEmblem      = homeTeam?.EmblemUrl,
                    AwayTeamEmblem      = awayTeam?.EmblemUrl
                };
            }
            else
            {
                existing.PredictedHomeScore = req.HomeScore;
                existing.PredictedAwayScore = req.AwayScore;
                existing.UpdatedAt          = DateTime.UtcNow;
            }

            await predRepo.UpsertAsync(existing, ct);
            return Ok(MapPrediction(existing));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving prediction matchId={MatchId} userId={UserId}", req.MatchId, CurrentUser.TelegramId);
            return StatusCode(500, new { error = "Failed to save prediction. Please try again." });
        }
    }

    private static object MapPrediction(PredictionDoc p) => new
    {
        id            = p.DocId,
        matchId       = p.MatchId,
        matchDate     = p.MatchDate,
        deadlineUtc   = p.MatchDate,
        homeTeam      = new { id = p.HomeTeamId, name = p.HomeTeamName, emblemUrl = p.HomeTeamEmblem },
        awayTeam      = new { id = p.AwayTeamId, name = p.AwayTeamName, emblemUrl = p.AwayTeamEmblem },
        predictedHome = p.PredictedHomeScore,
        predictedAway = p.PredictedAwayScore,
        actualHome    = p.MatchHomeScore,
        actualAway    = p.MatchAwayScore,
        matchStatus   = p.MatchStatus,
        pointsAwarded = p.PointsAwarded,
        isScored      = p.IsScored,
        updatedAt     = p.UpdatedAt
    };
}

public sealed record SavePredictionRequest(int MatchId, int HomeScore, int AwayScore);
