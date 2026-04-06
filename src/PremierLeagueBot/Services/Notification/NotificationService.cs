using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using Telegram.Bot;

namespace PremierLeagueBot.Services.Notification;

/// <summary>
/// Handles sending notifications to users and persisting notification logs.
/// </summary>
public sealed class NotificationService(
    ITelegramBotClient bot,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<NotificationService> logger)
{
    public async Task SendToUserAsync(long telegramId, string html, CancellationToken ct = default)
    {
        try
        {
            await bot.SendMessage(
                chatId: telegramId,
                text: html,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                cancellationToken: ct);

            await LogAsync(telegramId, html, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send notification to {TelegramId}", telegramId);
        }
    }

    public async Task BroadcastAsync(IEnumerable<long> telegramIds, string html, CancellationToken ct = default)
    {
        foreach (var id in telegramIds)
        {
            await SendToUserAsync(id, html, ct);
            await Task.Delay(50, ct); // Respect Telegram rate-limit (30 msg/s)
        }
    }

    private async Task LogAsync(long telegramId, string message, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.NotificationLogs.Add(new NotificationLog
        {
            TelegramId = telegramId,
            Message    = message.Length > 500 ? message[..500] : message,
            SentAt     = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
