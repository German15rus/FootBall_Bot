using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class NotificationLogDoc
{
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public long TelegramId { get; set; }

    [FirestoreProperty]
    public string Message { get; set; } = "";

    [FirestoreProperty]
    public DateTime SentAt { get; set; }
}
