using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class PredictionDoc
{
    /// <summary>Document ID = "{telegramId}_{matchId}"</summary>
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public long TelegramId { get; set; }

    [FirestoreProperty]
    public int MatchId { get; set; }

    [FirestoreProperty]
    public int PredictedHomeScore { get; set; }

    [FirestoreProperty]
    public int PredictedAwayScore { get; set; }

    [FirestoreProperty]
    public DateTime CreatedAt { get; set; }

    [FirestoreProperty]
    public DateTime UpdatedAt { get; set; }

    /// <summary>null = not yet scored; 0/1/3 after match finishes</summary>
    [FirestoreProperty]
    public int? PointsAwarded { get; set; }

    [FirestoreProperty]
    public bool IsScored { get; set; }

    // Denormalized match fields — updated by DataUpdateService when match changes.
    [FirestoreProperty]
    public DateTime MatchDate { get; set; }

    [FirestoreProperty]
    public string MatchStatus { get; set; } = "scheduled";

    [FirestoreProperty]
    public int? MatchHomeScore { get; set; }

    [FirestoreProperty]
    public int? MatchAwayScore { get; set; }

    [FirestoreProperty]
    public int HomeTeamId { get; set; }

    [FirestoreProperty]
    public int AwayTeamId { get; set; }

    [FirestoreProperty]
    public string HomeTeamName { get; set; } = "";

    [FirestoreProperty]
    public string AwayTeamName { get; set; } = "";

    [FirestoreProperty]
    public string? HomeTeamEmblem { get; set; }

    [FirestoreProperty]
    public string? AwayTeamEmblem { get; set; }
}
