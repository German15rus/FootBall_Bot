using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using Telegram.Bot;

namespace PremierLeagueBot.Services.TelegramAvatar;

/// <summary>
/// Fetches the user's Telegram profile photo and stores the URL in the DB.
/// Called from AuthController after login so the avatar is always fresh.
/// </summary>
public sealed class AvatarService(
    ITelegramBotClient bot,
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<AvatarService> logger)
{
    public async Task RefreshAvatarAsync(long telegramId, CancellationToken ct = default)
    {
        try
        {
            var photos = await bot.GetUserProfilePhotos(telegramId, limit: 1, cancellationToken: ct);
            if (photos.TotalCount == 0) return;

            // Get the smallest thumbnail (last in the sizes array) to save bandwidth
            var photo = photos.Photos[0].LastOrDefault() ?? photos.Photos[0].First();
            var file  = await bot.GetFile(photo.FileId, ct);

            if (file.FilePath is null) return;

            var botToken = configuration["BotToken"] ?? "";
            var url = $"https://api.telegram.org/file/bot{botToken}/{file.FilePath}";

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var user = await db.Users.FindAsync([telegramId], ct);
            if (user is not null && user.AvatarUrl != url)
            {
                user.AvatarUrl = url;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Non-critical: missing avatar is acceptable
            logger.LogWarning(ex, "Failed to refresh avatar for user {Id}", telegramId);
        }
    }
}
