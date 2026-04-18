using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using PremierLeagueBot.Infrastructure;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/friends")]
[ServiceFilter(typeof(TelegramAuthFilter))]
public sealed class FriendsController(IDbContextFactory<AppDbContext> dbFactory) : ControllerBase
{
    private User CurrentUser => (User)HttpContext.Items[TelegramAuthFilter.CurrentUserKey]!;

    /// <summary>Returns accepted friends of the current user.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFriends(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var me = CurrentUser.TelegramId;

        var friendships = await db.Friendships
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => (f.RequesterId == me || f.AddresseeId == me) && f.Status == "accepted")
            .ToListAsync(ct);

        var friends = friendships.Select(f =>
        {
            var friend = f.RequesterId == me ? f.Addressee : f.Requester;
            return new
            {
                telegramId = friend.TelegramId,
                firstName  = friend.FirstName,
                username   = friend.Username,
                avatarUrl  = friend.AvatarUrl
            };
        });

        return Ok(friends);
    }

    /// <summary>Returns incoming pending friend requests.</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var me = CurrentUser.TelegramId;

        var requests = await db.Friendships
            .Include(f => f.Requester)
            .Where(f => f.AddresseeId == me && f.Status == "pending")
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);

        return Ok(requests.Select(f => new
        {
            id         = f.Id,
            telegramId = f.Requester.TelegramId,
            firstName  = f.Requester.FirstName,
            username   = f.Requester.Username,
            avatarUrl  = f.Requester.AvatarUrl
        }));
    }

    /// <summary>Sends a friend request to a user found by @username.</summary>
    [HttpPost("request/{username}")]
    public async Task<IActionResult> SendRequest(string username, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var me = CurrentUser.TelegramId;

        var cleanName = username.TrimStart('@').ToLower();
        var target = await db.Users
            .FirstOrDefaultAsync(u => u.Username != null &&
                u.Username.ToLower() == cleanName, ct);

        if (target is null)
            return NotFound(new { error = "Пользователь не найден" });

        if (target.TelegramId == me)
            return BadRequest(new { error = "Нельзя добавить себя" });

        var existing = await db.Friendships.FirstOrDefaultAsync(
            f => (f.RequesterId == me && f.AddresseeId == target.TelegramId) ||
                 (f.RequesterId == target.TelegramId && f.AddresseeId == me), ct);

        if (existing is not null)
        {
            var errorMsg = existing.Status == "accepted"
                ? "Уже в друзьях"
                : "Заявка уже отправлена";
            return Conflict(new { error = errorMsg });
        }

        db.Friendships.Add(new Friendship
        {
            RequesterId = me,
            AddresseeId = target.TelegramId
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Заявка отправлена" });
    }

    /// <summary>Accepts an incoming friend request.</summary>
    [HttpPost("accept/{id:int}")]
    public async Task<IActionResult> AcceptRequest(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var me = CurrentUser.TelegramId;

        var friendship = await db.Friendships
            .FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == me && f.Status == "pending", ct);

        if (friendship is null)
            return NotFound(new { error = "Заявка не найдена" });

        friendship.Status = "accepted";
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Заявка принята" });
    }

    /// <summary>Declines and removes an incoming friend request.</summary>
    [HttpPost("decline/{id:int}")]
    public async Task<IActionResult> DeclineRequest(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var me = CurrentUser.TelegramId;

        var friendship = await db.Friendships
            .FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == me && f.Status == "pending", ct);

        if (friendship is null)
            return NotFound(new { error = "Заявка не найдена" });

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Заявка отклонена" });
    }
}
