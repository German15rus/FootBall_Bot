using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PremierLeagueBot.Services.Bot;

/// <summary>
/// Hosted service that runs the Telegram bot via Long Polling.
/// Automatically reconnects if the polling session drops.
/// </summary>
public sealed class BotHostedService(
    ITelegramBotClient bot,
    UpdateHandler handler,
    ILogger<BotHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until we can reach the Telegram API
        User? me = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                me = await bot.GetMe(stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Cannot reach Telegram API, retrying in 10 s…");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        if (me is null) return;

        logger.LogInformation("Bot started: @{Username} (id={Id})", me.Username, me.Id);

        // IMPORTANT: delete any existing webhook so Telegram delivers updates
        // via long polling (getUpdates) instead of pushing to a webhook URL.
        await bot.DeleteWebhook(dropPendingUpdates: false, cancellationToken: stoppingToken);
        logger.LogInformation("Webhook deleted — long polling is now active");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            // Do NOT drop pending updates — /start sent while the app is
            // starting must still be delivered once polling begins.
            DropPendingUpdates = false,
        };

        Func<ITelegramBotClient, Update, CancellationToken, Task> updateHandler =
            async (_, update, ct) =>
            {
                logger.LogDebug("Received {Type} update {Id}", update.Type, update.Id);
                await handler.HandleAsync(update, ct);
            };

        Func<ITelegramBotClient, Exception, CancellationToken, Task> errorHandler =
            (_, exception, ct) =>
            {
                if (!ct.IsCancellationRequested)
                    logger.LogError(exception, "Telegram polling error");
                return Task.CompletedTask;
            };

        // Restart polling automatically if the session drops (network issues, etc.)
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await bot.ReceiveAsync(
                    updateHandler:     updateHandler,
                    errorHandler:      errorHandler,
                    receiverOptions:   receiverOptions,
                    cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // clean shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReceiveAsync terminated unexpectedly, restarting in 5 s…");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("Bot polling stopped");
    }
}
