using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Services.Football;
using PremierLeagueBot.Services.Notification;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Background service that checks for new news articles once per hour
/// and sends relevant news to users who have subscribed to a favourite team.
/// </summary>
public sealed class NewsNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<NewsNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NewsNotificationService started");

        // Initial delay – start news checks after other services have settled
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try   { await CheckAndSendNewsAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            { logger.LogError(ex, "Error in NewsNotificationService"); }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckAndSendNewsAsync(CancellationToken ct)
    {
        using var scope   = scopeFactory.CreateScope();
        var db            = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var football      = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // Find distinct team IDs that have at least one subscriber
        var teamIds = await db.Users
            .Where(u => u.FavoriteTeamId.HasValue)
            .Select(u => u.FavoriteTeamId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var sentCount = 0;

        foreach (var teamId in teamIds)
        {
            ct.ThrowIfCancellationRequested();

            var teamName = (await db.Teams.FindAsync([teamId], ct))?.Name ?? "";

            var news = await football.GetNewsAsync(teamId, ct);
            if (news.Count == 0) continue;

            // Filter: published in the last hour AND mentions the team name
            var fresh = news
                .Where(n => n.PublishedAt >= DateTime.UtcNow.AddHours(-1)
                         && (string.IsNullOrEmpty(teamName)
                             || n.Title.Contains(teamName, StringComparison.OrdinalIgnoreCase)
                             || n.Summary.Contains(teamName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (fresh.Count == 0) continue;

            var subscribers = await db.Users
                .Where(u => u.FavoriteTeamId == teamId)
                .Select(u => u.TelegramId)
                .ToListAsync(ct);

            foreach (var article in fresh)
            {
                var message =
                    $"📰 <b>{teamName}</b>\n\n" +
                    $"<b>{article.Title}</b>\n" +
                    $"{article.Summary}\n\n" +
                    $"<a href=\"{article.Url}\">Читать далее →</a>";

                await notifications.BroadcastAsync(subscribers, message, ct);
                sentCount++;
            }

            // Avoid hitting rate limits across multiple teams
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        if (sentCount > 0)
            logger.LogInformation("Sent {Count} news notifications", sentCount);
    }
}
