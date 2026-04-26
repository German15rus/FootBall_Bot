using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Services.Football;
using PremierLeagueBot.Services.Notification;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Checks for new news articles once per hour and sends them to users with a matching favorite team.
/// </summary>
public sealed class NewsNotificationService(
    IServiceScopeFactory scopeFactory,
    ILogger<NewsNotificationService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NewsNotificationService started");
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
        var userRepo      = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var teamRepo      = scope.ServiceProvider.GetRequiredService<TeamRepository>();
        var football      = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var teamIds  = await userRepo.GetAllFavoriteTeamIdsAsync(ct);
        var sentCount = 0;

        foreach (var teamId in teamIds)
        {
            ct.ThrowIfCancellationRequested();

            var teamName = (await teamRepo.GetByIdAsync(teamId, ct))?.Name ?? "";
            var news     = await football.GetNewsAsync(teamId, ct);
            if (news.Count == 0) continue;

            var fresh = news
                .Where(n => n.PublishedAt >= DateTime.UtcNow.AddHours(-1)
                         && (string.IsNullOrEmpty(teamName)
                             || n.Title.Contains(teamName, StringComparison.OrdinalIgnoreCase)
                             || n.Summary.Contains(teamName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (fresh.Count == 0) continue;

            var subscribers = await userRepo.GetSubscriberIdsAsync(new[] { teamId }, ct);

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

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        if (sentCount > 0)
            logger.LogInformation("Sent {Count} news notifications", sentCount);
    }
}
