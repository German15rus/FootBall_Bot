using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class PlayerDoc
{
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public int PlayerId { get; set; }

    [FirestoreProperty]
    public int TeamId { get; set; }

    [FirestoreProperty]
    public string Name { get; set; } = "";

    [FirestoreProperty]
    public int Number { get; set; }

    /// <summary>goalkeeper | defender | midfielder | forward</summary>
    [FirestoreProperty]
    public string Position { get; set; } = "";
}
