using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class AchievementDoc
{
    /// <summary>Document ID = achievement code (e.g. "ROOKIE")</summary>
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public string Code { get; set; } = "";

    [FirestoreProperty]
    public string NameRu { get; set; } = "";

    [FirestoreProperty]
    public string NameEn { get; set; } = "";

    [FirestoreProperty]
    public string DescriptionRu { get; set; } = "";

    [FirestoreProperty]
    public string DescriptionEn { get; set; } = "";

    [FirestoreProperty]
    public string Icon { get; set; } = "";
}
