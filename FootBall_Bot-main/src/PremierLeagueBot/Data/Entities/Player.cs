namespace PremierLeagueBot.Data.Entities;

public class Player
{
    public int PlayerId { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }

    /// <summary>goalkeeper | defender | midfielder | forward</summary>
    public string Position { get; set; } = string.Empty;

    public Team Team { get; set; } = null!;
}
