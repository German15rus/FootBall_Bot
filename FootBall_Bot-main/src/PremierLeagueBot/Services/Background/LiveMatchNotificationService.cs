using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using PremierLeagueBot.Formatters;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Services.Emoji;
using PremierLeagueBot.Services.Football;
using PremierLeagueBot.Services.Notification;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Background service that, while a favorite-team match is in progress,
/// broadcasts per-event notifications to subscribers:
///   • Goals (minute, scorer, running score with club emblems)
///   • Yellow / red cards (minute, player, team)
///   • Half-time summary (score + first-half scorers)
///
/// Polls PL API every 60 s only for matches that have at least one subscriber,
/// and de-duplicates via the MatchEventNotifications table.
/// </summary>
public sealed class LiveMatchNotificationService(
    IServiceScopeFactory scopeFactory,
    IFootballApiClient   football,
    EmojiPackService     emoji,
    ILogger<LiveMatchNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("LiveMatchNotificationService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error in LiveMatchNotificationService");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope   = scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now       = DateTime.UtcNow;
        var windowLo  = now.AddHours(-3);     // catches matches that overran / extra time
        var windowHi  = now.AddMinutes(10);   // catches matches where status lags slightly

        // Candidate matches: live, or recently kicked-off (not yet flipped to "live" in DB)
        var candidates = await db.Matches
            .Include(m => m.HomeTeam)
            .Include(m => m.AwayTeam)
            .Where(m => m.Status != "finished"
                     && m.MatchDate >= windowLo
                     && m.MatchDate <= windowHi)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        // Teams that at least one user follows — we only spend API calls on those matches.
        var favoriteTeamIds = await db.Users
            .Where(u => u.FavoriteTeamId != null)
            .Select(u => u.FavoriteTeamId!.Value)
            .Distinct()
            .ToListAsync(ct);

        if (favoriteTeamIds.Count == 0) return;

        var favoriteSet = favoriteTeamIds.ToHashSet();

        foreach (var match in candidates)
        {
            if (!favoriteSet.Contains(match.HomeTeamId) &&
                !favoriteSet.Contains(match.AwayTeamId)) continue;

            try
            {
                await ProcessMatchAsync(db, notifications, match, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed processing live events for match {MatchId}", match.MatchId);
            }
        }
    }

    private async Task ProcessMatchAsync(
        AppDbContext db,
        NotificationService notifications,
        Match match,
        CancellationToken ct)
    {
        var events = await football.GetMatchEventsAsync(match.MatchId, ct);
        if (events.Count == 0) return;

        // Already-sent event keys for this match
        var sentKeys = await db.MatchEventNotifications
            .Where(x => x.MatchId == match.MatchId)
            .Select(x => x.EventKey)
            .ToListAsync(ct);
        var sentSet = sentKeys.ToHashSet(StringComparer.Ordinal);

        var subscribers = await db.Users
            .Where(u => u.FavoriteTeamId == match.HomeTeamId || u.FavoriteTeamId == match.AwayTeamId)
            .Select(u => u.TelegramId)
            .ToListAsync(ct);

        if (subscribers.Count == 0) return;

        var newLogEntries = new List<MatchEventNotification>();

        // Replay events in chronological order so score-after-goal is consistent.
        foreach (var ev in events.OrderBy(e => e.Minute))
        {
            if (sentSet.Contains(ev.EventKey)) continue;

            string? message = ev.Type switch
            {
                MatchEventType.Goal =>
                    MatchesFormatter.FormatGoal(ev, match.HomeTeam.Name, match.AwayTeam.Name, emoji),

                MatchEventType.YellowCard or MatchEventType.RedCard =>
                    MatchesFormatter.FormatCard(ev, ResolveTeamName(ev.TeamId, match)),

                MatchEventType.HalfTime when !match.HalftimeNotificationSent =>
                    BuildHalftimeMessage(ev, events, match),

                _ => null
            };

            if (message is null) continue;

            logger.LogInformation(
                "Live event: match={MatchId} type={Type} min={Min} player={Player} → {Count} subscribers",
                match.MatchId, ev.Type, ev.Minute, ev.PlayerName, subscribers.Count);

            await notifications.BroadcastAsync(subscribers, message, ct);

            if (ev.Type == MatchEventType.HalfTime)
                match.HalftimeNotificationSent = true;

            newLogEntries.Add(new MatchEventNotification
            {
                MatchId  = match.MatchId,
                EventKey = ev.EventKey,
                SentAt   = DateTime.UtcNow,
            });
            sentSet.Add(ev.EventKey);
        }

        if (newLogEntries.Count > 0)
        {
            db.MatchEventNotifications.AddRange(newLogEntries);
            await db.SaveChangesAsync(ct);
        }
    }

    private string BuildHalftimeMessage(
        MatchEventDto halftimeEv,
        IReadOnlyList<MatchEventDto> all,
        Match match)
    {
        // Prefer scores captured on the HT event itself; fall back to DB snapshot.
        var hs = halftimeEv.HomeScore ?? match.HomeScore ?? 0;
        var as_ = halftimeEv.AwayScore ?? match.AwayScore ?? 0;

        var firstHalfGoals = all
            .Where(e => e.Type == MatchEventType.Goal && e.Minute <= 45)
            .ToList();

        return MatchesFormatter.FormatHalftime(
            hs, as_, match.HomeTeam.Name, match.AwayTeam.Name, firstHalfGoals, emoji);
    }

    private static string ResolveTeamName(int? teamId, Match match)
    {
        if (teamId == match.HomeTeamId) return match.HomeTeam.Name;
        if (teamId == match.AwayTeamId) return match.AwayTeam.Name;
        return "—";
    }
}
