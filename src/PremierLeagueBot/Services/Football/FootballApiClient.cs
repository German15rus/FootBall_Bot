using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PremierLeagueBot.Infrastructure;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Services.Football;

/// <summary>
/// Data sources:
///   Standings, matches, squad → TheSportsDB (free, no key)
///   News                     → BBC Sport EPL RSS (free, no key)
/// </summary>
public sealed class FootballApiClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<FootballApiOptions> opts,
    PremierLeagueApiClient plClient,
    ILogger<FootballApiClient> logger) : IFootballApiClient
{
    private readonly FootballApiOptions _opts = opts.Value;

    // ── Cache ─────────────────────────────────────────────────────────────────

    private async Task<T> GetCachedAsync<T>(
        string key, TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        if (cache.TryGetValue(key, out T? hit) && hit is not null) return hit;
        var result = await factory(ct);
        cache.Set(key, result, ttl);
        return result;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<StandingDto>> GetStandingsAsync(CancellationToken ct = default)
        => GetCachedAsync("standings", TimeSpan.FromHours(3), FetchStandingsWithFallbackAsync, ct);

    public Task<IReadOnlyList<MatchDto>> GetMatchesAsync(DateTime from, DateTime to, CancellationToken ct = default)
        => GetCachedAsync($"matches:{from:yyyyMMdd}:{to:yyyyMMdd}", TimeSpan.FromMinutes(10),
            c => FetchMatchesWithFallbackAsync(from, to, c), ct);

    public Task<IReadOnlyList<PlayerDto>> GetTeamSquadAsync(int teamId, CancellationToken ct = default)
        => GetCachedAsync($"squad:{teamId}", TimeSpan.FromHours(24),
            c => FetchSquadWithFallbackAsync(teamId, c), ct);

    public Task<IReadOnlyList<MatchDto>> GetRecentMatchesAsync(int teamId, int count = 5, CancellationToken ct = default)
        => GetCachedAsync($"recent:{teamId}", TimeSpan.FromMinutes(15),
            c => FetchRecentWithFallbackAsync(teamId, count, c), ct);

    public Task<IReadOnlyList<NewsDto>> GetNewsAsync(int? teamId = null, CancellationToken ct = default)
        => GetCachedAsync($"news:{teamId}", TimeSpan.FromHours(1),
            c => FetchNewsAsync(teamId, c), ct);

    // ── Standings ─────────────────────────────────────────────────────────────
    // GET /lookuptable.php?l=4328&s=2024-2025

    private async Task<IReadOnlyList<StandingDto>> FetchStandingsAsync(CancellationToken ct)
    {
        try
        {
            var url  = $"lookuptable.php?l={_opts.LeagueId}&s={Uri.EscapeDataString(_opts.Season)}";
            var root = await GetJsonAsync(url, ct);
            if (root is null) return [];

            if (!root.Value.TryGetProperty("table", out var table)
                || table.ValueKind == JsonValueKind.Null)
            {
                logger.LogWarning("TheSportsDB standings: null table for season {Season}", _opts.Season);
                return [];
            }

            var result = new List<StandingDto>();
            int rank = 1;
            foreach (var item in table.EnumerateArray())
            {
                var teamId   = ParseInt(item, "idTeam");
                var gf       = ParseInt(item, "intGoalsFor");
                var ga       = ParseInt(item, "intGoalsAgainst");
                var teamName = GetStr(item, "strTeam");
                var badge    = GetStrNullable(item, "strTeamBadge");

                result.Add(new StandingDto(
                    Rank:           rank++,
                    TeamId:         teamId,
                    TeamName:       teamName,
                    ShortName:      teamName.Length >= 3 ? teamName[..3].ToUpper() : teamName.ToUpper(),
                    EmblemUrl:      badge,
                    Played:         ParseInt(item, "intPlayed"),
                    Won:            ParseInt(item, "intWin"),
                    Drawn:          ParseInt(item, "intDraw"),
                    Lost:           ParseInt(item, "intLoss"),
                    GoalsFor:       gf,
                    GoalsAgainst:   ga,
                    GoalDifference: gf - ga,
                    Points:         ParseInt(item, "intPoints")
                ));
            }

            logger.LogInformation("Fetched {Count} standings", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch standings");
            return [];
        }
    }

    private async Task<IReadOnlyList<StandingDto>> FetchStandingsWithFallbackAsync(CancellationToken ct)
    {
        var plStandings = await plClient.GetStandingsAsync(ct);
        if (plStandings.Count > 0)
        {
            logger.LogInformation("Using PL API standings: {Count} teams", plStandings.Count);
            return plStandings;
        }

        logger.LogWarning("PL API standings empty, falling back to TheSportsDB");
        return await FetchStandingsAsync(ct);
    }

    // ── Matches in date range ─────────────────────────────────────────────────
    // Merges upcoming + past league events, then filters by date

    private async Task<IReadOnlyList<MatchDto>> FetchMatchesInRangeAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        try
        {
            var nextTask = FetchLeagueEventsAsync("eventsnextleague.php", ct);
            var pastTask = FetchLeagueEventsAsync("eventspastleague.php", ct);
            await Task.WhenAll(nextTask, pastTask);

            return nextTask.Result
                .Concat(pastTask.Result)
                .DistinctBy(m => m.MatchId)
                .Where(m => m.MatchDate >= from && m.MatchDate <= to)
                .OrderBy(m => m.MatchDate)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch matches");
            return [];
        }
    }

    private async Task<IReadOnlyList<MatchDto>> FetchMatchesWithFallbackAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var plMatches = await plClient.GetLeagueMatchesAsync(from, to, ct);
        if (plMatches.Count > 0)
        {
            logger.LogInformation("Using PL API matches: {Count} in range", plMatches.Count);
            return plMatches;
        }

        logger.LogWarning("PL API matches empty, falling back to TheSportsDB");
        return await FetchMatchesInRangeAsync(from, to, ct);
    }

    private async Task<IReadOnlyList<MatchDto>> FetchLeagueEventsAsync(
        string endpoint, CancellationToken ct)
    {
        var root = await GetJsonAsync($"{endpoint}?id={_opts.LeagueId}", ct);
        if (root is null) return [];
        if (!root.Value.TryGetProperty("events", out var events)
            || events.ValueKind == JsonValueKind.Null) return [];
        return ParseEvents(events);
    }

    // ── Recent matches for a specific team ────────────────────────────────────
    // Primary:  GET /eventslast.php?id={teamId}   → returns "results" array
    // Fallback: filter past league events by teamId

    private async Task<IReadOnlyList<MatchDto>> FetchRecentTeamMatchesAsync(
        int teamId, int count, CancellationToken ct)
    {
        try
        {
            // Primary attempt
            var primary = await FetchEventslastAsync(teamId, ct);
            if (primary.Count > 0)
                return primary.TakeLast(count).ToList();

            logger.LogWarning("eventslast.php returned empty for team {TeamId}, using fallback", teamId);

            // Fallback: filter past league events
            var past = await FetchLeagueEventsAsync("eventspastleague.php", ct);
            return past
                .Where(m => m.HomeTeamId == teamId || m.AwayTeamId == teamId)
                .OrderByDescending(m => m.MatchDate)
                .Take(count)
                .Reverse()
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch recent matches for team {TeamId}", teamId);
            return [];
        }
    }

    private async Task<IReadOnlyList<MatchDto>> FetchEventslastAsync(int teamId, CancellationToken ct)
    {
        var root = await GetJsonAsync($"eventslast.php?id={teamId}", ct);
        if (root is null) return [];
        if (!root.Value.TryGetProperty("results", out var results)
            || results.ValueKind == JsonValueKind.Null) return [];
        return ParseEvents(results);
    }

    // ── Squad with PL API → TheSportsDB fallback ─────────────────────────────

    private async Task<IReadOnlyList<PlayerDto>> FetchSquadWithFallbackAsync(
        int teamId, CancellationToken ct)
    {
        // Resolve team name so PL API can look up its internal team ID
        var teamName = await ResolveTeamNameFromStandingsAsync(teamId, ct);

        if (!string.IsNullOrEmpty(teamName))
        {
            var plSquad = await plClient.GetSquadAsync(teamName, teamId, ct);
            if (plSquad.Count > 0) return plSquad;
        }

        logger.LogWarning("PL API squad empty for {TeamId}, falling back to TheSportsDB", teamId);
        return await FetchSquadAsync(teamId, ct);
    }

    private async Task<IReadOnlyList<MatchDto>> FetchRecentWithFallbackAsync(
        int teamId, int count, CancellationToken ct)
    {
        var teamName = await ResolveTeamNameFromStandingsAsync(teamId, ct);

        if (!string.IsNullOrEmpty(teamName))
        {
            var plRecent = await plClient.GetRecentMatchesAsync(teamName, teamId, count, ct);
            if (plRecent.Count > 0) return plRecent;
        }

        logger.LogWarning("PL API recent matches empty for {TeamId}, falling back to TheSportsDB", teamId);
        return await FetchRecentTeamMatchesAsync(teamId, count, ct);
    }

    private async Task<string> ResolveTeamNameFromStandingsAsync(int teamId, CancellationToken ct)
    {
        var standings = await GetStandingsAsync(ct);
        return standings.FirstOrDefault(s => s.TeamId == teamId)?.TeamName ?? string.Empty;
    }

    // ── Squad (TheSportsDB fallback) ──────────────────────────────────────────
    // GET /lookup_all_players.php?id={teamId}

    private async Task<IReadOnlyList<PlayerDto>> FetchSquadAsync(int teamId, CancellationToken ct)
    {
        try
        {
            var root = await GetJsonAsync($"lookup_all_players.php?id={teamId}", ct);
            if (root is null) return [];

            if (!root.Value.TryGetProperty("player", out var players)
                || players.ValueKind == JsonValueKind.Null)
            {
                logger.LogInformation("No squad data for team {TeamId}", teamId);
                return [];
            }

            var result = new List<PlayerDto>();
            foreach (var p in players.EnumerateArray())
            {
                var numStr = GetStrNullable(p, "strNumber");
                var number = int.TryParse(numStr, out var n) ? n : 0;

                result.Add(new PlayerDto(
                    PlayerId: ParseInt(p, "idPlayer"),
                    TeamId:   teamId,
                    Name:     GetStr(p, "strPlayer"),
                    Number:   number,
                    Position: MapPosition(GetStr(p, "strPosition"))
                ));
            }

            return result.OrderBy(p => PositionOrder(p.Position)).ThenBy(p => p.Number).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch squad for team {TeamId}", teamId);
            return [];
        }
    }

    // ── News (BBC Sport EPL RSS) ──────────────────────────────────────────────

    private async Task<IReadOnlyList<NewsDto>> FetchNewsAsync(int? teamId, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync(_opts.NewsRssUrl, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("BBC RSS returned {Code}", resp.StatusCode);
                return [];
            }

            var xml  = await resp.Content.ReadAsStringAsync(ct);
            var doc  = XDocument.Parse(xml);

            return doc.Descendants("item")
                .Select(item => new NewsDto(
                    Title:         item.Element("title")?.Value ?? "",
                    Summary:       StripHtml(item.Element("description")?.Value ?? ""),
                    Url:           item.Element("link")?.Value ?? "",
                    PublishedAt:   ParseRssDate(item.Element("pubDate")?.Value),
                    RelatedTeamId: teamId
                ))
                .Where(n => !string.IsNullOrEmpty(n.Url))
                .Take(10)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch news");
            return [];
        }
    }

    // ── Event parser ──────────────────────────────────────────────────────────

    private static IReadOnlyList<MatchDto> ParseEvents(JsonElement events)
    {
        var result = new List<MatchDto>();
        foreach (var e in events.EnumerateArray())
        {
            var dateStr   = GetStr(e, "dateEvent");
            var timeStr   = GetStr(e, "strTime") is { Length: > 0 } t ? t : "00:00:00";
            var statusRaw = GetStr(e, "strStatus");

            if (!DateTime.TryParse($"{dateStr}T{timeStr}",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var matchDate))
                continue;

            result.Add(new MatchDto(
                MatchId:      ParseInt(e, "idEvent"),
                HomeTeamId:   ParseInt(e, "idHomeTeam"),
                HomeTeamName: GetStr(e, "strHomeTeam"),
                AwayTeamId:   ParseInt(e, "idAwayTeam"),
                AwayTeamName: GetStr(e, "strAwayTeam"),
                MatchDate:    matchDate.ToUniversalTime(),
                Stadium:      GetStrNullable(e, "strVenue"),
                HomeScore:    ParseNullableInt(e, "intHomeScore"),
                AwayScore:    ParseNullableInt(e, "intAwayScore"),
                Status:       MapStatus(statusRaw)
            ));
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("HTTP {Code} for {Url}", (int)resp.StatusCode, url);
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    private static string MapStatus(string raw)
    {
        var up = raw.Trim().ToUpperInvariant();
        if (up is "NS" or "TBD" or "POSTPONED" or "CANC" or "ABD") return "scheduled";
        if (up.Contains("FINISH") || up is "FT" or "AET" or "PEN" or "AP") return "finished";
        if (up.Length > 0) return "live";
        return "scheduled";
    }

    private static string MapPosition(string raw) => raw.ToLower() switch
    {
        var p when p.Contains("goalkeeper") || p.Contains("goalie") => "goalkeeper",
        var p when p.Contains("defend")                             => "defender",
        var p when p.Contains("midfield")                           => "midfielder",
        var p when p.Contains("forward") || p.Contains("attack")
                || p.Contains("winger") || p.Contains("striker")   => "forward",
        _ => "midfielder"
    };

    private static int PositionOrder(string pos) => pos switch
    {
        "goalkeeper" => 0, "defender" => 1, "midfielder" => 2, "forward" => 3, _ => 4
    };

    private static int ParseInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
        return int.TryParse(v.GetString(), out var n) ? n : 0;
    }

    private static int? ParseNullableInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt32();
        return int.TryParse(v.GetString(), out var n) ? n : null;
    }

    private static string GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetStrNullable(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? v.GetString() : null;

    private static DateTime ParseRssDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return DateTime.UtcNow;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var dt) ? dt : DateTime.UtcNow;
    }

    private static string StripHtml(string html)
        => string.IsNullOrEmpty(html) ? html
            : System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
}
