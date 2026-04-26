using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

/// <summary>
/// Stored in subcollection: users/{telegramId}/achievements/{achievementCode}
/// Achievement data is denormalized here to avoid extra reads on profile load.
/// </summary>
[FirestoreData]
public class UserAchievementDoc
{
    /// <summary>Document ID = achievement code</summary>
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public long TelegramId { get; set; }

    [FirestoreProperty]
    public string AchievementCode { get; set; } = "";

    [FirestoreProperty]
    public DateTime EarnedAt { get; set; }

    // Denormalized from AchievementDoc
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
