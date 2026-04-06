using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using PremierLeagueBot.Services.Achievements;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Runs every minute, finds finished matches with unscored predictions,
/// awards points and checks for new achievements.
///
/// Scoring rules:
///   1 pt  — correct outcome (W/D/L) but wrong score
///   3 pts — exact score (default)
///   4 pts — exact score, table-position gap ≥ 10
///   5 pts — exact score, table-position gap ≥ 15  (dynamic, hidden from user)
/// </summary>
public sealed class PredictionScoringService(
    IServiceScopeFactory scopeFactory,
    ILogger<PredictionScoringService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PredictionScoringService started");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // startup grace

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScoreAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error in PredictionScoringService");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ScoreAsync(CancellationToken ct)
    {
        using var scope          = scopeFactory.CreateScope();
        var db                   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var achievementService   = scope.ServiceProvider.GetRequiredService<AchievementService>();

        // Find predictions for finished matches that haven't been scored yet
        var unscored = await db.Predictions
            .Include(p => p.Match).ThenInclude(m => m.HomeTeam)
            .Include(p => p.Match).ThenInclude(m => m.AwayTeam)
            .Where(p => !p.IsScored
                     && p.Match.Status == "finished"
                     && p.Match.HomeScore.HasValue
                     && p.Match.AwayScore.HasValue)
            .ToListAsync(ct);

        if (unscored.Count == 0) return;

        var affectedUsers = new HashSet<long>();

        foreach (var prediction in unscored)
        {
            var actualHome = prediction.Match.HomeScore!.Value;
            var actualAway = prediction.Match.AwayScore!.Value;

            prediction.PointsAwarded = CalculatePoints(
                prediction.PredictedHomeScore,
                prediction.PredictedAwayScore,
                actualHome,
                actualAway,
                prediction.Match.HomeTeam.Position,
                prediction.Match.AwayTeam.Position);

            prediction.IsScored = true;
            affectedUsers.Add(prediction.TelegramId);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Scored {Count} predictions", unscored.Count);

        // Check achievements for each affected user
        foreach (var userId in affectedUsers)
        {
            await achievementService.CheckAndGrantAsync(userId, ct);
        }
    }

    private static int CalculatePoints(
        int predHome, int predAway,
        int actualHome, int actualAway,
        int? homePos, int? awayPos)
    {
        bool exactScore   = predHome == actualHome && predAway == actualAway;
        bool correctOutcome = Outcome(predHome, predAway) == Outcome(actualHome, actualAway);

        if (!correctOutcome) return 0;
        if (!exactScore)     return 1;

        // Dynamic coefficient for exact scores
        if (homePos.HasValue && awayPos.HasValue)
        {
            var gap = Math.Abs(homePos.Value - awayPos.Value);
            if (gap >= 15) return 5;
            if (gap >= 10) return 4;
        }

        return 3;
    }

    private static char Outcome(int home, int away) =>
        home > away ? 'H' : home < away ? 'A' : 'D';
}
