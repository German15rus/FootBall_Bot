using Google.Cloud.Firestore;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Data.Repositories;

public sealed class NotificationLogRepository(FirestoreDb db)
{
    private const string Col = "notificationLogs";

    public async Task AddAsync(NotificationLogDoc log, CancellationToken ct = default)
    {
        await db.Collection(Col).AddAsync(log, ct);
    }
}
