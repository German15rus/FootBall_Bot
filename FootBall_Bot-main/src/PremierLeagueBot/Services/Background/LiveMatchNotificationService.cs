using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Formatters;
using PremierLeagueBot.Models.Api;
using PremierLeagueBot.Services.Emoji;
using PremierLeagueBot.Services.Football;
using PremierLeagueBot.Services.Notification;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Polls every 60 s for active matches and broadcasts live events (goals, cards, half-time)
/// to users who follow one of the teams. De-duplicates via MatchEventNotification subcollection.
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
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { logger.LogError(ex, "Error in LiveMatchNotificationService"); }
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope   = scopeFactory.CreateScope();
        var matchRepo     = scope.ServiceProvider.GetRequiredService<MatchRepository>();
        var teamRepo      = scope.ServiceProvider.GetRequiredService<TeamRepository>();
        var userRepo      = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var eventRepo     = scope.ServiceProvider.GetRequiredService<MatchEventRepository>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now      = DateTime.UtcNow;
        var windowLo = now.AddHours(-3);
        var windowHi = now.AddMinutes(10);

        var candidates = await matchRepo.GetInWindowAsync(windowLo, windowHi, ct);
        if (candidates.Count == 0) return;

        var favoriteTeamIds = (await userRepo.GetAllFavoriteTeamIdsAsync(ct)).ToHashSet();
        if (favoriteTeamIds.Count == 0) return;

        foreach (var match in candidates)
        {
            if (!favoriteTeamIds.Contains(match.HomeTeamId) &&
                !favoriteTeamIds.Contains(match.AwayTeamId)) continue;

            try
            {
                var teams = await teamRepo.GetManyAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);
                await ProcessMatchAsync(match, teams, eventRepo, notifications, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed processing live events for match {MatchId}", match.MatchId);
            }
        }
    }

    private async Task ProcessMatchAsync(
        MatchDoc match,
        Dictionary<int, TeamDoc> teams,
        MatchEventRepository eventRepo,
        NotificationService notifications,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var userRepo    = scope.ServiceProvider.GetRequiredService<UserRepository>();

        var events = await football.GetMatchEventsAsync(match.MatchId, ct);
        if (events.Count == 0) return;

        var sentSet     = await eventRepo.GetSentKeysAsync(match.MatchId, ct);
        var subscribers = await userRepo.GetSubscriberIdsAsync(new[] { match.HomeTeamId, match.AwayTeamId }, ct);

        if (subscribers.Count == 0) return;

        teams.TryGetValue(match.HomeTeamId, out var homeTeam);
        teams.TryGetValue(match.AwayTeamId, out var awayTeam);
        var homeName = homeTeam?.Name ?? "?";
        var awayName = awayTeam?.Name ?? "?";

        var newEvents = new List<MatchEventDoc>();
        bool halftimeSent = match.HalftimeNotificationSent;

        foreach (var ev in events.OrderBy(e => e.Minute))
        {
            if (sentSet.Contains(ev.EventKey)) continue;

            string? message = ev.Type switch
            {
                MatchEventType.Goal =>
                    MatchesFormatter.FormatGoal(ev, homeName, awayName, emoji),

                MatchEventType.YellowCard or MatchEventType.RedCard =>
                    MatchesFormatter.FormatCard(ev, ResolveTeamName(ev.TeamId, match.HomeTeamId, homeName, match.AwayTeamId, awayName)),

                MatchEventType.HalfTime when !halftimeSent =>
                    BuildHalftimeMessage(ev, events, match, homeName, awayName),

                _ => null
            };

            if (message is null) continue;

            logger.LogInformation("Live event: match={MatchId} type={Type} min={Min}", match.MatchId, ev.Type, ev.Minute);
            await notifications.BroadcastAsync(subscribers, message, ct);

            if (ev.Type == MatchEventType.HalfTime)
            {
                halftimeSent = true;
                using var innerScope = scopeFactory.CreateScope();
                var matchRepo = innerScope.ServiceProvider.GetRequiredService<MatchRepository>();
                await matchRepo.UpdateFieldsAsync(match.MatchId,
                    new Dictionary<string, object?> { ["HalftimeNotificationSent"] = true }, ct);
            }

            newEvents.Add(new MatchEventDoc { MatchId = match.MatchId, EventKey = ev.EventKey, SentAt = DateTime.UtcNow });
            sentSet.Add(ev.EventKey);
        }

        if (newEvents.Count > 0)
            await eventRepo.BatchAddAsync(newEvents, ct);
    }

    private string BuildHalftimeMessage(
        MatchEventDto halftimeEv, IReadOnlyList<MatchEventDto> all,
        MatchDoc match, string homeName, string awayName)
    {
        var hs  = halftimeEv.HomeScore ?? match.HomeScore ?? 0;
        var as_ = halftimeEv.AwayScore ?? match.AwayScore ?? 0;
        var firstHalfGoals = all.Where(e => e.Type == MatchEventType.Goal && e.Minute <= 45).ToList();
        return MatchesFormatter.FormatHalftime(hs, as_, homeName, awayName, firstHalfGoals, emoji);
    }

    private static string ResolveTeamName(int? teamId, int homeId, string homeName, int awayId, string awayName)
    {
        if (teamId == homeId) return homeName;
        if (teamId == awayId) return awayName;
        return "—";
    }
}
