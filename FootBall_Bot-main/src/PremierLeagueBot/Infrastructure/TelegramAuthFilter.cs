using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;

namespace PremierLeagueBot.Infrastructure;

public sealed class TelegramAuthFilter(
    UserRepository userRepo,
    IConfiguration configuration,
    ILogger<TelegramAuthFilter> logger) : IAsyncActionFilter
{
    public const string CurrentUserKey = "TelegramUser";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // ── Option 1: session token (cached after first login) ───────────────
        var sessionToken = context.HttpContext.Request.Headers["X-Session-Token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(sessionToken))
        {
            var userByToken = await userRepo.GetBySessionTokenAsync(sessionToken);
            if (userByToken is not null)
            {
                if (userByToken.SessionTokenExpiresAt.HasValue && userByToken.SessionTokenExpiresAt < DateTime.UtcNow)
                {
                    context.Result = new UnauthorizedObjectResult(new { error = "Session expired, please reopen the app." });
                    return;
                }
                context.HttpContext.Items[CurrentUserKey] = userByToken;
                await next();
                return;
            }
        }

        // ── Option 2: Telegram initData (HMAC validation) ────────────────────
        var initData = context.HttpContext.Request.Headers["X-Telegram-Init-Data"].FirstOrDefault();

        if (string.IsNullOrEmpty(initData))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Not authenticated. Please reopen the app from the bot." });
            return;
        }

        var botToken = (configuration["BotToken"] ?? "").Trim();
        var isDev    = context.HttpContext.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        if (!TelegramInitDataValidator.TryValidate(initData, botToken, out var parsed) && !isDev)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid initData" });
            return;
        }

        if (parsed.TelegramId == 0 && isDev)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid initData (dev mode)" });
            return;
        }

        var user = await userRepo.GetByIdAsync(parsed.TelegramId);

        if (user is null)
        {
            user = new UserDoc
            {
                TelegramId   = parsed.TelegramId,
                FirstName    = parsed.FirstName,
                Username     = parsed.Username,
                LanguageCode = parsed.LanguageCode,
                RegisteredAt = DateTime.UtcNow
            };
            await userRepo.UpsertAsync(user);
            logger.LogInformation("MiniApp: new user {Id} ({Name})", user.TelegramId, user.FirstName);
        }
        else
        {
            var changed = user.FirstName    != parsed.FirstName    ||
                          user.Username     != parsed.Username     ||
                          user.LanguageCode != parsed.LanguageCode;
            if (changed)
            {
                user.FirstName    = parsed.FirstName;
                user.Username     = parsed.Username;
                user.LanguageCode = parsed.LanguageCode;
                await userRepo.UpsertAsync(user);
            }
        }

        context.HttpContext.Items[CurrentUserKey] = user;
        await next();
    }
}
