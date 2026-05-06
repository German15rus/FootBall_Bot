using System.Security.Cryptography;
using System.Text;
using System.Web;
using PremierLeagueBot.Infrastructure;

namespace PremierLeagueBot.Tests;

public class TelegramInitDataValidatorTests
{
    private const string BotToken = "test-bot-token-123";

    /// <summary>Строит корректный initData со свежим auth_date и правильной подписью.</summary>
    private static string BuildValidInitData(long userId = 123456789, string firstName = "Test")
    {
        var authDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var userJson = $"{{\"id\":{userId},\"first_name\":\"{firstName}\"}}";

        var pairs = new SortedDictionary<string, string>
        {
            ["auth_date"] = authDate.ToString(),
            ["user"]      = userJson
        };

        var dataCheckString = string.Join("\n", pairs.Select(kv => $"{kv.Key}={kv.Value}"));

        var secretKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"),
            Encoding.UTF8.GetBytes(BotToken));

        var hash = Convert.ToHexString(
            HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))
        ).ToLowerInvariant();

        // Формируем строку как query-параметры
        var encoded = string.Join("&", pairs
            .Select(kv => $"{HttpUtility.UrlEncode(kv.Key)}={HttpUtility.UrlEncode(kv.Value)}")
            .Append($"hash={hash}"));

        return encoded;
    }

    [Fact]
    public void ValidInitData_ReturnsTrueAndCorrectUser()
    {
        var initData = BuildValidInitData(userId: 987654, firstName: "Иван");
        var result   = TelegramInitDataValidator.TryValidate(initData, BotToken, out var parsed);

        Assert.True(result);
        Assert.Equal(987654, parsed.TelegramId);
        Assert.Equal("Иван", parsed.FirstName);
    }

    [Fact]
    public void TamperedHash_ReturnsFalse()
    {
        var initData = BuildValidInitData() + "x"; // портим hash
        var result   = TelegramInitDataValidator.TryValidate(initData, BotToken, out _);
        Assert.False(result);
    }

    [Fact]
    public void EmptyString_ReturnsFalse()
    {
        var result = TelegramInitDataValidator.TryValidate("", BotToken, out _);
        Assert.False(result);
    }

    [Fact]
    public void WrongBotToken_ReturnsFalse()
    {
        var initData = BuildValidInitData();
        var result   = TelegramInitDataValidator.TryValidate(initData, "wrong-token", out _);
        Assert.False(result);
    }
}
