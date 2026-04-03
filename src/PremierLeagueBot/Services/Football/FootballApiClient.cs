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
/// Football data from TheSportsDB (free, no registration required).
/// News from BBC Sport EPL RSS feed (free, no registration required).
///
/// TheSportsDB docs: https://www.thesportsdb.com/api.php
/// EPL league ID: 4328
/// </summary>
public sealed class FootballApiClient(
    HttpClient http,
    IMemoryCache cache,
    IOptions<FootballApiOptions> opts,
    ILogger<FootballApiClient> logger) : IFootballApiClient
{
    private readonly FootballApiOptions _opts = opts.Value;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── Cache helper ─────────────────────────────────────────────────────────

    private async Task<T> GetCachedAsync<T>(
        string key, TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;
        var result = await factory(ct);
        cache.Set(key, result, ttl);
        return result;
    }

    // ── Public interface ─────────────────────────────────────────────────────

    public Task<IReadOnlyList<StandingDto>> GetStandingsAsync(CancellationToken ct = default)
        => GetCachedAsync("standings", TimeSpan.FromMinutes(15), FetchStandingsAsync, ct);

    public Task<IReadOnlyList<MatchDto>> GetMatchesAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
        => GetCachedAsync(
            $"matches:{from:yyyyMMdd}:{to:yyyyMMdd}",
            TimeSpan.FromMinutes(10),
            c => FetchMatchesInRangeAsync(from, to, c), ct);

    public Task<IReadOnlyList<PlayerDto>> GetTeamSquadAsync(int teamId, CancellationToken ct = default)
        => GetCachedAsync(
            $"squad:{teamId}",
            TimeSpan.FromHours(24),
            c => FetchSquadAsync(teamId, c), ct);

    public Task<IReadOnlyList<MatchDto>> GetRecentMatchesAsync(
        int teamId, int count = 5, CancellationToken ct = default)
        => GetCachedAsync(
            $"recent:{teamId}",
            TimeSpan.FromMinutes(15),
            c => FetchLastTeamMatchesAsync(teamId, count, c), ct);

    public Task<IReadOnlyList<NewsDto>> GetNewsAsync(
        int? teamId = null, CancellationToken ct = default)
        => GetCachedAsync(
            $"news:{teamId}",
            TimeSpan.FromHours(1),
            c => FetchNewsAsync(teamId, c), ct);

    // ── Standings (TheSportsDB lookuptable) ──────────────────────────────────
    // GET /lookuptable.php?l=4328&s=2024-2025

    private async Task<IReadOnlyList<StandingDto>> FetchStandingsAsync(CancellationToken ct)
    {
        var url = $"lookuptable.php?l={_opts.LeagueId}&s={Uri.EscapeDataString(_opts.Season)}";
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        if (!root.TryGetProperty("table", out var table) || table.ValueKind == JsonValueKind.Null)
        {
            logger.LogWarning("TheSportsDB standings returned null table");
            return [];
        }

        var result = new List<StandingDto>();
        int rank = 1;
        foreach (var item in table.EnumerateArray())
        {
            var teamId   = ParseInt(item, "idTeam");
            var played   = ParseInt(item, "intPlayed");
            var won      = ParseInt(item, "intWin");
            var drawn    = ParseInt(item, "intDraw");
            var lost     = ParseInt(item, "intLoss");
            var gf       = ParseInt(item, "intGoalsFor");
            var ga       = ParseInt(item, "intGoalsAgainst");
            var points   = ParseInt(item, "intPoints");
            var teamName = item.TryGetProperty("strTeam", out var tn) ? tn.GetString() ?? "" : "";
            var badge    = item.TryGetProperty("strTeamBadge", out var b) ? b.GetString() : null;

            result.Add(new StandingDto(
                Rank:           rank++,
                TeamId:         teamId,
                TeamName:       teamName,
                ShortName:      teamName.Length >= 3 ? teamName[..3].ToUpper() : teamName.ToUpper(),
                EmblemUrl:      badge,
                Played:         played,
                Won:            won,
                Drawn:          drawn,
                Lost:           lost,
                GoalsFor:       gf,
                GoalsAgainst:   ga,
                GoalDifference: gf - ga,
                Points:         points
            ));
        }

        logger.LogInformation("Fetched {Count} standings from TheSportsDB", result.Count);
        return result;
    }

    // ── Matches in date range (next + past league events merged) ─────────────
    // GET /eventsnextleague.php?id=4328
    // GET /eventspastleague.php?id=4328

    private async Task<IReadOnlyList<MatchDto>> FetchMatchesInRangeAsync(
        DateTime from, DateTime to, CancellationToken ct)
    {
        var nextTask = FetchLeagueEventsAsync("eventsnextleague.php", ct);
        var pastTask = FetchLeagueEventsAsync("eventspastleague.php", ct);
        await Task.WhenAll(nextTask, pastTask);

        var all = nextTask.Result.Concat(pastTask.Result)
            .DistinctBy(m => m.MatchId)
            .Where(m => m.MatchDate >= from && m.MatchDate <= to)
            .OrderBy(m => m.MatchDate)
            .ToList();

        return all;
    }

    private async Task<IReadOnlyList<MatchDto>> FetchLeagueEventsAsync(
        string endpoint, CancellationToken ct)
    {
        var url = $"{endpoint}?id={_opts.LeagueId}";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var key  = endpoint.Contains("next") ? "events" : "events";

        if (!root.TryGetProperty("events", out var events) || events.ValueKind == JsonValueKind.Null)
            return [];

        return ParseEvents(events);
    }

    // ── Last N matches for a team ─────────────────────────────────────────────
    // GET /eventslast.php?id={teamId}

    private async Task<IReadOnlyList<MatchDto>> FetchLastTeamMatchesAsync(
        int teamId, int count, CancellationToken ct)
    {
        var url = $"eventslast.php?id={teamId}";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        if (!root.TryGetProperty("results", out var results) || results.ValueKind == JsonValueKind.Null)
            return [];

        return ParseEvents(results).TakeLast(count).ToList();
    }

    // ── Squad (TheSportsDB lookup_all_players) ────────────────────────────────
    // GET /lookup_all_players.php?id={teamId}

    private async Task<IReadOnlyList<PlayerDto>> FetchSquadAsync(int teamId, CancellationToken ct)
    {
        var url = $"lookup_all_players.php?id={teamId}";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Squad fetch failed for team {TeamId}: {Code}", teamId, resp.StatusCode);
            return [];
        }

        var root = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        if (!root.TryGetProperty("player", out var players) || players.ValueKind == JsonValueKind.Null)
        {
            logger.LogInformation("No squad data for team {TeamId} (may require Patreon tier)", teamId);
            return [];
        }

        var result = new List<PlayerDto>();
        foreach (var p in players.EnumerateArray())
        {
            var pos = p.TryGetProperty("strPosition", out var posEl) ? posEl.GetString() ?? "" : "";
            var numStr = p.TryGetProperty("strNumber", out var numEl) ? numEl.GetString() : null;
            _ = int.TryParse(numStr, out var number);

            result.Add(new PlayerDto(
                PlayerId: ParseInt(p, "idPlayer"),
                TeamId:   teamId,
                Name:     p.TryGetProperty("strPlayer", out var nm) ? nm.GetString() ?? "" : "",
                Number:   number,
                Position: MapPosition(pos)
            ));
        }

        return result;
    }

    // ── News from BBC Sport EPL RSS ───────────────────────────────────────────

    private async Task<IReadOnlyList<NewsDto>> FetchNewsAsync(int? teamId, CancellationToken ct)
    {
        using var resp = await http.GetAsync(_opts.NewsRssUrl, ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("BBC RSS fetch failed: {Code}", resp.StatusCode);
            return [];
        }

        var xml = await resp.Content.ReadAsStringAsync(ct);
        var doc = XDocument.Parse(xml);
        XNamespace media = "http://search.yahoo.com/mrss/";

        var items = doc.Descendants("item")
            .Select(item => new NewsDto(
                Title:         item.Element("title")?.Value ?? "",
                Summary:       StripHtml(item.Element("description")?.Value ?? ""),
                Url:           item.Element("link")?.Value ?? "",
                PublishedAt:   ParseRssDate(item.Element("pubDate")?.Value),
                RelatedTeamId: teamId
            ))
            .Where(n => !string.IsNullOrEmpty(n.Url));

        // If teamId is provided, filter by team name in title/summary
        if (teamId.HasValue)
        {
            // We don't have team name here, so filtering is done in NewsNotificationService
            // which passes the team name. Return all for now — caller filters.
        }

        return items.Take(10).ToList();
    }

    // ── Shared event parser ───────────────────────────────────────────────────

    private static IReadOnlyList<MatchDto> ParseEvents(JsonElement events)
    {
        var result = new List<MatchDto>();
        foreach (var e in events.EnumerateArray())
        {
            var dateStr  = e.TryGetProperty("dateEvent", out var d)  ? d.GetString() : null;
            var timeStr  = e.TryGetProperty("strTime",   out var t)  ? t.GetString() : "00:00:00";
            var statusRaw = e.TryGetProperty("strStatus", out var s) ? s.GetString() ?? "NS" : "NS";

            if (!DateTime.TryParse($"{dateStr}T{timeStr}",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var matchDate))
                continue;

            var homeScore = ParseNullableInt(e, "intHomeScore");
            var awayScore = ParseNullableInt(e, "intAwayScore");

            result.Add(new MatchDto(
                MatchId:      ParseInt(e, "idEvent"),
                HomeTeamId:   ParseInt(e, "idHomeTeam"),
                HomeTeamName: e.TryGetProperty("strHomeTeam", out var hn) ? hn.GetString() ?? "" : "",
                AwayTeamId:   ParseInt(e, "idAwayTeam"),
                AwayTeamName: e.TryGetProperty("strAwayTeam", out var an) ? an.GetString() ?? "" : "",
                MatchDate:    matchDate.ToUniversalTime(),
                Stadium:      e.TryGetProperty("strVenue", out var v) ? v.GetString() : null,
                HomeScore:    homeScore,
                AwayScore:    awayScore,
                Status:       MapStatus(statusRaw)
            ));
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MapStatus(string raw) => raw.ToUpper() switch
    {
        "NS" or "TBD" or "POSTPONED" => "scheduled",
        "FT" or "AET" or "PEN"       => "finished",
        _                             => raw.Length > 0 ? "live" : "scheduled"
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

    private static int ParseInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return 0;
        if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
        if (val.ValueKind == JsonValueKind.String)
            return int.TryParse(val.GetString(), out var n) ? n : 0;
        return 0;
    }

    private static int? ParseNullableInt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var val)) return null;
        if (val.ValueKind == JsonValueKind.Null) return null;
        if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
        if (val.ValueKind == JsonValueKind.String)
            return int.TryParse(val.GetString(), out var n) ? n : null;
        return null;
    }

    private static DateTime ParseRssDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return DateTime.UtcNow;
        // RFC 822 format: "Sun, 02 Apr 2025 14:30:00 GMT"
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return DateTime.UtcNow;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        // Simple tag removal – sufficient for RSS descriptions
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
    }
}
