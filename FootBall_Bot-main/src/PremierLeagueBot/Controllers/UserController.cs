using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using PremierLeagueBot.Infrastructure;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/user")]
[ServiceFilter(typeof(TelegramAuthFilter))]
public sealed class UserController(IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
{
    private User CurrentUser => (User)HttpContext.Items[TelegramAuthFilter.CurrentUserKey]!;

    /// <summary>Returns the authenticated user's full profile.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await BuildProfileAsync(db, CurrentUser.TelegramId, ct));
    }

    /// <summary>Returns a public profile (game stats + achievements only).</summary>
    [HttpGet("{telegramId:long}")]
    public async Task<IActionResult> GetPublic(long telegramId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([telegramId], ct);
        if (user is null) return NotFound();
        return Ok(await BuildProfileAsync(db, telegramId, ct));
    }

    private static async Task<object> BuildProfileAsync(AppDbContext db, long telegramId, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.FavoriteTeam)
            .FirstAsync(u => u.TelegramId == telegramId, ct);

        var predictions = await db.Predictions
            .Include(p => p.Match).ThenInclude(m => m.HomeTeam)
            .Include(p => p.Match).ThenInclude(m => m.AwayTeam)
            .Where(p => p.TelegramId == telegramId && p.IsScored)
            .OrderByDescending(p => p.Match.MatchDate)
            .Take(10)
            .ToListAsync(ct);

        var activePredictions = await db.Predictions
            .Include(p => p.Match).ThenInclude(m => m.HomeTeam)
            .Include(p => p.Match).ThenInclude(m => m.AwayTeam)
            .Where(p => p.TelegramId == telegramId && !p.IsScored)
            .OrderBy(p => p.Match.MatchDate)
            .ToListAsync(ct);

        var achievements = await db.UserAchievements
            .Include(ua => ua.Achievement)
            .Where(ua => ua.TelegramId == telegramId)
            .OrderBy(ua => ua.EarnedAt)
            .ToListAsync(ct);

        var totalPoints   = predictions.Sum(p => p.PointsAwarded ?? 0);
        var totalOutcomes = predictions.Count(p => (p.PointsAwarded ?? 0) > 0);
        var exactScores   = predictions.Count(p => (p.PointsAwarded ?? 0) >= 3);
        var totalScored   = predictions.Count;
        var outcomeRate   = totalScored > 0 ? Math.Round(totalOutcomes * 100.0 / totalScored, 1) : 0;
        var exactRate     = totalScored > 0 ? Math.Round(exactScores   * 100.0 / totalScored, 1) : 0;

        // Perfect week = ISO week where ALL predictions are exact (≥ 3 predictions)
        var perfectWeeks = predictions
            .GroupBy(p => System.Globalization.ISOWeek.GetWeekOfYear(p.Match.MatchDate))
            .Count(g => g.Count() >= 3 && g.All(p => (p.PointsAwarded ?? 0) >= 3));

        var friendsCount = await db.Friendships
            .CountAsync(f => (f.RequesterId == telegramId || f.AddresseeId == telegramId)
                          && f.Status == "accepted", ct);

        return new
        {
            telegramId   = user.TelegramId,
            firstName    = user.FirstName,
            username     = user.Username,
            avatarUrl    = user.AvatarUrl,
            registeredAt = user.RegisteredAt,
            favoriteTeam = user.FavoriteTeam is null ? null : new
            {
                id       = user.FavoriteTeam.TeamId,
                name     = user.FavoriteTeam.Name,
                emblemUrl= user.FavoriteTeam.EmblemUrl
            },
            friendsCount,
            stats = new
            {
                totalPoints,
                totalPredictions = totalScored,
                outcomesCorrect  = totalOutcomes,
                exactScores,
                outcomeRate,
                exactRate,
                perfectWeeks
            },
            achievements = achievements.Select(ua => new
            {
                code        = ua.AchievementCode,
                nameRu      = ua.Achievement.NameRu,
                nameEn      = ua.Achievement.NameEn,
                descriptionRu = ua.Achievement.DescriptionRu,
                descriptionEn = ua.Achievement.DescriptionEn,
                icon        = ua.Achievement.Icon,
                earnedAt    = ua.EarnedAt
            }),
            history = predictions.Select(p => new
            {
                matchId          = p.MatchId,
                matchDate        = p.Match.MatchDate,
                homeTeam         = p.Match.HomeTeam.Name,
                awayTeam         = p.Match.AwayTeam.Name,
                homeEmblem       = p.Match.HomeTeam.EmblemUrl,
                awayEmblem       = p.Match.AwayTeam.EmblemUrl,
                predictedHome    = p.PredictedHomeScore,
                predictedAway    = p.PredictedAwayScore,
                actualHome       = p.Match.HomeScore,
                actualAway       = p.Match.AwayScore,
                pointsAwarded    = p.PointsAwarded,
                updatedAt        = p.UpdatedAt
            }),
            activePredictions = activePredictions.Select(p => new
            {
                matchId       = p.MatchId,
                matchDate     = p.Match.MatchDate,
                homeTeam      = p.Match.HomeTeam.Name,
                awayTeam      = p.Match.AwayTeam.Name,
                homeEmblem    = p.Match.HomeTeam.EmblemUrl,
                awayEmblem    = p.Match.AwayTeam.EmblemUrl,
                predictedHome = p.PredictedHomeScore,
                predictedAway = p.PredictedAwayScore,
                matchStatus   = p.Match.Status
            })
        };
    }
}
