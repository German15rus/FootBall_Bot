using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Formatters;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Services.Notification;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Background service that runs every 30 seconds and sends two types of notifications:
/// 1. Pre-match reminder (15 min before kick-off)  → status = scheduled
/// 2. Post-match result                            → status changed to finished
/// </summary>
public sealed class MatchNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<MatchNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MatchNotificationService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in MatchNotificationService");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndNotifyAsync(CancellationToken ct)
    {
        using var scope   = scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = DateTime.UtcNow;

        // ── 1. Pre-match reminders (kick-off within next 15 minutes) ─────────
        var preMatchCutoff = now.AddMinutes(15);
        var upcoming = await db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Where(m => m.Status == "scheduled"
                     && !m.PreMatchNotificationSent
                     && m.MatchDate >= now
                     && m.MatchDate <= preMatchCutoff)
            .ToListAsync(ct);

        foreach (var match in upcoming)
        {
            var dto      = MapToDto(match);
            var message  = MatchesFormatter.FormatReminder(dto);
            var userIds  = await GetSubscribersAsync(db, match.HomeTeamId, match.AwayTeamId, ct);

            if (userIds.Count > 0)
            {
                logger.LogInformation(
                    "Sending pre-match notification for match {MatchId} to {Count} users",
                    match.MatchId, userIds.Count);
                await notifications.BroadcastAsync(userIds, message, ct);
            }

            match.PreMatchNotificationSent = true;
        }

        // ── 2. Post-match results ────────────────────────────────────────────
        var finished = await db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Where(m => m.Status == "finished"
                     && !m.PostMatchNotificationSent)
            .ToListAsync(ct);

        foreach (var match in finished)
        {
            var dto     = MapToDto(match);
            var message = MatchesFormatter.FormatResult(dto);
            var userIds = await GetSubscribersAsync(db, match.HomeTeamId, match.AwayTeamId, ct);

            if (userIds.Count > 0)
            {
                logger.LogInformation(
                    "Sending post-match result for match {MatchId} to {Count} users",
                    match.MatchId, userIds.Count);
                await notifications.BroadcastAsync(userIds, message, ct);
            }

            match.PostMatchNotificationSent = true;
        }

        if (upcoming.Count > 0 || finished.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private static async Task<List<long>> GetSubscribersAsync(
        AppDbContext db, int homeTeamId, int awayTeamId, CancellationToken ct)
        => await db.Users
            .Where(u => u.FavoriteTeamId == homeTeamId || u.FavoriteTeamId == awayTeamId)
            .Select(u => u.TelegramId)
            .ToListAsync(ct);

    private static MatchDto MapToDto(Data.Entities.Match m) => new(
        MatchId:      m.MatchId,
        HomeTeamId:   m.HomeTeamId,
        HomeTeamName: m.HomeTeam.Name,
        AwayTeamId:   m.AwayTeamId,
        AwayTeamName: m.AwayTeam.Name,
        MatchDate:    m.MatchDate,
        Stadium:      m.Stadium,
        HomeScore:    m.HomeScore,
        AwayScore:    m.AwayScore,
        Status:       m.Status
    );
}
