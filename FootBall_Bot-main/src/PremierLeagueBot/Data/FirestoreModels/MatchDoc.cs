using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class MatchDoc
{
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public int MatchId { get; set; }

    [FirestoreProperty]
    public int HomeTeamId { get; set; }

    [FirestoreProperty]
    public int AwayTeamId { get; set; }

    [FirestoreProperty]
    public DateTime MatchDate { get; set; }

    [FirestoreProperty]
    public string? Stadium { get; set; }

    [FirestoreProperty]
    public int? HomeScore { get; set; }

    [FirestoreProperty]
    public int? AwayScore { get; set; }

    /// <summary>scheduled | live | finished</summary>
    [FirestoreProperty]
    public string Status { get; set; } = "scheduled";

    /// <summary>1 = Premier League, 2 = Champions League</summary>
    [FirestoreProperty]
    public int CompetitionId { get; set; } = 1;

    [FirestoreProperty]
    public bool PreMatchNotificationSent { get; set; }

    [FirestoreProperty]
    public bool PostMatchNotificationSent { get; set; }

    [FirestoreProperty]
    public bool HalftimeNotificationSent { get; set; }
}
