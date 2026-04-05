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
/// Football data from two sources:
///   1. Official Premier League API (footballapi.pulselive.com) — standings, matches.
///      Requires Origin/Referer headers; no auth token needed.
///   2. TheSportsDB (free, no key) — squad/player data only.
///   3. BBC Sport EPL RSS — news.
///
/// Season ID is auto-detected from the PL API compseasons endpoint and cached 24 h.
/// All results are cached in-memory to reduce API traffic.
/// </summary>
public sealed class FootballApiClient : IFootballApiClient
{
    // Named HTTP client keys (registered in Program.cs)
    public const string PlApiClient    = "PlApi";
    public const string SportsDbClient = "SportsDb";

    private readonly IHttpClientFactory           _factory;
    private readonly IMemoryCache                 _cache;
    private readonly FootballApiOptions           _opts;
    private readonly ILogger<FootballApiClient>   _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public FootballApiClient(
        IHttpClientFactory factory,
        IMemoryCache cache,
        IOptions<FootballApiOptions> opts,
        ILogger<FootballApiClient> logger)
    {
        _factory = factory;
        _cache   = cache;
        _opts    = opts.Value;
        _logger  = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<StandingDto>> GetStandingsAsync(CancellationToken ct = default)
        => GetCachedAsync("pl:standings", TimeSpan.FromMinutes(15), FetchStandingsAsync, ct);

    public Task<IReadOnlyList<MatchDto>> GetMatchesAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
        => GetCachedAsync(
            $"pl:matches:{from:yyyyMMdd}:{to:yyyyMMdd}",
            TimeSpan.FromMinutes(10),
            c => FetchMatchesInRangeAsync(from, to, c), ct);

    public Task<IReadOnlyList<PlayerDto>> GetTeamSquadAsync(
        int teamId, CancellationToken ct = default)
        => GetCachedAsync(
            $"sdb:squad:{teamId}",
            TimeSpan.FromHours(24),
            c => FetchSquadByPlTeamIdAsync(teamId, c), ct);

    public Task<IReadOnlyList<MatchDto>> GetRecentMatchesAsync(
        int teamId, int count = 5, CancellationToken ct = default)
        => GetCachedAsync(
            $"pl:recent:{teamId}",
            TimeSpan.FromMinutes(15),
            c => FetchRecentTeamMatchesAsync(teamId, count, c), ct);

    public Task<IReadOnlyList<NewsDto>> GetNewsAsync(int? teamId = null, CancellationToken ct = default)
        => GetCachedAsync($"news:{teamId}", TimeSpan.FromHours(1),
            c => FetchNewsAsync(teamId, c), ct);

    // ── Season ID (auto-detected from PL API) ────────────────────────────────

    private Task<int> GetSeasonIdAsync(CancellationToken ct)
        => GetCachedAsync("pl:seasonId", TimeSpan.FromHours(24), FetchCurrentSeasonIdAsync, ct);

    private async Task<int> FetchCurrentSeasonIdAsync(CancellationToken ct)
    {
        const int fallbackId = 719; // 2024/25 — safe fallback
        try
        {
            var client = _factory.CreateClient(PlApiClient);
            var url    = $"compseasons?page=0&pageSize=5&league={_opts.CompetitionId}";
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PL compseasons fetch failed ({Status}), using fallback season {Id}",
                    resp.StatusCode, fallbackId);
                return fallbackId;
            }

            var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (root.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
            {
                // API returns seasons newest-first
                var id = ParseInt(content[0], "id");
                if (id > 0)
                {
                    _logger.LogInformation("Current PL season ID resolved: {Id}", id);
                    return id;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception resolving PL season ID, using fallback {Id}", fallbackId);
        }

        return fallbackId;
    }

    // ── Standings (official PL API) ──────────────────────────────────────────

    private async Task<IReadOnlyList<StandingDto>> FetchStandingsAsync(CancellationToken ct)
    {
        var seasonId = await GetSeasonIdAsync(ct);
        var client   = _factory.CreateClient(PlApiClient);
        var url      = $"standings?compSeasons={seasonId}&altIds=true&detail=2" +
                       $"&COMP={_opts.CompetitionId}&phase=1&source=PLUS&teams=-1&type=totals";

        using var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("PL standings fetch failed: {Status}", resp.StatusCode);
            return [];
        }

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        // tables[] is ordered by gameweek; last entry = latest standings
        if (!root.TryGetProperty("tables", out var tables) || tables.GetArrayLength() == 0)
        {
            _logger.LogWarning("PL standings response has no tables");
            return [];
        }

        var latestTable = tables[tables.GetArrayLength() - 1];
        if (!latestTable.TryGetProperty("entries", out var entries))
            return [];

        var result = new List<StandingDto>();

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("team", out var teamEl)) continue;
            if (!entry.TryGetProperty("overall", out var overall)) continue;

            var teamId   = ParseInt(teamEl, "id");
            var teamName = teamEl.TryGetProperty("name", out var tn)      ? tn.GetString() ?? "" : "";
            var abbr     = teamEl.TryGetProperty("club", out var club) &&
                           club.TryGetProperty("abbr", out var ab)
                           ? ab.GetString() ?? ""
                           : teamName.Length >= 3 ? teamName[..3].ToUpper() : teamName.ToUpper();

            if (teamId == 0 || string.IsNullOrEmpty(teamName)) continue;

            result.Add(new StandingDto(
                Rank:           ParseInt(entry, "position"),
                TeamId:         teamId,
                TeamName:       teamName,
                ShortName:      abbr,
                EmblemUrl:      null,   // PL API badge URLs require extra auth
                Played:         ParseInt(overall, "played"),
                Won:            ParseInt(overall, "won"),
                Drawn:          ParseInt(overall, "drawn"),
                Lost:           ParseInt(overall, "lost"),
                GoalsFor:       ParseInt(overall, "goalsFor"),
                GoalsAgainst:   ParseInt(overall, "goalsAgainst"),
                GoalDifference: ParseInt(overall, "goalsDifference"),
                Points:         ParseInt(overall, "points")
            ));
        }

        result.Sort((a, b) => a.Rank.CompareTo(b.Rank));
        _logger.LogInformation("Fetched {Count} PL standings", result.Count);
        return result;
    }

    // ── Matches in date range (official PL API) ──────────────────────────────

    private async Task<IReadOnlyList<MatchDto>> FetchMatchesInRangeAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var seasonId = await GetSeasonIdAsync(ct);
        var client   = _factory.CreateClient(PlApiClient);

        // Fetch upcoming (M = scheduled, L = live) and completed in parallel
        var upcomingTask  = FetchPlFixturesAsync(client, seasonId, "M,L", "asc",  80, ct);
        var completedTask = FetchPlFixturesAsync(client, seasonId, "C",   "desc", 80, ct);
        await Task.WhenAll(upcomingTask, completedTask);

        var all = upcomingTask.Result.Concat(completedTask.Result)
            .DistinctBy(m => m.MatchId)
            .Where(m => m.MatchDate >= from && m.MatchDate <= to)
            .OrderBy(m => m.MatchDate)
            .ToList();

        _logger.LogInformation("Fetched {Count} PL matches in range {From:d}–{To:d}", all.Count, from, to);
        return all;
    }

    private async Task<IReadOnlyList<MatchDto>> FetchPlFixturesAsync(
        HttpClient client, int seasonId, string statuses, string sort, int pageSize, CancellationToken ct)
    {
        var url = $"fixtures?comps={_opts.CompetitionId}&compSeasons={seasonId}" +
                  $"&page=0&pageSize={pageSize}&sort={sort}&statuses={statuses}&altIds=true";

        using var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("PL fixtures fetch failed (statuses={S}): {Code}", statuses, resp.StatusCode);
            return [];
        }

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return root.TryGetProperty("content", out var content)
            ? ParsePlFixtures(content)
            : [];
    }

    // ── Recent matches for a team (official PL API) ──────────────────────────

    private async Task<IReadOnlyList<MatchDto>> FetchRecentTeamMatchesAsync(
        int teamId, int count, CancellationToken ct)
    {
        var seasonId = await GetSeasonIdAsync(ct);
        var client   = _factory.CreateClient(PlApiClient);

        // Request completed PL fixtures for this specific team, newest first
        var url = $"fixtures?comps={_opts.CompetitionId}&compSeasons={seasonId}" +
                  $"&teams={teamId}&page=0&pageSize=10&sort=desc&statuses=C&altIds=true";

        using var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("PL recent matches failed for team {TeamId}: {Status}", teamId, resp.StatusCode);
            return [];
        }

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!root.TryGetProperty("content", out var content))
        {
            _logger.LogInformation("No recent PL matches for team {TeamId}", teamId);
            return [];
        }

        return ParsePlFixtures(content)
            .OrderByDescending(m => m.MatchDate)
            .Take(count)
            .ToList();
    }

    // ── Squad (TheSportsDB, looked up via team name) ─────────────────────────

    private async Task<IReadOnlyList<PlayerDto>> FetchSquadByPlTeamIdAsync(int plTeamId, CancellationToken ct)
    {
        // Resolve team name from standings (PL API)
        var standings = await GetStandingsAsync(ct);
        var entry     = standings.FirstOrDefault(s => s.TeamId == plTeamId);
        if (entry is null)
        {
            _logger.LogInformation("Team {PlTeamId} not found in standings for squad lookup", plTeamId);
            return [];
        }

        // Find the TheSportsDB numeric team ID by searching the team name
        var sportsDbId = await LookupSportsDbTeamIdAsync(entry.TeamName, ct);
        if (sportsDbId == 0)
        {
            _logger.LogInformation("No TheSportsDB ID for '{TeamName}'", entry.TeamName);
            return [];
        }

        return await FetchSquadFromSportsDbAsync(sportsDbId, plTeamId, ct);
    }

    private async Task<int> LookupSportsDbTeamIdAsync(string teamName, CancellationToken ct)
        => await GetCachedAsync(
            $"sdb:teamid:{teamName.ToLower()}",
            TimeSpan.FromHours(48),
            async innerCt =>
            {
                var client = _factory.CreateClient(SportsDbClient);
                var url    = $"searchteams.php?t={Uri.EscapeDataString(teamName)}";
                using var resp = await client.GetAsync(url, innerCt);
                if (!resp.IsSuccessStatusCode) return 0;

                var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: innerCt);
                if (!root.TryGetProperty("teams", out var teams) ||
                    teams.ValueKind == JsonValueKind.Null ||
                    teams.GetArrayLength() == 0)
                    return 0;

                // Prefer the Premier League / English team result
                foreach (var t in teams.EnumerateArray())
                {
                    var league = t.TryGetProperty("strLeague", out var l) ? l.GetString() ?? "" : "";
                    if (league.Contains("Premier", StringComparison.OrdinalIgnoreCase) ||
                        league.Contains("English", StringComparison.OrdinalIgnoreCase))
                        return ParseInt(t, "idTeam");
                }

                // Fallback: first result
                return ParseInt(teams[0], "idTeam");
            }, ct);

    private async Task<IReadOnlyList<PlayerDto>> FetchSquadFromSportsDbAsync(
        int sportsDbTeamId, int plTeamId, CancellationToken ct)
    {
        var client = _factory.CreateClient(SportsDbClient);
        var url    = $"lookup_all_players.php?id={sportsDbTeamId}";
        using var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!root.TryGetProperty("player", out var players) ||
            players.ValueKind == JsonValueKind.Null)
        {
            _logger.LogInformation(
                "No squad data for SportsDB team {SportsDbId} (may require Patreon tier)", sportsDbTeamId);
            return [];
        }

        var result = new List<PlayerDto>();
        foreach (var p in players.EnumerateArray())
        {
            var pos    = p.TryGetProperty("strPosition", out var posEl) ? posEl.GetString() ?? "" : "";
            var numStr = p.TryGetProperty("strNumber",   out var numEl) ? numEl.GetString() : null;
            _ = int.TryParse(numStr, out var number);

            result.Add(new PlayerDto(
                PlayerId: ParseInt(p, "idPlayer"),
                TeamId:   plTeamId,
                Name:     p.TryGetProperty("strPlayer", out var nm) ? nm.GetString() ?? "" : "",
                Number:   number,
                Position: MapPosition(pos)
            ));
        }

        return result;
    }

    // ── News (BBC Sport EPL RSS) ─────────────────────────────────────────────

    private async Task<IReadOnlyList<NewsDto>> FetchNewsAsync(int? teamId, CancellationToken ct)
    {
        var client = _factory.CreateClient(SportsDbClient); // any client works for external URL
        using var resp = await client.GetAsync(_opts.NewsRssUrl, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("BBC RSS fetch failed: {Code}", resp.StatusCode);
            return [];
        }

        var xml = await resp.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);

        var items = doc.Descendants("item")
            .Select(item => new NewsDto(
                Title:         item.Element("title")?.Value ?? "",
                Summary:       StripHtml(item.Element("description")?.Value ?? ""),
                Url:           item.Element("link")?.Value ?? "",
                PublishedAt:   ParseRssDate(item.Element("pubDate")?.Value),
                RelatedTeamId: teamId
            ))
            .Where(n => !string.IsNullOrEmpty(n.Url));

        return items.Take(10).ToList();
    }

    // ── PL fixture parser ────────────────────────────────────────────────────

    private static IReadOnlyList<MatchDto> ParsePlFixtures(JsonElement content)
    {
        var result = new List<MatchDto>();

        foreach (var f in content.EnumerateArray())
        {
            // Parse kickoff timestamp (milliseconds since epoch)
            if (!f.TryGetProperty("kickoff", out var kickoff)) continue;
            var millis = kickoff.TryGetProperty("millis", out var ms)
                ? ParseLong(ms)
                : 0L;
            if (millis == 0) continue;

            var matchDate = DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;

            // Parse teams array — teams[0] = home, teams[1] = away
            if (!f.TryGetProperty("teams", out var teams) || teams.GetArrayLength() < 2) continue;

            var homeEl = teams[0];
            var awayEl = teams[1];

            if (!homeEl.TryGetProperty("team", out var homeTeam) ||
                !awayEl.TryGetProperty("team", out var awayTeam)) continue;

            var homeId   = ParseInt(homeTeam, "id");
            var awayId   = ParseInt(awayTeam, "id");
            var homeName = homeTeam.TryGetProperty("name", out var hn) ? hn.GetString() ?? "" : "";
            var awayName = awayTeam.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";

            if (homeId == 0 || awayId == 0) continue;

            // Parse scores (null if match not yet played)
            var homeScore = ParseNullableInt(homeEl, "score");
            var awayScore = ParseNullableInt(awayEl, "score");

            // Parse status ("C" = completed, "L"/"H"/"ET" = live, "M"/"U" = upcoming)
            var statusRaw = f.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            var status    = MapPlStatus(statusRaw);

            // Parse ground name
            string? stadium = null;
            if (f.TryGetProperty("ground", out var ground))
                stadium = ground.TryGetProperty("name", out var gn) ? gn.GetString() : null;

            var matchId = ParseInt(f, "id");
            if (matchId == 0) continue;

            result.Add(new MatchDto(
                MatchId:      matchId,
                HomeTeamId:   homeId,
                HomeTeamName: homeName,
                AwayTeamId:   awayId,
                AwayTeamName: awayName,
                MatchDate:    matchDate,
                Stadium:      stadium,
                HomeScore:    homeScore,
                AwayScore:    awayScore,
                Status:       status
            ));
        }

        return result;
    }

    // ── Cache helper ─────────────────────────────────────────────────────────

    private async Task<T> GetCachedAsync<T>(
        string key, TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;
        var result = await factory(ct);
        _cache.Set(key, result, ttl);
        return result;
    }

    // ── Status / position mappers ────────────────────────────────────────────

    /// <summary>Maps PL API status codes to internal status strings.</summary>
    private static string MapPlStatus(string raw) => raw.ToUpperInvariant() switch
    {
        "C"                   => "finished",    // Completed
        "L" or "H" or "ET"   => "live",         // Live / Half-time / Extra-time
        _                     => "scheduled"    // M = upcoming, U = TBD, etc.
    };

    private static string MapPosition(string raw) => raw.ToLower() switch
    {
        var p when p.Contains("goalkeeper") || p.Contains("goalie") => "goalkeeper",
        var p when p.Contains("defend")                             => "defender",
        var p when p.Contains("midfield")                           => "midfielder",
        var p when p.Contains("forward") || p.Contains("attack")
                || p.Contains("winger") || p.Contains("striker")   => "forward",
        _ => "midfielder"
    };

    // ── JSON parse helpers ───────────────────────────────────────────────────

    private static int ParseInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return 0;
        return ParseInt(val);
    }

    private static int ParseInt(JsonElement val)
    {
        if (val.ValueKind == JsonValueKind.Number)
        {
            if (val.TryGetInt32(out var n)) return n;
            return (int)Math.Round(val.GetDouble()); // handles "12.0" from PL API
        }
        if (val.ValueKind == JsonValueKind.String)
            return int.TryParse(val.GetString(), out var n2) ? n2 : 0;
        return 0;
    }

    private static int? ParseNullableInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return null;
        if (val.ValueKind == JsonValueKind.Null) return null;
        if (val.ValueKind == JsonValueKind.Number)
        {
            if (val.TryGetInt32(out var n)) return n;
            return (int)Math.Round(val.GetDouble());
        }
        if (val.ValueKind == JsonValueKind.String)
            return int.TryParse(val.GetString(), out var n2) ? n2 : null;
        return null;
    }

    private static long ParseLong(JsonElement val)
    {
        if (val.ValueKind == JsonValueKind.Number)
        {
            if (val.TryGetInt64(out var l)) return l;
            return (long)val.GetDouble();
        }
        if (val.ValueKind == JsonValueKind.String)
            return long.TryParse(val.GetString(), out var l2) ? l2 : 0L;
        return 0L;
    }

    private static DateTime ParseRssDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return DateTime.UtcNow;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return DateTime.UtcNow;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
    }
}
