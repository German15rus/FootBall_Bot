using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;

namespace PremierLeagueBot.Infrastructure;

/// <summary>
/// Action filter that validates the Telegram initData header and sets CurrentUser in HttpContext.Items.
/// Apply [ServiceFilter(typeof(TelegramAuthFilter))] on controllers or actions that require auth.
/// </summary>
public sealed class TelegramAuthFilter(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration configuration,
    ILogger<TelegramAuthFilter> logger) : IAsyncActionFilter
{
    public const string CurrentUserKey = "TelegramUser";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var initData = context.HttpContext.Request.Headers["X-Telegram-Init-Data"].FirstOrDefault();

        if (string.IsNullOrEmpty(initData))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing X-Telegram-Init-Data header" });
            return;
        }

        var botToken = (configuration["BotToken"] ?? "").Trim();

        // In Development, skip time-based expiry check by accepting test initData
        var isDev = context.HttpContext.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        if (!TelegramInitDataValidator.TryValidate(initData, botToken, out var parsed) && !isDev)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid initData" });
            return;
        }

        // In development, allow a fake userId for testing if validation failed
        if (parsed.TelegramId == 0 && isDev)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid initData (dev mode)" });
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(parsed.TelegramId);

        if (user is null)
        {
            // Auto-create user on first API call (mirrors bot behavior)
            user = new User
            {
                TelegramId   = parsed.TelegramId,
                FirstName    = parsed.FirstName,
                Username     = parsed.Username,
                LanguageCode = parsed.LanguageCode,
                RegisteredAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            logger.LogInformation("MiniApp: new user {Id} ({Name})", user.TelegramId, user.FirstName);
        }
        else
        {
            // Keep profile data fresh on every login
            var changed = false;
            if (user.FirstName != parsed.FirstName)   { user.FirstName    = parsed.FirstName;    changed = true; }
            if (user.Username  != parsed.Username)     { user.Username     = parsed.Username;     changed = true; }
            if (user.LanguageCode != parsed.LanguageCode) { user.LanguageCode = parsed.LanguageCode; changed = true; }
            if (changed) await db.SaveChangesAsync();
        }

        context.HttpContext.Items[CurrentUserKey] = user;
        await next();
    }
}
