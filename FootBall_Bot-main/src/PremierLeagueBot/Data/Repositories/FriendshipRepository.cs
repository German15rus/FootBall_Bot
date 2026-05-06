using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class FriendshipRepository(FirestoreDb db)
{
    private const string Col = "friendships";

    public static string DocId(long requesterId, long addresseeId) => $"{requesterId}_{addresseeId}";

    /// <summary>Returns accepted friendships where user is requester OR addressee.</summary>
    public async Task<List<FriendshipDoc>> GetAcceptedAsync(long userId, CancellationToken ct = default)
    {
        // Firestore doesn't support OR on different fields — run two queries
        var t1 = db.Collection(Col)
            .WhereEqualTo("RequesterId", userId)
            .WhereEqualTo("Status", "accepted")
            .GetSnapshotAsync(ct);

        var t2 = db.Collection(Col)
            .WhereEqualTo("AddresseeId", userId)
            .WhereEqualTo("Status", "accepted")
            .GetSnapshotAsync(ct);

        await Task.WhenAll(t1, t2);

        return t1.Result.Concat(t2.Result)
            .Select(d => d.ConvertTo<FriendshipDoc>())
            .ToList();
    }

    /// <summary>Returns pending requests where user is the addressee.</summary>
    public async Task<List<FriendshipDoc>> GetPendingRequestsAsync(long userId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("AddresseeId", userId)
            .WhereEqualTo("Status", "pending")
            .OrderByDescending("CreatedAt")
            .GetSnapshotAsync(ct);

        return snap.Select(d => d.ConvertTo<FriendshipDoc>()).ToList();
    }

    /// <summary>Counts accepted friendships for a user (both directions).</summary>
    public async Task<int> CountAcceptedAsync(long userId, CancellationToken ct = default)
    {
        var friends = await GetAcceptedAsync(userId, ct);
        return friends.Count;
    }

    /// <summary>Checks both directions — returns the existing friendship if found.</summary>
    public async Task<FriendshipDoc?> FindExistingAsync(long userA, long userB, CancellationToken ct = default)
    {
        var ref1 = db.Collection(Col).Document(DocId(userA, userB));
        var ref2 = db.Collection(Col).Document(DocId(userB, userA));
        var snaps = await db.GetAllSnapshotsAsync(new[] { ref1, ref2 }, ct);
        var existing = snaps.FirstOrDefault(s => s.Exists);
        return existing?.ConvertTo<FriendshipDoc>();
    }

    /// <summary>Returns friendship sent by requesterId to addresseeId (one direction).</summary>
    public async Task<FriendshipDoc?> GetAsync(long requesterId, long addresseeId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col).Document(DocId(requesterId, addresseeId)).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<FriendshipDoc>() : null;
    }

    public async Task CreateAsync(FriendshipDoc friendship, CancellationToken ct = default)
    {
        var id = DocId(friendship.RequesterId, friendship.AddresseeId);
        await db.Collection(Col).Document(id).SetAsync(friendship, cancellationToken: ct);
    }

    public async Task AcceptAsync(long requesterId, long addresseeId, CancellationToken ct = default)
    {
        await db.Collection(Col).Document(DocId(requesterId, addresseeId))
            .UpdateAsync("Status", "accepted", cancellationToken: ct);
    }

    public async Task DeleteAsync(long requesterId, long addresseeId, CancellationToken ct = default)
    {
        await db.Collection(Col).Document(DocId(requesterId, addresseeId))
            .DeleteAsync(cancellationToken: ct);
    }
}
