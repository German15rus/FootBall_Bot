using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Services.Football;

/// <summary>
/// Client for the official Premier League Pulselive API.
/// Base URL: https://footballapi.pulselive.com/football/
/// Requires header: Origin: https://www.premierleague.com
///
/// Used for: squad data (with correct jersey numbers and positions),
///           recent and upcoming fixtures per team.
/// </summary>
public sealed class PremierLeagueApiClient(
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    ILogger<PremierLeagueApiClient> logger)
{
    private HttpClient Http => httpFactory.CreateClient("plapi");

    // ── Season ID ─────────────────────────────────────────────────────────────

    /// <summary>Resolves the current season ID from PL API (cached 24 h).</summary>
    public async Task<int> GetSeasonIdAsync(CancellationToken ct = default)
    {
        const string key = "pl:seasonId";
        if (cache.TryGetValue(key, out int sid)) return sid;

        try
        {
            var root = await GetJsonAsync("compseasons?pageSize=100&comps=1", ct);
            if (root is null) return 0;

            foreach (var s in root.Value.GetProperty("content").EnumerateArray())
            {
                var label = GetStr(s, "label");          // e.g. "2025/26"
                if (label.Contains("2025") || label.Contains("2026"))
                {
                    sid = s.GetProperty("id").GetInt32();
                    cache.Set(key, sid, TimeSpan.FromHours(24));
                    logger.LogInformation("PL season ID resolved: {SeasonId} ({Label})", sid, label);
                    return sid;
                }
            }

            // Fallback: take the first (most recent) season
            sid = root.Value.GetProperty("content")[0].GetProperty("id").GetInt32();
            cache.Set(key, sid, TimeSpan.FromHours(24));
            return sid;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve PL season ID");
            return 0;
        }
    }

    // ── Team ID map (PL ID ↔ name) ────────────────────────────────────────────

    /// <summary>Returns the PL-internal team ID for a given team name (fuzzy match).</summary>
    public async Task<int?> GetPlTeamIdAsync(string teamName, CancellationToken ct = default)
    {
        var map = await GetTeamMapAsync(ct);
        if (map.TryGetValue(teamName.Trim(), out var id)) return id;

        // Fuzzy: PL name contains our name or vice versa
        var key = map.Keys.FirstOrDefault(k =>
            k.Contains(teamName, StringComparison.OrdinalIgnoreCase) ||
            teamName.Contains(k,  StringComparison.OrdinalIgnoreCase));

        return key is not null ? map[key] : null;
    }

    private async Task<Dictionary<string, int>> GetTeamMapAsync(CancellationToken ct)
    {
        const string key = "pl:teamMap";
        if (cache.TryGetValue(key, out Dictionary<string, int>? map) && map is not null)
            return map;

        map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seasonId = await GetSeasonIdAsync(ct);
        if (seasonId == 0) return map;

        try
        {
            var root = await GetJsonAsync(
                $"teams?altIds=true&comps=1&compSeasons={seasonId}&page=0&pageSize=100", ct);
            if (root is null) return map;

            foreach (var item in root.Value.GetProperty("content").EnumerateArray())
            {
                var club = item.GetProperty("club");
                var name = GetStr(club, "name");
                var tid  = club.GetProperty("id").GetInt32();
                if (!string.IsNullOrEmpty(name)) map[name] = tid;

                // Also add short name as alias
                var shortName = GetStr(club, "shortName");
                if (!string.IsNullOrEmpty(shortName) && !map.ContainsKey(shortName))
                    map[shortName] = tid;
            }

            cache.Set(key, map, TimeSpan.FromHours(6));
            logger.LogInformation("PL team map built: {Count} teams", map.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build PL team map");
        }

        return map;
    }

    // ── Squad ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the First Team squad from the official PL API.
    /// Includes correct jersey numbers and detailed positions.
    /// </summary>
    public async Task<IReadOnlyList<PlayerDto>> GetSquadAsync(
        string teamName, int sportsDbTeamId, CancellationToken ct = default)
    {
        const string cachePrefix = "pl:squad:";
        var cacheKey = $"{cachePrefix}{sportsDbTeamId}";

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<PlayerDto>? cached) && cached is not null)
            return cached;

        var seasonId = await GetSeasonIdAsync(ct);
        var plTeamId = await GetPlTeamIdAsync(teamName, ct);

        if (seasonId == 0 || plTeamId is null)
        {
            logger.LogWarning("Cannot fetch PL squad: seasonId={SeasonId}, plTeamId={PlTeamId} for {Name}",
                seasonId, plTeamId, teamName);
            return [];
        }

        try
        {
            var url = $"teams/{plTeamId}/compseasons/{seasonId}/staff" +
                      $"?pageSize=100&compSeasons={seasonId}&altIds=true" +
                      $"&page=0&type=player&id=-1&withSquadNumbers=true";

            var root = await GetJsonAsync(url, ct);
            if (root is null) return [];

            var result = new List<PlayerDto>();
            foreach (var p in root.Value.GetProperty("players").EnumerateArray())
            {
                var info       = p.GetProperty("info");
                var posCode    = GetStr(info, "position");          // G, D, M, F
                var posDetail  = GetStr(info, "positionInfo");      // "Goalkeeper", "Centre-Back"…
                var shirtNum   = info.TryGetProperty("shirtNum", out var n) ? n.GetInt32() : 0;
                var displayName = p.GetProperty("name").GetProperty("display").GetString() ?? "";
                var playerId   = p.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0;

                result.Add(new PlayerDto(
                    PlayerId: playerId,
                    TeamId:   sportsDbTeamId,
                    Name:     displayName,
                    Number:   shirtNum,
                    Position: MapPositionCode(posCode, posDetail)
                ));
            }

            var ordered = result
                .OrderBy(p => PositionOrder(p.Position))
                .ThenBy(p => p.Number == 0 ? 99 : p.Number)
                .ToList();

            cache.Set(cacheKey, (IReadOnlyList<PlayerDto>)ordered, TimeSpan.FromHours(24));
            logger.LogInformation("PL squad fetched: {Count} players for {Team}", ordered.Count, teamName);
            return ordered;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PL squad for {TeamName}", teamName);
            return [];
        }
    }

    // ── Standings ─────────────────────────────────────────────────────────────

    /// <summary>Returns the full First Team standings (all 20 clubs) from the PL API.</summary>
    public async Task<IReadOnlyList<StandingDto>> GetStandingsAsync(CancellationToken ct = default)
    {
        const string cacheKey = "pl:standings";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<StandingDto>? cached) && cached is not null)
            return cached;

        var seasonId = await GetSeasonIdAsync(ct);
        if (seasonId == 0) return [];

        try
        {
            var root = await GetJsonAsync(
                $"standings?compSeasons={seasonId}&altIds=true&detail=2&LCID=1&live=true", ct);
            if (root is null) return [];

            // Response: { "tables": [ { "entries": [ { "position", "team", "overall" } ] } ] }
            if (!root.Value.TryGetProperty("tables", out var tables)
                || tables.GetArrayLength() == 0)
            {
                logger.LogWarning("PL API standings: no tables in response");
                return [];
            }

            // First table is the First Team league table
            if (!tables[0].TryGetProperty("entries", out var entries))
                return [];

            var result = new List<StandingDto>();
            foreach (var e in entries.EnumerateArray())
            {
                var pos  = e.TryGetProperty("position", out var posEl) ? posEl.GetInt32() : 0;
                var team = e.GetProperty("team");
                var ov   = e.GetProperty("overall");

                var gf = ov.TryGetProperty("goalsFor",      out var gfEl) ? gfEl.GetInt32() : 0;
                var ga = ov.TryGetProperty("goalsAgainst",  out var gaEl) ? gaEl.GetInt32() : 0;

                result.Add(new StandingDto(
                    Rank:           pos,
                    TeamId:         team.GetProperty("id").GetInt32(),
                    TeamName:       GetStr(team, "name"),
                    ShortName:      GetStr(team, "shortName"),
                    EmblemUrl:      null,
                    Played:         ov.TryGetProperty("played", out var p) ? p.GetInt32() : 0,
                    Won:            ov.TryGetProperty("won",    out var w) ? w.GetInt32() : 0,
                    Drawn:          ov.TryGetProperty("drawn",  out var d) ? d.GetInt32() : 0,
                    Lost:           ov.TryGetProperty("lost",   out var l) ? l.GetInt32() : 0,
                    GoalsFor:       gf,
                    GoalsAgainst:   ga,
                    GoalDifference: gf - ga,
                    Points:         ov.TryGetProperty("points", out var pts) ? pts.GetInt32() : 0
                ));
            }

            if (result.Count == 0) return [];

            cache.Set(cacheKey, (IReadOnlyList<StandingDto>)result, TimeSpan.FromHours(3));
            logger.LogInformation("PL API standings fetched: {Count} teams", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PL standings");
            return [];
        }
    }

    // ── League-wide upcoming/recent matches ───────────────────────────────────

    /// <summary>Returns all EPL fixtures within the given date range (UTC).</summary>
    public async Task<IReadOnlyList<MatchDto>> GetLeagueMatchesAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        var cacheKey = $"pl:matches:{from:yyyyMMdd}:{to:yyyyMMdd}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<MatchDto>? cached) && cached is not null)
            return cached;

        var seasonId = await GetSeasonIdAsync(ct);
        if (seasonId == 0) return [];

        try
        {
            // Fetch upcoming (U) and live (L) fixtures
            var upcomingTask = GetJsonAsync(
                $"fixtures?comps=1&compSeasons={seasonId}&page=0&pageSize=100&sort=asc&statuses=U,L", ct);
            // Fetch recently completed (C) fixtures to cover date ranges that include today
            var completedTask = GetJsonAsync(
                $"fixtures?comps=1&compSeasons={seasonId}&page=0&pageSize=50&sort=desc&statuses=C", ct);

            await Task.WhenAll(upcomingTask, completedTask);

            var all = new List<MatchDto>();
            if (upcomingTask.Result is not null) all.AddRange(ParseFixtures(upcomingTask.Result.Value));
            if (completedTask.Result is not null) all.AddRange(ParseFixtures(completedTask.Result.Value));

            var filtered = all
                .DistinctBy(m => m.MatchId)
                .Where(m => m.MatchDate >= from && m.MatchDate <= to)
                .OrderBy(m => m.MatchDate)
                .ToList();

            cache.Set(cacheKey, (IReadOnlyList<MatchDto>)filtered, TimeSpan.FromMinutes(10));
            logger.LogInformation("PL API league matches fetched: {Count} in range", filtered.Count);
            return filtered;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PL league matches");
            return [];
        }
    }

    // ── Recent matches ────────────────────────────────────────────────────────

    /// <summary>Returns the last N completed First Team PL matches for a team.</summary>
    public async Task<IReadOnlyList<MatchDto>> GetRecentMatchesAsync(
        string teamName, int sportsDbTeamId, int count = 5, CancellationToken ct = default)
    {
        var cacheKey = $"pl:recent:{sportsDbTeamId}:{count}";
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<MatchDto>? cached) && cached is not null)
            return cached;

        var seasonId = await GetSeasonIdAsync(ct);
        var plTeamId = await GetPlTeamIdAsync(teamName, ct);

        if (seasonId == 0 || plTeamId is null) return [];

        try
        {
            // C = Completed; sort=desc to get most recent first
            var url = $"fixtures?comps=1&compSeasons={seasonId}&teams={plTeamId}" +
                      $"&page=0&pageSize={count}&sort=desc&statuses=C";

            var root = await GetJsonAsync(url, ct);
            if (root is null) return [];

            var result = ParseFixtures(root.Value);
            cache.Set(cacheKey, result, TimeSpan.FromMinutes(15));
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch PL recent matches for {TeamName}", teamName);
            return [];
        }
    }

    // ── Fixture parser ────────────────────────────────────────────────────────

    private static IReadOnlyList<MatchDto> ParseFixtures(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content)) return [];

        var result = new List<MatchDto>();
        foreach (var f in content.EnumerateArray())
        {
            // Kick-off time
            var kickoffMillis = f.GetProperty("kickoff").GetProperty("millis").GetInt64();
            var matchDate     = DateTimeOffset.FromUnixTimeMilliseconds(kickoffMillis).UtcDateTime;

            // Teams (home / away)
            string homeTeam = "", awayTeam = "";
            int    homeScore = 0, awayScore = 0;
            int    homeId = 0, awayId = 0;
            bool   homeScoreSet = false, awayScoreSet = false;

            foreach (var t in f.GetProperty("teams").EnumerateArray())
            {
                var side    = GetStr(t, "side");
                var tName   = GetStr(t.GetProperty("team"), "name");
                var tId     = t.GetProperty("team").GetProperty("id").GetInt32();
                int? score  = t.TryGetProperty("score", out var sv) && sv.ValueKind != JsonValueKind.Null
                              ? sv.GetInt32() : null;

                if (side == "home")
                {
                    homeTeam  = tName; homeId = tId;
                    homeScore = score ?? 0; homeScoreSet = score.HasValue;
                }
                else
                {
                    awayTeam  = tName; awayId = tId;
                    awayScore = score ?? 0; awayScoreSet = score.HasValue;
                }
            }

            var stadium = f.TryGetProperty("ground", out var g)
                ? GetStr(g, "name") : null;

            var statusRaw = GetStr(f, "status");
            var status = statusRaw switch { "C" => "finished", "L" => "live", _ => "scheduled" };

            result.Add(new MatchDto(
                MatchId:      f.GetProperty("id").GetInt32(),
                HomeTeamId:   homeId,
                HomeTeamName: homeTeam,
                AwayTeamId:   awayId,
                AwayTeamName: awayTeam,
                MatchDate:    matchDate,
                Stadium:      stadium,
                HomeScore:    homeScoreSet ? homeScore : null,
                AwayScore:    awayScoreSet ? awayScore : null,
                Status:       status
            ));
        }

        // Return chronological (oldest first) so formatter can TakeLast(5)
        return result.OrderBy(m => m.MatchDate).ToList();
    }

    // ── HTTP helper ───────────────────────────────────────────────────────────

    private async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("PL API HTTP {Code} for {Url}", (int)resp.StatusCode, url);
            return null;
        }
        return await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static string MapPositionCode(string code, string detail) =>
        code.ToUpper() switch
        {
            "G" => "goalkeeper",
            "D" => "defender",
            "M" => "midfielder",
            "F" => "forward",
            _ => detail.ToLower() switch
            {
                var d when d.Contains("goalkeeper")    => "goalkeeper",
                var d when d.Contains("back") ||
                           d.Contains("defend")        => "defender",
                var d when d.Contains("midfield")      => "midfielder",
                var d when d.Contains("forward") ||
                           d.Contains("winger") ||
                           d.Contains("striker") ||
                           d.Contains("attack")        => "forward",
                _                                      => "midfielder"
            }
        };

    private static int PositionOrder(string pos) => pos switch
    {
        "goalkeeper" => 0, "defender" => 1, "midfielder" => 2, "forward" => 3, _ => 4
    };

    private static string GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";
}
