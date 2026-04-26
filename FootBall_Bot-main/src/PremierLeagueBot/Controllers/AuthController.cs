using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Infrastructure;
using PremierLeagueBot.Services.Achievements;
using PremierLeagueBot.Services.TelegramAvatar;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserRepository userRepo,
    AvatarService avatarService,
    AchievementService achievementService,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InitData))
            return BadRequest(new { error = "initData is required" });

        var botToken = (configuration["BotToken"] ?? "").Trim();
        var isDev    = HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        if (!TelegramInitDataValidator.TryValidate(request.InitData, botToken, out var parsed) && !isDev)
            return Unauthorized(new { error = "Invalid initData" });

        if (parsed.TelegramId == 0)
            return Unauthorized(new { error = "Could not parse user from initData" });

        var user = await userRepo.GetByIdAsync(parsed.TelegramId, ct);
        var sessionToken = Guid.NewGuid().ToString("N");

        if (user is null)
        {
            user = new UserDoc
            {
                TelegramId   = parsed.TelegramId,
                FirstName    = parsed.FirstName,
                Username     = parsed.Username,
                LanguageCode = parsed.LanguageCode,
                RegisteredAt = DateTime.UtcNow,
                SessionToken = sessionToken
            };
            await userRepo.UpsertAsync(user, ct);
            await achievementService.SeedAsync(ct);
            logger.LogInformation("MiniApp login: new user {Id}", parsed.TelegramId);
        }
        else
        {
            user.FirstName    = parsed.FirstName;
            user.Username     = parsed.Username;
            user.LanguageCode = parsed.LanguageCode;
            user.SessionToken = sessionToken;
            await userRepo.UpsertAsync(user, ct);
        }

        _ = avatarService.RefreshAvatarAsync(parsed.TelegramId);

        return Ok(new
        {
            telegramId   = user.TelegramId,
            firstName    = user.FirstName,
            username     = user.Username,
            avatarUrl    = user.AvatarUrl,
            languageCode = user.LanguageCode,
            registeredAt = user.RegisteredAt,
            sessionToken
        });
    }
}

public sealed record LoginRequest(string InitData);
