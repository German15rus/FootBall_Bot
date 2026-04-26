using PremierLeagueBot.Data.FirestoreModels;
using PremierLeagueBot.Data.Repositories;
using PremierLeagueBot.Services.Football;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Periodically syncs data from the external Football API into Firestore.
/// Schedule: standings every 3 h, matches every 10 min, squads every 72 h.
/// </summary>
public sealed class DataUpdateService(
    IServiceScopeFactory scopeFactory,
    ILogger<DataUpdateService> logger) : BackgroundService
{
    private static readonly TimeSpan StandingsInterval = TimeSpan.FromHours(3);
    private static readonly TimeSpan MatchInterval     = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SquadInterval     = TimeSpan.FromHours(72);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DataUpdateService started");

        await Task.WhenAll(
            RunOnIntervalAsync("Standings", StandingsInterval, SyncStandingsAsync, TimeSpan.Zero,           stoppingToken),
            RunOnIntervalAsync("Matches",   MatchInterval,     SyncMatchesAsync,   TimeSpan.FromSeconds(5), stoppingToken),
            RunOnIntervalAsync("ClMatches", MatchInterval,     SyncClMatchesAsync, TimeSpan.FromSeconds(15),stoppingToken),
            RunOnIntervalAsync("Squads",    SquadInterval,     SyncSquadsAsync,    TimeSpan.FromMinutes(2), stoppingToken)
        );
    }

    private static async Task RunOnIntervalAsync(
        string name, TimeSpan interval, Func<CancellationToken, Task> action,
        TimeSpan initialDelay, CancellationToken ct)
    {
        await Task.Delay(initialDelay, ct);
        while (!ct.IsCancellationRequested)
        {
            try   { await action(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { Console.Error.WriteLine($"[{name}] Error: {ex.Message}"); }
            await Task.Delay(interval, ct);
        }
    }

    private async Task SyncStandingsAsync(CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var football     = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var teamRepo     = scope.ServiceProvider.GetRequiredService<TeamRepository>();

        var standings = await football.GetStandingsAsync(ct);
        if (standings.Count == 0) { logger.LogWarning("SyncStandings: empty response"); return; }

        foreach (var s in standings)
        {
            if (s.TeamId <= 0) continue;
            await teamRepo.UpsertAsync(new TeamDoc
            {
                TeamId    = s.TeamId,
                Name      = s.TeamName,
                ShortName = s.ShortName,
                EmblemUrl = s.EmblemUrl,
                Position  = s.Rank
            }, ct);
        }

        logger.LogInformation("Synced {Count} teams from standings", standings.Count);
    }

    private async Task SyncMatchesAsync(CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var football     = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var matchRepo    = scope.ServiceProvider.GetRequiredService<MatchRepository>();
        var teamRepo     = scope.ServiceProvider.GetRequiredService<TeamRepository>();

        var from    = DateTime.UtcNow.AddDays(-30);
        var to      = DateTime.UtcNow.AddDays(30);
        var matches = await football.GetMatchesAsync(from, to, ct);

        var knownTeamIds = (await teamRepo.GetAllTeamIdsAsync(ct)).ToHashSet();

        foreach (var m in matches)
        {
            if (m.MatchId <= 0 || m.HomeTeamId <= 0 || m.AwayTeamId <= 0) continue;

            await EnsureTeamAsync(teamRepo, knownTeamIds, m.HomeTeamId, m.HomeTeamName, ct);
            await EnsureTeamAsync(teamRepo, knownTeamIds, m.AwayTeamId, m.AwayTeamName, ct);

            var existing = await matchRepo.GetByIdAsync(m.MatchId, ct);
            var doc = existing ?? new MatchDoc { MatchId = m.MatchId };
            doc.HomeTeamId = m.HomeTeamId;
            doc.AwayTeamId = m.AwayTeamId;
            doc.MatchDate  = m.MatchDate;
            doc.Stadium    = m.Stadium;
            doc.HomeScore  = m.HomeScore;
            doc.AwayScore  = m.AwayScore;
            doc.Status     = m.Status;
            await matchRepo.UpsertAsync(doc, ct);
        }

        logger.LogInformation("Synced {Count} EPL matches", matches.Count);
    }

    private async Task SyncClMatchesAsync(CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var football     = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var matchRepo    = scope.ServiceProvider.GetRequiredService<MatchRepository>();
        var teamRepo     = scope.ServiceProvider.GetRequiredService<TeamRepository>();

        var from    = DateTime.UtcNow.AddDays(-30);
        var to      = DateTime.UtcNow.AddDays(60);
        var matches = await football.GetClMatchesAsync(from, to, ct);

        var knownTeamIds = (await teamRepo.GetAllTeamIdsAsync(ct)).ToHashSet();

        foreach (var m in matches)
        {
            if (m.MatchId <= 0 || m.HomeTeamId <= 0 || m.AwayTeamId <= 0) continue;

            await EnsureTeamAsync(teamRepo, knownTeamIds, m.HomeTeamId, m.HomeTeamName, ct);
            await EnsureTeamAsync(teamRepo, knownTeamIds, m.AwayTeamId, m.AwayTeamName, ct);

            var existing = await matchRepo.GetByIdAsync(m.MatchId, ct);
            var doc = existing ?? new MatchDoc { MatchId = m.MatchId, CompetitionId = 2 };
            doc.HomeTeamId    = m.HomeTeamId;
            doc.AwayTeamId    = m.AwayTeamId;
            doc.MatchDate     = m.MatchDate;
            doc.Stadium       = m.Stadium;
            doc.HomeScore     = m.HomeScore;
            doc.AwayScore     = m.AwayScore;
            doc.Status        = m.Status;
            doc.CompetitionId = 2;
            await matchRepo.UpsertAsync(doc, ct);
        }

        logger.LogInformation("Synced {Count} CL matches", matches.Count);
    }

    private async Task SyncSquadsAsync(CancellationToken ct)
    {
        using var scope  = scopeFactory.CreateScope();
        var football     = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var teamRepo     = scope.ServiceProvider.GetRequiredService<TeamRepository>();
        var playerRepo   = scope.ServiceProvider.GetRequiredService<PlayerRepository>();

        var teamIds = await teamRepo.GetAllTeamIdsAsync(ct);
        logger.LogInformation("Syncing squads for {Count} teams…", teamIds.Count);

        foreach (var teamId in teamIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var players = await football.GetTeamSquadAsync(teamId, ct);
                await playerRepo.DeleteByTeamAsync(teamId, ct);
                await playerRepo.BatchUpsertAsync(players.Select(p => new PlayerDoc
                {
                    PlayerId = p.PlayerId,
                    TeamId   = p.TeamId,
                    Name     = p.Name,
                    Number   = p.Number,
                    Position = p.Position
                }), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync squad for team {TeamId}", teamId);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        logger.LogInformation("Squad sync complete");
    }

    private static async Task EnsureTeamAsync(
        TeamRepository teamRepo, HashSet<int> knownIds, int teamId, string teamName, CancellationToken ct)
    {
        if (knownIds.Contains(teamId)) return;

        var safeName = string.IsNullOrWhiteSpace(teamName) ? $"Team {teamId}" : teamName.Trim();
        await teamRepo.UpsertAsync(new TeamDoc
        {
            TeamId    = teamId,
            Name      = safeName,
            ShortName = safeName.Length <= 3 ? safeName.ToUpperInvariant() : safeName[..3].ToUpperInvariant()
        }, ct);
        knownIds.Add(teamId);
    }
}
