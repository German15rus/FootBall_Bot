using Google.Cloud.Firestore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PremierLeagueBot.Infrastructure;

internal sealed class FirestoreHealthCheck(FirestoreDb db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await db.Collection("_health").Limit(1).GetSnapshotAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
