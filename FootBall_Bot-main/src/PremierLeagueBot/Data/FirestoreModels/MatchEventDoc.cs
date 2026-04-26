using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

/// <summary>
/// Stored in subcollection: matches/{matchId}/events/{eventKey}
/// Tracks which live match events have already been broadcast.
/// </summary>
[FirestoreData]
public class MatchEventDoc
{
    /// <summary>Document ID = eventKey</summary>
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public int MatchId { get; set; }

    [FirestoreProperty]
    public string EventKey { get; set; } = "";

    [FirestoreProperty]
    public DateTime SentAt { get; set; }
}
