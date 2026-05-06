using Microsoft.AspNetCore.Mvc;
using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Infrastructure;

namespace PremierLeagueBot.Controllers;

[ApiController]
[Route("api/friends")]
[ServiceFilter(typeof(TelegramAuthFilter))]
public sealed class FriendsController(
    FriendshipRepository friendRepo,
    UserRepository userRepo) : ControllerBase
{
    private UserDoc CurrentUser => (UserDoc)HttpContext.Items[TelegramAuthFilter.CurrentUserKey]!;

    [HttpGet]
    public async Task<IActionResult> GetFriends(CancellationToken ct)
    {
        var me          = CurrentUser.TelegramId;
        var friendships = await friendRepo.GetAcceptedAsync(me, ct);

        var friendIds = friendships
            .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
            .Distinct()
            .ToList();

        var userMap = await userRepo.GetManyAsync(friendIds, ct);

        return Ok(friendIds
            .Where(id => userMap.ContainsKey(id))
            .Select(id => userMap[id])
            .Select(u => new
            {
                telegramId = u.TelegramId,
                firstName  = u.FirstName,
                username   = u.Username,
                avatarUrl  = u.AvatarUrl
            }));
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests(CancellationToken ct)
    {
        var me       = CurrentUser.TelegramId;
        var requests = await friendRepo.GetPendingRequestsAsync(me, ct);

        var requesterIds = requests.Select(f => f.RequesterId).Distinct().ToList();
        var userMap      = await userRepo.GetManyAsync(requesterIds, ct);

        return Ok(requests
            .Where(f => userMap.ContainsKey(f.RequesterId))
            .Select(f =>
            {
                var u = userMap[f.RequesterId];
                return new
                {
                    // id = requesterId so frontend can pass it to accept/decline endpoints
                    id         = f.RequesterId,
                    telegramId = u.TelegramId,
                    firstName  = u.FirstName,
                    username   = u.Username,
                    avatarUrl  = u.AvatarUrl
                };
            }));
    }

    [HttpPost("request/{username}")]
    public async Task<IActionResult> SendRequest(string username, CancellationToken ct)
    {
        var me        = CurrentUser.TelegramId;
        var cleanName = username.TrimStart('@').ToLower();
        var target    = await userRepo.GetByUsernameLowerAsync(cleanName, ct);

        if (target is null)
            return NotFound(new { error = "Пользователь не найден" });

        if (target.TelegramId == me)
            return BadRequest(new { error = "Нельзя добавить себя" });

        var existing = await friendRepo.FindExistingAsync(me, target.TelegramId, ct);
        if (existing is not null)
        {
            var msg = existing.Status == "accepted" ? "Уже в друзьях" : "Заявка уже отправлена";
            return Conflict(new { error = msg });
        }

        await friendRepo.CreateAsync(new FriendshipDoc
        {
            RequesterId = me,
            AddresseeId = target.TelegramId,
            Status      = "pending",
            CreatedAt   = DateTime.UtcNow
        }, ct);

        return Ok(new { message = "Заявка отправлена" });
    }

    /// <summary>Accept the pending request from requesterId.</summary>
    [HttpPost("accept/{requesterId:long}")]
    public async Task<IActionResult> AcceptRequest(long requesterId, CancellationToken ct)
    {
        var me         = CurrentUser.TelegramId;
        var friendship = await friendRepo.GetAsync(requesterId, me, ct);

        if (friendship is null || friendship.Status != "pending")
            return NotFound(new { error = "Заявка не найдена" });

        await friendRepo.AcceptAsync(requesterId, me, ct);
        return Ok(new { message = "Заявка принята" });
    }

    /// <summary>Decline the pending request from requesterId.</summary>
    [HttpPost("decline/{requesterId:long}")]
    public async Task<IActionResult> DeclineRequest(long requesterId, CancellationToken ct)
    {
        var me         = CurrentUser.TelegramId;
        var friendship = await friendRepo.GetAsync(requesterId, me, ct);

        if (friendship is null || friendship.Status != "pending")
            return NotFound(new { error = "Заявка не найдена" });

        await friendRepo.DeleteAsync(requesterId, me, ct);
        return Ok(new { message = "Заявка отклонена" });
    }
}
