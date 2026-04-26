using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class UserRepository(FirestoreDb db)
{
    private const string Col = "users";

    public async Task<UserDoc?> GetByIdAsync(long telegramId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col).Document(telegramId.ToString()).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<UserDoc>() : null;
    }

    public async Task<UserDoc?> GetBySessionTokenAsync(string token, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("SessionToken", token)
            .Limit(1)
            .GetSnapshotAsync(ct);
        return snap.Count > 0 ? snap[0].ConvertTo<UserDoc>() : null;
    }

    public async Task<UserDoc?> GetByUsernameLowerAsync(string usernameLower, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("UsernameLower", usernameLower)
            .Limit(1)
            .GetSnapshotAsync(ct);
        return snap.Count > 0 ? snap[0].ConvertTo<UserDoc>() : null;
    }

    /// <summary>Returns TelegramIds of all users whose favorite team is in the given set.</summary>
    public async Task<List<long>> GetSubscriberIdsAsync(IEnumerable<int> teamIds, CancellationToken ct = default)
    {
        var ids = teamIds.ToList();
        if (ids.Count == 0) return [];

        // WhereIn supports up to 10 values
        var snap = await db.Collection(Col)
            .WhereIn("FavoriteTeamId", ids.Cast<object>().ToList())
            .GetSnapshotAsync(ct);

        return snap.Select(d => d.ConvertTo<UserDoc>().TelegramId).Distinct().ToList();
    }

    /// <summary>Returns distinct FavoriteTeamIds across all users (nulls excluded).</summary>
    public async Task<List<int>> GetAllFavoriteTeamIdsAsync(CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereGreaterThan("FavoriteTeamId", 0)
            .Select("FavoriteTeamId")
            .GetSnapshotAsync(ct);

        return snap
            .Select(d => d.ConvertTo<UserDoc>().FavoriteTeamId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }

    public async Task<Dictionary<long, UserDoc>> GetManyAsync(IEnumerable<long> telegramIds, CancellationToken ct = default)
    {
        var ids = telegramIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var refs = ids.Select(id => db.Collection(Col).Document(id.ToString())).ToArray();
        var snaps = await db.GetAllSnapshotsAsync(refs, ct);
        return snaps
            .Where(s => s.Exists)
            .Select(s => s.ConvertTo<UserDoc>())
            .ToDictionary(u => u.TelegramId);
    }

    public async Task UpsertAsync(UserDoc user, CancellationToken ct = default)
    {
        user.UsernameLower = user.Username?.ToLower();
        await db.Collection(Col).Document(user.TelegramId.ToString()).SetAsync(user, cancellationToken: ct);
    }

    public async Task UpdateFieldsAsync(long telegramId, Dictionary<string, object?> fields, CancellationToken ct = default)
    {
        var updates = fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? (object)FieldValue.Delete);
        await db.Collection(Col).Document(telegramId.ToString())
            .UpdateAsync(updates!, cancellationToken: ct);
    }
}
