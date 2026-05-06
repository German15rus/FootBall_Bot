using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Formatters;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Services.Notification;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Sends pre-match reminders (15 min before kick-off) and post-match results.
/// Runs every 30 seconds.
/// </summary>
public sealed class MatchNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<MatchNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MatchNotificationService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAndNotifyAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { logger.LogError(ex, "Error in MatchNotificationService"); }
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        // Пропускаем тик, если предыдущий ещё не завершился
        if (!await _lock.WaitAsync(0, ct)) return;
        try
        {
        using var scope   = scopeFactory.CreateScope();
        var matchRepo     = scope.ServiceProvider.GetRequiredService<MatchRepository>();
        var teamRepo      = scope.ServiceProvider.GetRequiredService<TeamRepository>();
        var userRepo      = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = DateTime.UtcNow;

        // ── 1. Pre-match reminders ────────────────────────────────────────────
        var upcoming = await matchRepo.GetScheduledBeforeAsync(now, now.AddMinutes(15), ct);
        foreach (var match in upcoming)
        {
            var teams    = await teamRepo.GetManyAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);
            var dto      = MapToDto(match, teams);
            var message  = MatchesFormatter.FormatReminder(dto);
            var userIds  = await userRepo.GetSubscriberIdsAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);

            if (userIds.Count > 0)
            {
                logger.LogInformation("Pre-match notification for match {MatchId} → {Count} users", match.MatchId, userIds.Count);
                await notifications.BroadcastAsync(userIds, message, ct);
            }

            await matchRepo.UpdateFieldsAsync(match.MatchId,
                new Dictionary<string, object?> { ["PreMatchNotificationSent"] = true }, ct);
        }

        // ── 2. Post-match results ─────────────────────────────────────────────
        var finished = await matchRepo.GetFinishedUnnotifiedAsync(ct);
        foreach (var match in finished)
        {
            var teams   = await teamRepo.GetManyAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);
            var dto     = MapToDto(match, teams);
            var message = MatchesFormatter.FormatResult(dto);
            var userIds = await userRepo.GetSubscriberIdsAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);

            if (userIds.Count > 0)
            {
                logger.LogInformation("Post-match result for match {MatchId} → {Count} users", match.MatchId, userIds.Count);
                await notifications.BroadcastAsync(userIds, message, ct);
            }

            await matchRepo.UpdateFieldsAsync(match.MatchId,
                new Dictionary<string, object?> { ["PostMatchNotificationSent"] = true }, ct);
        }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static MatchDto MapToDto(MatchDoc m, Dictionary<int, TeamDoc> teams)
    {
        teams.TryGetValue(m.HomeTeamId, out var home);
        teams.TryGetValue(m.AwayTeamId, out var away);
        return new MatchDto(
            MatchId:      m.MatchId,
            HomeTeamId:   m.HomeTeamId,
            HomeTeamName: home?.Name ?? "?",
            AwayTeamId:   m.AwayTeamId,
            AwayTeamName: away?.Name ?? "?",
            MatchDate:    m.MatchDate,
            Stadium:      m.Stadium,
            HomeScore:    m.HomeScore,
            AwayScore:    m.AwayScore,
            Status:       m.Status
        );
    }
}
