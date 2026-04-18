namespace PremierLeagueBot.Data.Entities;

public class Friendship
{
    public int Id { get; set; }
    public long RequesterId { get; set; }
    public long AddresseeId { get; set; }
    public string Status { get; set; } = "pending"; // "pending" | "accepted"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Requester { get; set; } = null!;
    public User Addressee { get; set; } = null!;
}
