using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
#pragma warning disable CS0168

namespace PremierLeagueBot.Services.Bot;

/// <summary>
/// Hosted service that starts the Telegram bot using Long Polling.
/// Runs in the background for the entire application lifetime.
/// </summary>
public sealed class BotHostedService(
    ITelegramBotClient bot,
    UpdateHandler handler,
    ILogger<BotHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await bot.GetMe(stoppingToken);
        logger.LogInformation("Bot started: @{Username} (id={Id})", me.Username, me.Id);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = true   // skip messages sent while the bot was offline
        };

        Func<ITelegramBotClient, Update, CancellationToken, Task> updateHandler =
            async (_, update, ct) =>
            {
                logger.LogDebug("Received {Type} update {Id}", update.Type, update.Id);
                await handler.HandleAsync(update, ct);
            };

        Func<ITelegramBotClient, Exception, CancellationToken, Task> errorHandler =
            (_, exception, _) =>
            {
                logger.LogError(exception, "Telegram polling error");
                return Task.CompletedTask;
            };

        await bot.ReceiveAsync(
            updateHandler:     updateHandler,
            errorHandler:      errorHandler,
            receiverOptions:   receiverOptions,
            cancellationToken: stoppingToken);
    }
}
