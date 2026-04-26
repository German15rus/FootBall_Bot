using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class TeamRepository(FirestoreDb db)
{
    private const string Col = "teams";

    public async Task<TeamDoc?> GetByIdAsync(int teamId, CancellationToken ct = default)
    {
        var snap = await db.Collection(Col).Document(teamId.ToString()).GetSnapshotAsync(ct);
        return snap.Exists ? snap.ConvertTo<TeamDoc>() : null;
    }

    public async Task<Dictionary<int, TeamDoc>> GetManyAsync(IEnumerable<int> teamIds, CancellationToken ct = default)
    {
        var ids = teamIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        var refs = ids.Select(id => db.Collection(Col).Document(id.ToString())).ToArray();
        var snaps = await db.GetAllSnapshotsAsync(refs, ct);
        return snaps
            .Where(s => s.Exists)
            .Select(s => s.ConvertTo<TeamDoc>())
            .ToDictionary(t => t.TeamId);
    }

    public async Task<List<int>> GetAllTeamIdsAsync(CancellationToken ct = default)
    {
        var snap = await db.Collection(Col).Select("TeamId").GetSnapshotAsync(ct);
        return snap.Select(d => d.ConvertTo<TeamDoc>().TeamId).ToList();
    }

    public async Task UpsertAsync(TeamDoc team, CancellationToken ct = default)
    {
        await db.Collection(Col).Document(team.TeamId.ToString()).SetAsync(team, cancellationToken: ct);
    }
}
