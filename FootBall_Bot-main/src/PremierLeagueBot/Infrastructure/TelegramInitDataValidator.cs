using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace PremierLeagueBot.Infrastructure;

/// <summary>
/// Validates the initData string sent by Telegram WebApp using HMAC-SHA256.
/// Spec: https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public static class TelegramInitDataValidator
{
    /// <summary>Maximum age of initData before it is considered expired (Telegram: 24 h).</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static bool TryValidate(string initData, string botToken, out ParsedInitData parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(initData)) return false;

        try
        {
            var pairs = HttpUtility.ParseQueryString(initData);
            var hash  = pairs["hash"];
            if (string.IsNullOrEmpty(hash)) return false;

            // Build the data-check string: sorted key=value pairs joined by \n (without hash)
            var dataCheckString = string.Join("\n",
                pairs.AllKeys
                     .Where(k => k != "hash")
                     .OrderBy(k => k)
                     .Select(k => $"{k}={pairs[k]}"));

            // secret_key = HMAC-SHA256(bot_token, "WebAppData")
            var secretKey = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes("WebAppData"),
                Encoding.UTF8.GetBytes(botToken));

            var expectedHash = Convert.ToHexString(
                HMACSHA256.HashData(secretKey, Encoding.UTF8.GetBytes(dataCheckString))
            ).ToLowerInvariant();

            if (expectedHash != hash.ToLowerInvariant()) return false;

            // Check auth_date to reject stale data
            if (long.TryParse(pairs["auth_date"], out var authDate))
            {
                var issued = DateTimeOffset.FromUnixTimeSeconds(authDate).UtcDateTime;
                if (DateTime.UtcNow - issued > MaxAge) return false;
            }

            // Parse user JSON from the "user" field
            var userJson = pairs["user"];
            if (string.IsNullOrEmpty(userJson)) return false;

            parsed = ParseUser(userJson);
            return parsed.TelegramId != 0;
        }
        catch
        {
            return false;
        }
    }

    private static ParsedInitData ParseUser(string userJson)
    {
        // Minimal JSON parsing without System.Text.Json overhead
        static string? ExtractString(string json, string key)
        {
            var search = $"\"{key}\":";
            var idx = json.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += search.Length;
            while (idx < json.Length && json[idx] == ' ') idx++;
            if (idx >= json.Length) return null;
            if (json[idx] == '"')
            {
                idx++;
                var end = json.IndexOf('"', idx);
                return end < 0 ? null : json[idx..end];
            }
            // Number
            var numEnd = idx;
            while (numEnd < json.Length && (char.IsDigit(json[numEnd]) || json[numEnd] == '-')) numEnd++;
            return json[idx..numEnd];
        }

        var id        = ExtractString(userJson, "id");
        var firstName = ExtractString(userJson, "first_name") ?? "";
        var username  = ExtractString(userJson, "username");
        var langCode  = ExtractString(userJson, "language_code");

        return long.TryParse(id, out var telegramId)
            ? new ParsedInitData(telegramId, firstName, username, langCode)
            : default;
    }
}

public readonly record struct ParsedInitData(
    long   TelegramId,
    string FirstName,
    string? Username,
    string? LanguageCode);
