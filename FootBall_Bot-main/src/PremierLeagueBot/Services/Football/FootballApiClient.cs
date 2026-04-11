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
            $"pl:squad:{teamId}",
            TimeSpan.FromHours(72),
            c => FetchSquadFromPlApiAsync(teamId, c), ct);

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
        // If manually configured — use it immediately
        if (_opts.SeasonId > 0)
        {
            _logger.LogInformation("Using configured PL season ID: {Id}", _opts.SeasonId);
            return _opts.SeasonId;
        }

        const int fallbackId = 719; // 2024/25 — last known fallback
        try
        {
            var client = _factory.CreateClient(PlApiClient);
            // Correct endpoint: /competitions/{id}/compseasons  (old /compseasons?league= returns 404)
            var url = $"competitions/{_opts.CompetitionId}/compseasons?page=0&pageSize=10";
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PL compseasons fetch failed ({Status}), using fallback season {Id}",
                    resp.StatusCode, fallbackId);
                return fallbackId;
            }

            var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!root.TryGetProperty("content", out var content) || content.GetArrayLength() == 0)
                return fallbackId;

            // Log all available seasons to help diagnose wrong-season issues
            foreach (var s in content.EnumerateArray())
            {
                var sid   = ParseInt(s, "id");
                var label = s.TryGetProperty("label", out var l) ? l.GetString() : "?";
                _logger.LogInformation("Available PL season: id={Id} label={Label}", sid, label);
            }

            // First: look for the season that contains "2025" in its label (= 2025/26)
            foreach (var s in content.EnumerateArray())
            {
                var label = s.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                if (label.Contains("2025", StringComparison.Ordinal))
                {
                    var id = ParseInt(s, "id");
                    if (id > 0)
                    {
                        _logger.LogInformation("Resolved 2025/26 PL season ID: {Id} (label={Label})", id, label);
                        return id;
                    }
                }
            }

            // Second: fall back to the newest season in the list (index 0 = newest-first)
            var newestId = ParseInt(content[0], "id");
            if (newestId > 0)
            {
                var newestLabel = content[0].TryGetProperty("label", out var nl) ? nl.GetString() : "?";
                _logger.LogWarning(
                    "2025/26 season not found in compseasons. Using newest season: id={Id} label={Label}. " +
                    "If this is wrong, set FootballApi:SeasonId in appsettings.json.",
                    newestId, newestLabel);
                return newestId;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Exception resolving PL season ID, using fallback {Id}", fallbackId);
        }

        return fallbackId;
    }

    // ── Standings (official PL API) ──────────────────────────────────────────

    private static readonly string StandingsCacheFile =
        Path.Combine(AppContext.BaseDirectory, "standings_cache.json");

    private async Task<IReadOnlyList<StandingDto>> FetchStandingsAsync(CancellationToken ct)
    {
        try
        {
            var seasonId = await GetSeasonIdAsync(ct);
            var client   = _factory.CreateClient(PlApiClient);
            var url      = $"standings?compSeasons={seasonId}&altIds=true&detail=2" +
                           $"&COMP={_opts.CompetitionId}&phase=1&source=PLUS&teams=-1&type=totals";

            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PL standings fetch failed: {Status}", resp.StatusCode);
                return await LoadStandingsFallbackAsync(ct);
            }

            var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            if (!root.TryGetProperty("tables", out var tables) || tables.GetArrayLength() == 0)
            {
                _logger.LogWarning("PL standings response has no tables");
                return await LoadStandingsFallbackAsync(ct);
            }

            var latestTable = tables[tables.GetArrayLength() - 1];
            if (!latestTable.TryGetProperty("entries", out var entries))
                return await LoadStandingsFallbackAsync(ct);

            var result = new List<StandingDto>();

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("team", out var teamEl)) continue;
                if (!entry.TryGetProperty("overall", out var overall)) continue;

                var teamId   = ParseInt(teamEl, "id");
                var teamName = teamEl.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";
                var abbr     = teamEl.TryGetProperty("club", out var club) &&
                               club.TryGetProperty("abbr", out var ab)
                               ? ab.GetString() ?? ""
                               : Abbr(teamName);

                if (teamId == 0 || string.IsNullOrEmpty(teamName)) continue;

                result.Add(new StandingDto(
                    Rank:           ParseInt(entry, "position"),
                    TeamId:         teamId,
                    TeamName:       teamName,
                    ShortName:      abbr,
                    EmblemUrl:      null,
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

            // Save to disk so future restarts can serve cached data when API is down
            await SaveStandingsCacheAsync(result);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("PL standings fetch exception: {Msg} — trying disk cache", ex.Message);
            return await LoadStandingsFallbackAsync(ct);
        }
    }

    /// <summary>
    /// Fetches standings from TheSportsDB (free, no key required) as fallback.
    /// URL: lookuptable.php?l={leagueId}&amp;s={season}
    /// </summary>
    private async Task<IReadOnlyList<StandingDto>> FetchStandingsFromSportsDbAsync(CancellationToken ct)
    {
        try
        {
            var client = _factory.CreateClient(SportsDbClient);
            var url    = $"lookuptable.php?l={_opts.LeagueId}&s={_opts.Season}";

            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!root.TryGetProperty("table", out var table) || table.ValueKind == JsonValueKind.Null)
                return [];

            var result = new List<StandingDto>();
            foreach (var row in table.EnumerateArray())
            {
                var teamName  = row.TryGetProperty("strTeam",      out var tn) ? tn.GetString() ?? "" : "";
                var shortName = row.TryGetProperty("strTeamShort", out var ts) ? ts.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(teamName)) continue;
                if (string.IsNullOrEmpty(shortName))
                    shortName = Abbr(teamName);

                result.Add(new StandingDto(
                    Rank:           ParseInt(row, "intRank"),
                    TeamId:         0,
                    TeamName:       teamName,
                    ShortName:      shortName,
                    EmblemUrl:      null,
                    Played:         ParseInt(row, "intPlayed"),
                    Won:            ParseInt(row, "intWin"),
                    Drawn:          ParseInt(row, "intDraw"),
                    Lost:           ParseInt(row, "intLoss"),
                    GoalsFor:       ParseInt(row, "intGoalsFor"),
                    GoalsAgainst:   ParseInt(row, "intGoalsAgainst"),
                    GoalDifference: ParseInt(row, "intGoalDifference"),
                    Points:         ParseInt(row, "intPoints")
                ));
            }

            result.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            _logger.LogInformation("Fetched {Count} standings from TheSportsDB", result.Count);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("TheSportsDB standings fetch failed: {Msg}", ex.Message);
            return [];
        }
    }

    private async Task SaveStandingsCacheAsync(IReadOnlyList<StandingDto> standings)
    {
        try
        {
            var json = JsonSerializer.Serialize(standings);
            await File.WriteAllTextAsync(StandingsCacheFile, json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not save standings cache: {Msg}", ex.Message);
        }
    }

    private async Task<IReadOnlyList<StandingDto>> LoadStandingsFallbackAsync(CancellationToken ct = default)
    {
        // 1) Try TheSportsDB (free alternative API)
        var sportsDb = await FetchStandingsFromSportsDbAsync(ct);
        if (sportsDb.Count >= 18) return sportsDb; // only use if near-complete table

        // 2) Try disk cache from last successful fetch
        try
        {
            var json = await File.ReadAllTextAsync(StandingsCacheFile, ct);
            var data = JsonSerializer.Deserialize<List<StandingDto>>(json);
            if (data is { Count: > 0 })
            {
                _logger.LogInformation("Serving standings from disk cache ({Count} teams)", data.Count);
                return data;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not load standings disk cache: {Msg}", ex.Message);
        }
        return [];
    }

    // ── Matches in date range (official PL API) ──────────────────────────────

    private async Task<IReadOnlyList<MatchDto>> FetchMatchesInRangeAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var seasonId = await GetSeasonIdAsync(ct);
        var client   = _factory.CreateClient(PlApiClient);

        // U = unplayed/scheduled, M = active matchday window, L = live, C = completed
        var upcomingTask  = FetchPlFixturesAsync(client, seasonId, "U,M,L", "asc",  80, ct);
        var completedTask = FetchPlFixturesAsync(client, seasonId, "C",     "desc", 80, ct);
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

    // ── Squad (official PL API: teams/{id}/compseasons/{seasonId}/staff) ────────

    private async Task<IReadOnlyList<PlayerDto>> FetchSquadFromPlApiAsync(int teamId, CancellationToken ct)
    {
        var seasonId = await GetSeasonIdAsync(ct);
        var client   = _factory.CreateClient(PlApiClient);
        var url      = $"teams/{teamId}/compseasons/{seasonId}/staff" +
                       $"?compSeasons={seasonId}&altIds=true&detail=2&type=player&page=0&pageSize=100";

        using var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("PL squad fetch failed for team {TeamId}: {Status}", teamId, resp.StatusCode);
            return [];
        }

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!root.TryGetProperty("players", out var players) || players.GetArrayLength() == 0)
        {
            _logger.LogInformation("No squad data from PL API for team {TeamId}", teamId);
            return [];
        }

        var result = new List<PlayerDto>();
        foreach (var p in players.EnumerateArray())
        {
            // name.display
            var name = "";
            if (p.TryGetProperty("name", out var nameEl) &&
                nameEl.TryGetProperty("display", out var displayEl))
                name = displayEl.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            // info.shirtNum / info.position
            var shirtNum = 0;
            var posCode  = "";
            if (p.TryGetProperty("info", out var info))
            {
                if (info.TryGetProperty("shirtNum", out var sn))
                    shirtNum = sn.ValueKind == JsonValueKind.Number ? sn.GetInt32() : 0;
                if (info.TryGetProperty("position", out var pos))
                    posCode = pos.GetString() ?? "";
            }

            // appearances this season (top-level field from PL API)
            var appearances = 0;
            if (p.TryGetProperty("appearances", out var apEl) &&
                apEl.ValueKind == JsonValueKind.Number)
                appearances = apEl.GetInt32();

            // ── First-team filter ────────────────────────────────────────────
            // Include the player if they have a standard squad number (1–45)
            // OR have made at least one appearance this season.
            // Numbers 46+ with zero appearances are academy/youth players.
            var isFirstTeam = (shirtNum >= 1 && shirtNum <= 45) || appearances > 0;
            if (!isFirstTeam) continue;

            result.Add(new PlayerDto(
                PlayerId: ParseInt(p, "id"),
                TeamId:   teamId,
                Name:     name,
                Number:   shirtNum,
                Position: MapPlPosition(posCode)
            ));
        }

        result.Sort((a, b) =>
        {
            var po = PositionOrder(a.Position).CompareTo(PositionOrder(b.Position));
            if (po != 0) return po;
            var na = a.Number > 0 ? a.Number : int.MaxValue;
            var nb = b.Number > 0 ? b.Number : int.MaxValue;
            return na.CompareTo(nb);
        });

        _logger.LogInformation(
            "Fetched {Count} first-team players for team {TeamId} from PL API", result.Count, teamId);
        return result;
    }

    private static int PositionOrder(string pos) => pos switch
    {
        "goalkeeper" => 0,
        "defender"   => 1,
        "midfielder" => 2,
        "forward"    => 3,
        _            => 4
    };

    /// <summary>Maps PL API position codes (G/D/M/F) to internal position strings.</summary>
    private static string MapPlPosition(string code) => code.ToUpperInvariant() switch
    {
        "G" => "goalkeeper",
        "D" => "defender",
        "M" => "midfielder",
        "F" => "forward",
        _   => "midfielder"
    };

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

    /// <summary>
    /// Extracts team name from a fixture team object.
    /// Tries team.name first, then team.club.name (structure varies by endpoint).
    /// </summary>
    private static string ParseTeamName(JsonElement teamEl)
    {
        if (teamEl.TryGetProperty("name", out var nameEl))
        {
            var n = nameEl.GetString();
            if (!string.IsNullOrWhiteSpace(n)) return n;
        }
        if (teamEl.TryGetProperty("club", out var club) &&
            club.TryGetProperty("name", out var clubName))
        {
            var n = clubName.GetString();
            if (!string.IsNullOrWhiteSpace(n)) return n;
        }
        return "";
    }

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
            var homeName = ParseTeamName(homeTeam);
            var awayName = ParseTeamName(awayTeam);

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

    // ── JSON parse helpers ───────────────────────────────────────────────────

    private static string Abbr(string teamName)
        => teamName.Length >= 3 ? teamName[..3].ToUpper() : teamName.ToUpper();

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
            // TryGetInt64 fails for scientific notation floats (e.g. 1.7741052E12)
            if (val.TryGetInt64(out var l)) return l;
            var d = val.GetDouble();
            return double.IsNaN(d) || double.IsInfinity(d) ? 0L : (long)Math.Round(d);
        }
        if (val.ValueKind == JsonValueKind.String)
        {
            var s = val.GetString() ?? "";
            // Handle scientific notation strings
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                return (long)Math.Round(d);
        }
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
