using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class PredictionRepository(FirestoreDb db)
{
    private const string Col = "predictions";

    public static string DocId(long telegramId, int matchId) => $"{telegramId}_{matchId}";

    public async Task<PredictionDoc?> GetAsync(long telegramId, int matchId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col).Document(DocId(telegramId, matchId)).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<PredictionDoc>() : null;
    }

    /// <summary>
    /// All predictions for a user, ordered by MatchDate descending.
    /// Requires Firestore composite index on (TelegramId, MatchDate).
    /// </summary>
    public async Task<List<PredictionDoc>> GetByUserAsync(long telegramId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("TelegramId", telegramId)
            .GetSnapshotAsync(ct);

        return snap
            .Select(d => d.ConvertTo<PredictionDoc>())
            .OrderByDescending(p => p.MatchDate)
            .ToList();
    }

    /// <summary>
    /// Scored predictions for a user, ordered by MatchDate ascending (for achievement logic).
    /// Requires Firestore composite index on (TelegramId, IsScored).
    /// </summary>
    public async Task<List<PredictionDoc>> GetScoredByUserAsync(long telegramId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("TelegramId", telegramId)
            .WhereEqualTo("IsScored", true)
            .GetSnapshotAsync(ct);

        return snap
            .Select(d => d.ConvertTo<PredictionDoc>())
            .OrderBy(p => p.MatchDate)
            .ToList();
    }

    /// <summary>
    /// Unscored predictions for a specific match (used by scoring service).
    /// Requires Firestore composite index on (MatchId, IsScored).
    /// </summary>
    public async Task<List<PredictionDoc>> GetUnscoredByMatchAsync(int matchId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("MatchId", matchId)
            .WhereEqualTo("IsScored", false)
            .GetSnapshotAsync(ct);

        return snap.Select(d => d.ConvertTo<PredictionDoc>()).ToList();
    }

    public async Task UpsertAsync(PredictionDoc prediction, CancellationToken ct = default)
    {
        var id = DocId(prediction.TelegramId, prediction.MatchId);
        await db.Collection(Col).Document(id).SetAsync(prediction, cancellationToken: ct);
    }

    public async Task BatchUpdateAsync(IEnumerable<PredictionDoc> predictions, CancellationToken ct = default)
    {
        var list = predictions.ToList();
        const int batchSize = 500;
        for (int i = 0; i < list.Count; i += batchSize)
        {
            var batch = db.StartBatch();
            foreach (var p in list.Skip(i).Take(batchSize))
            {
                var docRef = db.Collection(Col).Document(DocId(p.TelegramId, p.MatchId));
                batch.Set(docRef, p);
            }
            await batch.CommitAsync(ct);
        }
    }
}
