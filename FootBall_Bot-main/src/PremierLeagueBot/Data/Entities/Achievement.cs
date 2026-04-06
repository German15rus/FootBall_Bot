namespace PremierLeagueBot.Data.Entities;

public class Achievement
{
    public string Code { get; set; } = string.Empty;
    public string NameRu { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionRu { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    public ICollection<UserAchievement> UserAchievements { get; set; } = [];
}
