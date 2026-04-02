using PremierLeagueBot.Models.Api;

namespace PremierLeagueBot.Services.Football;

public interface IFootballApiClient
{
    /// <summary>Returns current EPL standings sorted by rank.</summary>
    Task<IReadOnlyList<StandingDto>> GetStandingsAsync(CancellationToken ct = default);

    /// <summary>Returns matches in the given date range.</summary>
    Task<IReadOnlyList<MatchDto>> GetMatchesAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Returns squad (players) for the given team.</summary>
    Task<IReadOnlyList<PlayerDto>> GetTeamSquadAsync(int teamId, CancellationToken ct = default);

    /// <summary>Returns last N finished matches for the given team.</summary>
    Task<IReadOnlyList<MatchDto>> GetRecentMatchesAsync(int teamId, int count = 5, CancellationToken ct = default);

    /// <summary>Returns latest news for the given team (or all EPL news if teamId is null).</summary>
    Task<IReadOnlyList<NewsDto>> GetNewsAsync(int? teamId = null, CancellationToken ct = default);
}
