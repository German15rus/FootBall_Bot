using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class MatchEventRepository(FirestoreDb db)
{
    private static CollectionReference Events(FirestoreDb db, int matchId) =>
        db.Collection("matches").Document(matchId.ToString()).Collection("events");

    public async Task<HashSet<string>> GetSentKeysAsync(int matchId, CancellationToken ct = default)
    {
        var snap = await Events(db, matchId)
            .Select(FieldPath.DocumentId)
            .GetSnapshotAsync(ct);
        return snap.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
    }

    public async Task BatchAddAsync(IEnumerable<MatchEventDoc> events, CancellationToken ct = default)
    {
        var list = events.ToList();
        if (list.Count == 0) return;

        var batch = db.StartBatch();
        foreach (var ev in list)
            batch.Set(Events(db, ev.MatchId).Document(ev.EventKey), ev);
        await batch.CommitAsync(ct);
    }
}
