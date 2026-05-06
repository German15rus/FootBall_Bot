using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Infrastructure;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/user")]
[ServiceFilter(typeof(TelegramAuthFilter))]
public sealed class UserController(
    UserRepository userRepo,
    TeamRepository teamRepo,
    PredictionRepository predRepo,
    AchievementRepository achRepo,
    FriendshipRepository friendRepo) : ControllerBase
{
    private UserDoc CurrentUser => (UserDoc)HttpContext.Items[TelegramAuthFilter.CurrentUserKey]!;

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
        => Ok(await BuildProfileAsync(CurrentUser.TelegramId, ct));

    [HttpGet("{telegramId:long}")]
    public async Task<IActionResult> GetPublic(long telegramId, CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(telegramId, ct);
        if (user is null) return NotFound();
        return Ok(await BuildProfileAsync(telegramId, ct));
    }

    private async Task<object> BuildProfileAsync(long telegramId, CancellationToken ct)
    {
        // Загружаем независимые данные параллельно
        var userTask         = userRepo.GetByIdAsync(telegramId, ct);
        var predictionsTask  = predRepo.GetByUserAsync(telegramId, ct);
        var achievementsTask = achRepo.GetUserAchievementsAsync(telegramId, ct);
        var friendsTask      = friendRepo.CountAcceptedAsync(telegramId, ct);

        await Task.WhenAll(userTask, predictionsTask, achievementsTask, friendsTask);

        var user           = userTask.Result;
        var allPredictions = predictionsTask.Result;
        var achievements   = achievementsTask.Result;
        var friendsCount   = friendsTask.Result;

        // Любимая команда — зависит от user, загружаем отдельно
        TeamDoc? favoriteTeam = null;
        if (user?.FavoriteTeamId.HasValue == true)
            favoriteTeam = await teamRepo.GetByIdAsync(user.FavoriteTeamId.Value, ct);

        // Stats from scored predictions
        var scored         = allPredictions.Where(p => p.IsScored).ToList();
        var totalPoints    = scored.Sum(p => p.PointsAwarded ?? 0);
        var totalOutcomes  = scored.Count(p => (p.PointsAwarded ?? 0) > 0);
        var exactScores    = scored.Count(p => (p.PointsAwarded ?? 0) >= 3);
        var totalScored    = scored.Count;
        var outcomeRate    = totalScored > 0 ? Math.Round(totalOutcomes * 100.0 / totalScored, 1) : 0;
        var exactRate      = totalScored > 0 ? Math.Round(exactScores   * 100.0 / totalScored, 1) : 0;

        var perfectWeeks = scored
            .GroupBy(p => System.Globalization.ISOWeek.GetWeekOfYear(p.MatchDate))
            .Count(g => g.Count() >= 3 && g.All(p => (p.PointsAwarded ?? 0) >= 3));

        var history = scored
            .OrderByDescending(p => p.MatchDate)
            .Take(10)
            .Select(p => new
            {
                matchId       = p.MatchId,
                matchDate     = p.MatchDate,
                homeTeam      = p.HomeTeamName,
                awayTeam      = p.AwayTeamName,
                homeEmblem    = p.HomeTeamEmblem,
                awayEmblem    = p.AwayTeamEmblem,
                predictedHome = p.PredictedHomeScore,
                predictedAway = p.PredictedAwayScore,
                actualHome    = p.MatchHomeScore,
                actualAway    = p.MatchAwayScore,
                pointsAwarded = p.PointsAwarded,
                updatedAt     = p.UpdatedAt
            });

        var activePredictions = allPredictions
            .Where(p => !p.IsScored)
            .OrderBy(p => p.MatchDate)
            .Select(p => new
            {
                matchId       = p.MatchId,
                matchDate     = p.MatchDate,
                homeTeam      = p.HomeTeamName,
                awayTeam      = p.AwayTeamName,
                homeEmblem    = p.HomeTeamEmblem,
                awayEmblem    = p.AwayTeamEmblem,
                predictedHome = p.PredictedHomeScore,
                predictedAway = p.PredictedAwayScore,
                matchStatus   = p.MatchStatus
            });

        return new
        {
            telegramId   = user!.TelegramId,
            firstName    = user.FirstName,
            username     = user.Username,
            avatarUrl    = user.AvatarUrl,
            registeredAt = user.RegisteredAt,
            favoriteTeam = favoriteTeam is null ? null : new
            {
                id        = favoriteTeam.TeamId,
                name      = favoriteTeam.Name,
                emblemUrl = favoriteTeam.EmblemUrl
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
                code          = ua.AchievementCode,
                nameRu        = ua.NameRu,
                nameEn        = ua.NameEn,
                descriptionRu = ua.DescriptionRu,
                descriptionEn = ua.DescriptionEn,
                icon          = ua.Icon,
                earnedAt      = ua.EarnedAt
            }),
            history,
            activePredictions
        };
    }
}
