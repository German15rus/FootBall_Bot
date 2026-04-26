using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Services.Achievements;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Runs every minute, finds finished matches with unscored predictions,
/// awards points and checks for new achievements.
///
/// Scoring rules:
///   0 pts — incorrect outcome (wrong W/D/L)
///   1 pt  — correct outcome (W/D/L) but wrong score
///   3 pts — exact score
/// </summary>
public sealed class PredictionScoringService(
    IServiceScopeFactory scopeFactory,
    ILogger<PredictionScoringService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PredictionScoringService started");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScoreAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                { logger.LogError(ex, "Error in PredictionScoringService"); }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ScoreAsync(CancellationToken ct)
    {
        using var scope          = scopeFactory.CreateScope();
        var matchRepo            = scope.ServiceProvider.GetRequiredService<MatchRepository>();
        var predRepo             = scope.ServiceProvider.GetRequiredService<PredictionRepository>();
        var achievementService   = scope.ServiceProvider.GetRequiredService<AchievementService>();

        // Find all finished matches with scores
        var finishedMatches = await matchRepo.GetFinishedWithScoresAsync(ct);
        if (finishedMatches.Count == 0) return;

        var affectedUsers = new HashSet<long>();

        foreach (var match in finishedMatches)
        {
            var unscored = await predRepo.GetUnscoredByMatchAsync(match.MatchId, ct);
            if (unscored.Count == 0) continue;

            foreach (var p in unscored)
            {
                p.PointsAwarded = CalculatePoints(
                    p.PredictedHomeScore, p.PredictedAwayScore,
                    match.HomeScore!.Value, match.AwayScore!.Value);
                p.IsScored       = true;
                p.MatchStatus    = match.Status;
                p.MatchHomeScore = match.HomeScore;
                p.MatchAwayScore = match.AwayScore;
                affectedUsers.Add(p.TelegramId);
            }

            await predRepo.BatchUpdateAsync(unscored, ct);
            logger.LogInformation("Scored {Count} predictions for match {MatchId}", unscored.Count, match.MatchId);
        }

        foreach (var userId in affectedUsers)
            await achievementService.CheckAndGrantAsync(userId, ct);
    }

    private static int CalculatePoints(int predHome, int predAway, int actualHome, int actualAway)
    {
        bool exactScore     = predHome == actualHome && predAway == actualAway;
        bool correctOutcome = Outcome(predHome, predAway) == Outcome(actualHome, actualAway);

        if (!correctOutcome) return 0;
        if (!exactScore)     return 1;
        return 3;
    }

    private static char Outcome(int home, int away) =>
        home > away ? 'H' : home < away ? 'A' : 'D';
}
