using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class UserDoc
{
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public long TelegramId { get; set; }

    [FirestoreProperty]
    public string? Username { get; set; }

    [FirestoreProperty]
    public string FirstName { get; set; } = "";

    [FirestoreProperty]
    public string? AvatarUrl { get; set; }

    [FirestoreProperty]
    public string? LanguageCode { get; set; }

    [FirestoreProperty]
    public int? FavoriteTeamId { get; set; }

    [FirestoreProperty]
    public DateTime RegisteredAt { get; set; }

    [FirestoreProperty]
    public string? SessionToken { get; set; }

    /// <summary>Lowercase copy of Username for case-insensitive lookup.</summary>
    [FirestoreProperty]
    public string? UsernameLower { get; set; }
}
