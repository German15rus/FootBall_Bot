namespace PremierLeagueBot.Data.Entities;

public class Match
{
    public int MatchId { get; set; }
    public int HomeTeamId { get; set; }
    public int AwayTeamId { get; set; }
    public DateTime MatchDate { get; set; }
    public string? Stadium { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    /// <summary>scheduled | live | finished</summary>
    public string Status { get; set; } = "scheduled";

    /// <summary>1 = Premier League, 2 = Champions League</summary>
    public int CompetitionId { get; set; } = 1;

    /// <summary>Flag: pre-match notification already sent</summary>
    public bool PreMatchNotificationSent { get; set; }

    /// <summary>Flag: post-match notification already sent</summary>
    public bool PostMatchNotificationSent { get; set; }

    /// <summary>Flag: half-time notification already sent</summary>
    public bool HalftimeNotificationSent { get; set; }

    public Team HomeTeam { get; set; } = null!;
    public Team AwayTeam { get; set; } = null!;
}
