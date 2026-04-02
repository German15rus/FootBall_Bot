using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data;
using PremierLeagueBot.Data.Entities;
using PremierLeagueBot.Services.Football;

namespace PremierLeagueBot.Services.Background;

/// <summary>
/// Background service that periodically syncs data from the external Football API
/// into the local SQLite/PostgreSQL database.
///
/// Schedule:
///   - Standings + Matches: every 10 minutes
///   - Squads:              once per day (offset to avoid API rate limits)
/// </summary>
public sealed class DataUpdateService(
    IServiceScopeFactory scopeFactory,
    ILogger<DataUpdateService> logger) : BackgroundService
{
    private static readonly TimeSpan MatchInterval  = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SquadInterval  = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DataUpdateService started");

        // Stagger tasks so they don't hit the API simultaneously on startup
        var matchTask = RunOnIntervalAsync("Matches+Standings", MatchInterval,
            SyncMatchesAndStandingsAsync, TimeSpan.Zero, stoppingToken);

        var squadTask = RunOnIntervalAsync("Squads", SquadInterval,
            SyncSquadsAsync, TimeSpan.FromMinutes(2), stoppingToken);

        await Task.WhenAll(matchTask, squadTask);
    }

    private static async Task RunOnIntervalAsync(
        string name,
        TimeSpan interval,
        Func<CancellationToken, Task> action,
        TimeSpan initialDelay,
        CancellationToken ct)
    {
        await Task.Delay(initialDelay, ct);
        while (!ct.IsCancellationRequested)
        {
            try   { await action(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log and continue – don't crash the service on transient API errors
                Console.Error.WriteLine($"[{name}] Error: {ex.Message}");
            }
            await Task.Delay(interval, ct);
        }
    }

    // ── Sync matches + standings ─────────────────────────────────────────────

    private async Task SyncMatchesAndStandingsAsync(CancellationToken ct)
    {
        using var scope    = scopeFactory.CreateScope();
        var football       = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var db             = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Upsert standings → teams table
        var standings = await football.GetStandingsAsync(ct);
        foreach (var s in standings)
        {
            var existing = await db.Teams.FindAsync([s.TeamId], ct);
            if (existing is null)
            {
                db.Teams.Add(new Team
                {
                    TeamId    = s.TeamId,
                    Name      = s.TeamName,
                    ShortName = s.ShortName,
                    EmblemUrl = s.EmblemUrl
                });
            }
            else
            {
                existing.Name      = s.TeamName;
                existing.ShortName = s.ShortName;
                existing.EmblemUrl = s.EmblemUrl;
            }
        }

        // 2. Upsert upcoming + recent matches (±30 days window)
        var from    = DateTime.UtcNow.AddDays(-30);
        var to      = DateTime.UtcNow.AddDays(30);
        var matches = await football.GetMatchesAsync(from, to, ct);

        foreach (var m in matches)
        {
            var existing = await db.Matches.FindAsync([m.MatchId], ct);
            if (existing is null)
            {
                db.Matches.Add(new Match
                {
                    MatchId    = m.MatchId,
                    HomeTeamId = m.HomeTeamId,
                    AwayTeamId = m.AwayTeamId,
                    MatchDate  = m.MatchDate,
                    Stadium    = m.Stadium,
                    HomeScore  = m.HomeScore,
                    AwayScore  = m.AwayScore,
                    Status     = m.Status
                });
            }
            else
            {
                existing.HomeScore = m.HomeScore;
                existing.AwayScore = m.AwayScore;
                existing.Status    = m.Status;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Synced {Teams} teams, {Matches} matches",
            standings.Count, matches.Count);
    }

    // ── Sync squads ──────────────────────────────────────────────────────────

    private async Task SyncSquadsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var football    = scope.ServiceProvider.GetRequiredService<IFootballApiClient>();
        var db          = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var teamIds = await db.Teams.Select(t => t.TeamId).ToListAsync(ct);
        logger.LogInformation("Syncing squads for {Count} teams…", teamIds.Count);

        foreach (var teamId in teamIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var players = await football.GetTeamSquadAsync(teamId, ct);

                // Remove old squad entries for this team
                var old = db.Players.Where(p => p.TeamId == teamId);
                db.Players.RemoveRange(old);

                foreach (var p in players)
                {
                    db.Players.Add(new Player
                    {
                        PlayerId = p.PlayerId,
                        TeamId   = p.TeamId,
                        Name     = p.Name,
                        Number   = p.Number,
                        Position = p.Position
                    });
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync squad for team {TeamId}", teamId);
            }

            // Stay well within API-Football free tier (100 req/day)
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        logger.LogInformation("Squad sync complete");
    }
}
