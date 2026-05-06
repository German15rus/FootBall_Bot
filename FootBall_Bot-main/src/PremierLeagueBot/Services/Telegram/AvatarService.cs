using PremierLeagueBot.Data.Repositories;
using Telegram.Bot;

namespace PremierLeagueBot.Services.TelegramAvatar;

public sealed class AvatarService(
    ITelegramBotClient bot,
    UserRepository userRepo,
    IConfiguration configuration,
    ILogger<AvatarService> logger)
{
    public async Task RefreshAvatarAsync(long telegramId, CancellationToken ct = default)
    {
        try
        {
            var photos = await bot.GetUserProfilePhotos(telegramId, limit: 1, cancellationToken: ct);
            if (photos.TotalCount == 0) return;

            var photo = photos.Photos[0].LastOrDefault() ?? photos.Photos[0].First();
            var file  = await bot.GetFile(photo.FileId, ct);
            if (file.FilePath is null) return;

            var botToken = configuration["BotToken"] ?? "";
            var url      = $"https://api.telegram.org/file/bot{botToken}/{file.FilePath}";

            var user = await userRepo.GetByIdAsync(telegramId, ct);
            if (user is not null && user.AvatarUrl != url)
            {
                user.AvatarUrl = url;
                await userRepo.UpsertAsync(user, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh avatar for user {Id}", telegramId);
        }
    }
}
