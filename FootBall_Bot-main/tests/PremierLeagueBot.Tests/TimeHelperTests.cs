using PremierLeagueBot.Formatters;

namespace PremierLeagueBot.Tests;

public class TimeHelperTests
{
    [Fact]
    public void Winter_UTC_IsGMT_SameHour()
    {
        // В зимнее время Лондон = UTC (GMT+0)
        var utc    = new DateTime(2025, 12, 15, 15, 0, 0, DateTimeKind.Utc);
        var london = TimeHelper.ToLondonTime(utc);
        Assert.Equal(15, london.Hour);
    }

    [Fact]
    public void Summer_UTC_IsBST_PlusOneHour()
    {
        // В летнее время Лондон = BST (UTC+1)
        var utc    = new DateTime(2025, 7, 15, 14, 0, 0, DateTimeKind.Utc);
        var london = TimeHelper.ToLondonTime(utc);
        Assert.Equal(15, london.Hour);
    }

    [Fact]
    public void ReturnsCorrectDate_NotServerLocalTime()
    {
        // Независимо от настроек сервера результат должен быть London-time
        var utc    = new DateTime(2025, 8, 10, 20, 30, 0, DateTimeKind.Utc);
        var london = TimeHelper.ToLondonTime(utc);
        // В августе BST = UTC+1, значит 21:30
        Assert.Equal(21, london.Hour);
        Assert.Equal(30, london.Minute);
    }
}
