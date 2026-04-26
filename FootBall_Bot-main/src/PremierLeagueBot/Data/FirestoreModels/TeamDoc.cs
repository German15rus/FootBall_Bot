using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class TeamDoc
{
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public int TeamId { get; set; }

    [FirestoreProperty]
    public string Name { get; set; } = "";

    [FirestoreProperty]
    public string ShortName { get; set; } = "";

    [FirestoreProperty]
    public string? EmblemUrl { get; set; }

    [FirestoreProperty]
    public int? Position { get; set; }
}
