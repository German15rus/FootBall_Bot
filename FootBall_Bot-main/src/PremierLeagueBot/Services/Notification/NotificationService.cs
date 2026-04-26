using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using Telegram.Bot;

namespace PremierLeagueBot.Services.Notification;

public sealed class NotificationService(
    ITelegramBotClient bot,
    IServiceScopeFactory scopeFactory,
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
            await Task.Delay(50, ct);
        }
    }

    private async Task LogAsync(long telegramId, string message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<NotificationLogRepository>();
        await repo.AddAsync(new NotificationLogDoc
        {
            TelegramId = telegramId,
            Message    = message.Length > 500 ? message[..500] : message,
            SentAt     = DateTime.UtcNow
        }, ct);
    }
}
