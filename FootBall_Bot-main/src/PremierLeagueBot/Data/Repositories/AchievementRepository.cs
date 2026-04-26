using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class AchievementRepository(FirestoreDb db)
{
    private const string AchCol = "achievements";
    private const string UserAchSubCol = "achievements";

    public async Task<AchievementDoc?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var snap = await db.Collection(AchCol).Document(code).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<AchievementDoc>() : null;
    }

    public async Task<List<AchievementDoc>> GetAllAsync(CancellationToken ct = default)
    {
        var snap = await db.Collection(AchCol).GetSnapshotAsync(ct);
        return snap.Select(d => d.ConvertTo<AchievementDoc>()).ToList();
    }

    public async Task<bool> ExistsAsync(string code, CancellationToken ct = default)
    {
        var snap = await db.Collection(AchCol).Document(code).GetSnapshotAsync(ct);
        return snap.Exists;
    }

    public async Task UpsertAchievementAsync(AchievementDoc achievement, CancellationToken ct = default)
    {
        await db.Collection(AchCol).Document(achievement.Code).SetAsync(achievement, cancellationToken: ct);
    }

    // ── User achievements (subcollection: users/{telegramId}/achievements/{code}) ──

    public async Task<List<UserAchievementDoc>> GetUserAchievementsAsync(long telegramId, CancellationToken ct = default)
    {
        var snap = await db.Collection("users").Document(telegramId.ToString())
            .Collection(UserAchSubCol)
            .GetSnapshotAsync(ct);
        return snap.Select(d => d.ConvertTo<UserAchievementDoc>())
            .OrderBy(ua => ua.EarnedAt)
            .ToList();
    }

    public async Task<HashSet<string>> GetEarnedCodesAsync(long telegramId, CancellationToken ct = default)
    {
        var snap = await db.Collection("users").Document(telegramId.ToString())
            .Collection(UserAchSubCol)
            .Select(FieldPath.DocumentId)
            .GetSnapshotAsync(ct);
        return snap.Select(d => d.Id).ToHashSet();
    }

    public async Task GrantAsync(UserAchievementDoc ua, CancellationToken ct = default)
    {
        await db.Collection("users").Document(ua.TelegramId.ToString())
            .Collection(UserAchSubCol).Document(ua.AchievementCode)
            .SetAsync(ua, cancellationToken: ct);
    }
}
