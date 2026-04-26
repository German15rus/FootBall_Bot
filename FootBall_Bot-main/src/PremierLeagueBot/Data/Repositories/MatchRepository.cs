using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class MatchRepository(FirestoreDb db)
{
    private const string Col = "matches";

    public async Task<MatchDoc?> GetByIdAsync(int matchId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col).Document(matchId.ToString()).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<MatchDoc>() : null;
    }

    public async Task<Dictionary<int, MatchDoc>> GetManyAsync(IEnumerable<int> matchIds, CancellationToken ct = default)
    {
        var ids = matchIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var refs = ids.Select(id => db.Collection(Col).Document(id.ToString())).ToArray();
        var snaps = await db.GetAllSnapshotsAsync(refs, ct);
        return snaps
            .Where(s => s.Exists)
            .Select(s => s.ConvertTo<MatchDoc>())
            .ToDictionary(m => m.MatchId);
    }

    /// <summary>Returns matches in a date range with given status and competitionId.</summary>
    public async Task<List<MatchDoc>> GetUpcomingAsync(
        DateTime from, DateTime to, string status, int competitionId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("Status", status)
            .WhereEqualTo("CompetitionId", competitionId)
            .WhereGreaterThanOrEqualTo("MatchDate", from)
            .WhereLessThanOrEqualTo("MatchDate", to)
            .GetSnapshotAsync(ct);

        return snap.Select(d => d.ConvertTo<MatchDoc>()).OrderBy(m => m.MatchDate).ToList();
    }

    /// <summary>Returns live/recent matches in a time window (for live notifications).</summary>
    public async Task<List<MatchDoc>> GetInWindowAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereGreaterThanOrEqualTo("MatchDate", from)
            .WhereLessThanOrEqualTo("MatchDate", to)
            .GetSnapshotAsync(ct);

        return snap
            .Select(d => d.ConvertTo<MatchDoc>())
            .Where(m => m.Status != "finished")
            .ToList();
    }

    /// <summary>Returns scheduled matches whose kick-off is within the next window (pre-match reminder).</summary>
    public async Task<List<MatchDoc>> GetScheduledBeforeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("Status", "scheduled")
            .WhereEqualTo("PreMatchNotificationSent", false)
            .WhereGreaterThanOrEqualTo("MatchDate", from)
            .WhereLessThanOrEqualTo("MatchDate", to)
            .GetSnapshotAsync(ct);

        return snap.Select(d => d.ConvertTo<MatchDoc>()).ToList();
    }

    /// <summary>Returns finished matches where post-match notification was not yet sent.</summary>
    public async Task<List<MatchDoc>> GetFinishedUnnotifiedAsync(CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("Status", "finished")
            .WhereEqualTo("PostMatchNotificationSent", false)
            .GetSnapshotAsync(ct);

        return snap.Select(d => d.ConvertTo<MatchDoc>()).ToList();
    }

    /// <summary>Returns all finished matches (for scoring unscored predictions).</summary>
    public async Task<List<MatchDoc>> GetFinishedWithScoresAsync(CancellationToken ct = default)
    {
        var snap = await db.Collection(Col)
            .WhereEqualTo("Status", "finished")
            .GetSnapshotAsync(ct);

        return snap
            .Select(d => d.ConvertTo<MatchDoc>())
            .Where(m => m.HomeScore.HasValue && m.AwayScore.HasValue)
            .ToList();
    }

    public async Task UpsertAsync(MatchDoc match, CancellationToken ct = default)
    {
        await db.Collection(Col).Document(match.MatchId.ToString()).SetAsync(match, cancellationToken: ct);
    }

    public async Task UpdateFieldsAsync(int matchId, Dictionary<string, object?> fields, CancellationToken ct = default)
    {
        var updates = fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? (object)FieldValue.Delete);
        await db.Collection(Col).Document(matchId.ToString())
            .UpdateAsync(updates!, cancellationToken: ct);
    }
}
