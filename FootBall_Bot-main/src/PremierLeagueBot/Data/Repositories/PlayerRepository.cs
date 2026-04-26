using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class PlayerRepository(FirestoreDb db)
{
    private const string Col = "players";

    public async Task<List<PlayerDoc>> GetByTeamAsync(int teamId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("TeamId", teamId)
            .GetSnapshotAsync(ct);
        return snap.Select(d => d.ConvertTo<PlayerDoc>()).ToList();
    }

    public async Task DeleteByTeamAsync(int teamId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("TeamId", teamId)
            .Select(FieldPath.DocumentId)
            .GetSnapshotAsync(ct);

        // Batch delete (max 500 per batch)
        const int batchSize = 500;
        for (int i = 0; i < snap.Count; i += batchSize)
        {
            var batch = db.StartBatch();
            foreach (var doc in snap.Skip(i).Take(batchSize))
                batch.Delete(doc.Reference);
            await batch.CommitAsync(ct);
        }
    }

    public async Task UpsertAsync(PlayerDoc player, CancellationToken ct = default)
    {
        await db.Collection(Col).Document(player.PlayerId.ToString()).SetAsync(player, cancellationToken: ct);
    }

    public async Task BatchUpsertAsync(IEnumerable<PlayerDoc> players, CancellationToken ct = default)
    {
        var list = players.ToList();
        const int batchSize = 500;
        for (int i = 0; i < list.Count; i += batchSize)
        {
            var batch = db.StartBatch();
            foreach (var p in list.Skip(i).Take(batchSize))
                batch.Set(db.Collection(Col).Document(p.PlayerId.ToString()), p);
            await batch.CommitAsync(ct);
        }
    }
}
