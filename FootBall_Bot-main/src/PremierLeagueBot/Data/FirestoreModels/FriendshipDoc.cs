using Google.Cloud.Firestore;

namespace PremierLeagueBot.Data.FirestoreModels;

[FirestoreData]
public class FriendshipDoc
{
    /// <summary>Document ID = "{requesterId}_{addresseeId}"</summary>
    [FirestoreDocumentId]
    public string DocId { get; set; } = "";

    [FirestoreProperty]
    public long RequesterId { get; set; }

    [FirestoreProperty]
    public long AddresseeId { get; set; }

    /// <summary>pending | accepted</summary>
    [FirestoreProperty]
    public string Status { get; set; } = "pending";

    [FirestoreProperty]
    public DateTime CreatedAt { get; set; }
}
