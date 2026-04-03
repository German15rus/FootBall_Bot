namespace PremierLeagueBot.Models.Bot;

/// <summary>
/// Structured callback data for inline keyboard buttons.
/// Format: "action:param"
/// </summary>
public static class CallbackData
{
    public const string ShowTable      = "show_table";
    public const string ShowMatches    = "show_matches";
    public const string SelectTeam     = "select_team";
    public const string MyTeam         = "my_team";
    public const string RemoveMyTeam   = "remove_my_team";

    /// <summary>Callback for viewing a specific team: "team_info:42"</summary>
    public static string TeamInfo(int teamId) => $"team_info:{teamId}";

    /// <summary>Callback for setting favourite team: "set_fav:42"</summary>
    public static string SetFavorite(int teamId) => $"set_fav:{teamId}";

    public static bool TryParse(string data, out string action, out string param)
    {
        var parts = data.Split(':', 2);
        action = parts[0];
        param  = parts.Length > 1 ? parts[1] : string.Empty;
        return true;
    }
}
