using Google.Cloud.Firestore;
using Microsoft.Data.Sqlite;
using PremierLeagueBot.Data.FirestoreModels;

namespace PremierLeagueBot.Tools;

/// <summary>
/// One-time migration script: reads all data from the SQLite database
/// and writes it to Firestore using batch operations (max 500 per batch).
///
/// Usage: run the app with --migrate argument:
///   dotnet run --migrate
///
/// Or call MigrateSqliteToFirestore.RunAsync() directly from Program.cs during startup.
/// </summary>
public static class MigrateSqliteToFirestore
{
    public static async Task RunAsync(string sqlitePath, FirestoreDb firestoreDb)
    {
        Console.WriteLine($"[Migration] Starting SQLite → Firestore migration from '{sqlitePath}'");

        if (!File.Exists(sqlitePath))
        {
            Console.WriteLine("[Migration] SQLite file not found — skipping.");
            return;
        }

        var connStr = $"Data Source={sqlitePath}";
        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        await MigrateTeamsAsync(conn, firestoreDb);
        await MigrateUsersAsync(conn, firestoreDb);
        await MigrateMatchesAsync(conn, firestoreDb);
        await MigratePlayersAsync(conn, firestoreDb);
        await MigrateAchievementsAsync(conn, firestoreDb);
        await MigratePredictionsAsync(conn, firestoreDb);
        await MigrateUserAchievementsAsync(conn, firestoreDb);
        await MigrateFriendshipsAsync(conn, firestoreDb);
        await MigrateNotificationLogsAsync(conn, firestoreDb);
        await MigrateMatchEventNotificationsAsync(conn, firestoreDb);

        Console.WriteLine("[Migration] Done!");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task BatchSetAsync<T>(
        FirestoreDb db, CollectionReference col,
        IEnumerable<(string docId, T data)> items) where T : class
    {
        const int size = 500;
        var list = items.ToList();
        for (int i = 0; i < list.Count; i += size)
        {
            var batch = db.StartBatch();
            foreach (var (docId, data) in list.Skip(i).Take(size))
                batch.Set(col.Document(docId), data);
            await batch.CommitAsync();
        }
        Console.WriteLine($"[Migration] {col.Id}: {list.Count} documents written");
    }

    private static int? ReadNullableInt(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }

    private static string? ReadNullableString(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static DateTime ReadDateTime(SqliteDataReader r, string col)
    {
        var s = r.GetString(r.GetOrdinal(col));
        return DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind)
                       .ToUniversalTime();
    }

    private static bool ReadBool(SqliteDataReader r, string col)
    {
        int ord = r.GetOrdinal(col);
        return !r.IsDBNull(ord) && r.GetInt32(ord) != 0;
    }

    // ── Table migrations ──────────────────────────────────────────────────────

    private static async Task MigrateTeamsAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("teams");
        var items = new List<(string, TeamDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TeamId, Name, ShortName, EmblemUrl, Position FROM Teams";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var doc = new TeamDoc
            {
                TeamId    = r.GetInt32(0),
                Name      = r.GetString(1),
                ShortName = r.GetString(2),
                EmblemUrl = ReadNullableString(r, "EmblemUrl"),
                Position  = ReadNullableInt(r, "Position")
            };
            items.Add((doc.TeamId.ToString(), doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigrateUsersAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("users");
        var items = new List<(string, UserDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TelegramId, Username, FirstName, AvatarUrl, LanguageCode, FavoriteTeamId, RegisteredAt, SessionToken FROM Users";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var username = ReadNullableString(r, "Username");
            var doc = new UserDoc
            {
                TelegramId    = r.GetInt64(0),
                Username      = username,
                FirstName     = r.GetString(2),
                AvatarUrl     = ReadNullableString(r, "AvatarUrl"),
                LanguageCode  = ReadNullableString(r, "LanguageCode"),
                FavoriteTeamId = ReadNullableInt(r, "FavoriteTeamId"),
                RegisteredAt  = ReadDateTime(r, "RegisteredAt"),
                SessionToken  = ReadNullableString(r, "SessionToken"),
                UsernameLower = username?.ToLower()
            };
            items.Add((doc.TelegramId.ToString(), doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigrateMatchesAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("matches");
        var items = new List<(string, MatchDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT MatchId, HomeTeamId, AwayTeamId, MatchDate, Stadium,
            HomeScore, AwayScore, Status, CompetitionId,
            PreMatchNotificationSent, PostMatchNotificationSent, HalftimeNotificationSent
            FROM Matches";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var doc = new MatchDoc
            {
                MatchId                  = r.GetInt32(0),
                HomeTeamId               = r.GetInt32(1),
                AwayTeamId               = r.GetInt32(2),
                MatchDate                = ReadDateTime(r, "MatchDate"),
                Stadium                  = ReadNullableString(r, "Stadium"),
                HomeScore                = ReadNullableInt(r, "HomeScore"),
                AwayScore                = ReadNullableInt(r, "AwayScore"),
                Status                   = r.GetString(r.GetOrdinal("Status")),
                CompetitionId            = r.GetInt32(r.GetOrdinal("CompetitionId")),
                PreMatchNotificationSent = ReadBool(r, "PreMatchNotificationSent"),
                PostMatchNotificationSent= ReadBool(r, "PostMatchNotificationSent"),
                HalftimeNotificationSent = ReadBool(r, "HalftimeNotificationSent")
            };
            items.Add((doc.MatchId.ToString(), doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigratePlayersAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("players");
        var items = new List<(string, PlayerDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PlayerId, TeamId, Name, Number, Position FROM Players";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var doc = new PlayerDoc
            {
                PlayerId = r.GetInt32(0),
                TeamId   = r.GetInt32(1),
                Name     = r.GetString(2),
                Number   = r.GetInt32(3),
                Position = r.GetString(4)
            };
            items.Add((doc.PlayerId.ToString(), doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigrateAchievementsAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("achievements");
        var items = new List<(string, AchievementDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Code, NameRu, NameEn, DescriptionRu, DescriptionEn, Icon FROM Achievements";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var doc = new AchievementDoc
            {
                Code          = r.GetString(0),
                NameRu        = r.GetString(1),
                NameEn        = r.GetString(2),
                DescriptionRu = r.GetString(3),
                DescriptionEn = r.GetString(4),
                Icon          = r.GetString(5)
            };
            items.Add((doc.Code, doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigratePredictionsAsync(SqliteConnection conn, FirestoreDb db)
    {
        // We need match data to fill denormalized fields — load matches first
        var matchMap = new Dictionary<int, (DateTime date, string status, int? hs, int? as_, int homeId, int awayId)>();
        await using (var mCmd = conn.CreateCommand())
        {
            mCmd.CommandText = "SELECT MatchId, MatchDate, Status, HomeScore, AwayScore, HomeTeamId, AwayTeamId FROM Matches";
            await using var mr = await mCmd.ExecuteReaderAsync();
            while (await mr.ReadAsync())
            {
                matchMap[mr.GetInt32(0)] = (
                    ReadDateTime(mr, "MatchDate"),
                    mr.GetString(mr.GetOrdinal("Status")),
                    ReadNullableInt(mr, "HomeScore"),
                    ReadNullableInt(mr, "AwayScore"),
                    mr.GetInt32(mr.GetOrdinal("HomeTeamId")),
                    mr.GetInt32(mr.GetOrdinal("AwayTeamId"))
                );
            }
        }

        // Load teams for name/emblem denormalization
        var teamMap = new Dictionary<int, (string name, string? emblem)>();
        await using (var tCmd = conn.CreateCommand())
        {
            tCmd.CommandText = "SELECT TeamId, Name, EmblemUrl FROM Teams";
            await using var tr = await tCmd.ExecuteReaderAsync();
            while (await tr.ReadAsync())
                teamMap[tr.GetInt32(0)] = (tr.GetString(1), ReadNullableString(tr, "EmblemUrl"));
        }

        var col   = db.Collection("predictions");
        var items = new List<(string, PredictionDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT TelegramId, MatchId, PredictedHomeScore, PredictedAwayScore,
            CreatedAt, UpdatedAt, PointsAwarded, IsScored FROM Predictions";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var telegramId = r.GetInt64(0);
            var matchId    = r.GetInt32(1);

            matchMap.TryGetValue(matchId, out var m);
            teamMap.TryGetValue(m.homeId, out var homeTeam);
            teamMap.TryGetValue(m.awayId, out var awayTeam);

            var doc = new PredictionDoc
            {
                TelegramId         = telegramId,
                MatchId            = matchId,
                PredictedHomeScore = r.GetInt32(2),
                PredictedAwayScore = r.GetInt32(3),
                CreatedAt          = ReadDateTime(r, "CreatedAt"),
                UpdatedAt          = ReadDateTime(r, "UpdatedAt"),
                PointsAwarded      = ReadNullableInt(r, "PointsAwarded"),
                IsScored           = ReadBool(r, "IsScored"),
                MatchDate          = m.date,
                MatchStatus        = m.status ?? "scheduled",
                MatchHomeScore     = m.hs,
                MatchAwayScore     = m.as_,
                HomeTeamId         = m.homeId,
                AwayTeamId         = m.awayId,
                HomeTeamName       = homeTeam.name ?? "?",
                AwayTeamName       = awayTeam.name ?? "?",
                HomeTeamEmblem     = homeTeam.emblem,
                AwayTeamEmblem     = awayTeam.emblem
            };
            items.Add(($"{telegramId}_{matchId}", doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigrateUserAchievementsAsync(SqliteConnection conn, FirestoreDb db)
    {
        // Load achievement definitions for denormalization
        var achMap = new Dictionary<string, (string nameRu, string nameEn, string descRu, string descEn, string icon)>();
        await using (var aCmd = conn.CreateCommand())
        {
            aCmd.CommandText = "SELECT Code, NameRu, NameEn, DescriptionRu, DescriptionEn, Icon FROM Achievements";
            await using var ar = await aCmd.ExecuteReaderAsync();
            while (await ar.ReadAsync())
                achMap[ar.GetString(0)] = (ar.GetString(1), ar.GetString(2), ar.GetString(3), ar.GetString(4), ar.GetString(5));
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TelegramId, AchievementCode, EarnedAt FROM UserAchievements";
        await using var r = await cmd.ExecuteReaderAsync();

        // Group by user to write subcollection documents
        var byUser = new Dictionary<long, List<UserAchievementDoc>>();
        while (await r.ReadAsync())
        {
            var uid  = r.GetInt64(0);
            var code = r.GetString(1);
            achMap.TryGetValue(code, out var a);

            var doc = new UserAchievementDoc
            {
                TelegramId      = uid,
                AchievementCode = code,
                EarnedAt        = ReadDateTime(r, "EarnedAt"),
                NameRu          = a.nameRu ?? "",
                NameEn          = a.nameEn ?? "",
                DescriptionRu   = a.descRu ?? "",
                DescriptionEn   = a.descEn ?? "",
                Icon            = a.icon ?? ""
            };

            if (!byUser.TryGetValue(uid, out var list))
                byUser[uid] = list = new List<UserAchievementDoc>();
            list.Add(doc);
        }

        int total = 0;
        foreach (var (uid, docs) in byUser)
        {
            var col = db.Collection("users").Document(uid.ToString()).Collection("achievements");
            await BatchSetAsync(db, col, docs.Select(d => (d.AchievementCode, d)));
            total += docs.Count;
        }

        Console.WriteLine($"[Migration] userAchievements: {total} documents written");
    }

    private static async Task MigrateFriendshipsAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("friendships");
        var items = new List<(string, FriendshipDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT RequesterId, AddresseeId, Status, CreatedAt FROM Friendships";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var requesterId = r.GetInt64(0);
            var addresseeId = r.GetInt64(1);
            var doc = new FriendshipDoc
            {
                RequesterId = requesterId,
                AddresseeId = addresseeId,
                Status      = r.GetString(2),
                CreatedAt   = ReadDateTime(r, "CreatedAt")
            };
            items.Add(($"{requesterId}_{addresseeId}", doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigrateNotificationLogsAsync(SqliteConnection conn, FirestoreDb db)
    {
        var col   = db.Collection("notificationLogs");
        var items = new List<(string, NotificationLogDoc)>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, TelegramId, Message, SentAt FROM NotificationLogs";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var doc = new NotificationLogDoc
            {
                TelegramId = r.GetInt64(1),
                Message    = r.GetString(2),
                SentAt     = ReadDateTime(r, "SentAt")
            };
            items.Add((r.GetInt32(0).ToString(), doc));
        }

        await BatchSetAsync(db, col, items);
    }

    private static async Task MigrateMatchEventNotificationsAsync(SqliteConnection conn, FirestoreDb db)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MatchId, EventKey, SentAt FROM MatchEventNotifications";
        await using var r = await cmd.ExecuteReaderAsync();

        var byMatch = new Dictionary<int, List<MatchEventDoc>>();
        while (await r.ReadAsync())
        {
            var matchId  = r.GetInt32(0);
            var eventKey = r.GetString(1);
            var doc = new MatchEventDoc
            {
                MatchId  = matchId,
                EventKey = eventKey,
                SentAt   = ReadDateTime(r, "SentAt")
            };
            if (!byMatch.TryGetValue(matchId, out var list))
                byMatch[matchId] = list = new List<MatchEventDoc>();
            list.Add(doc);
        }

        int total = 0;
        foreach (var (matchId, docs) in byMatch)
        {
            var col = db.Collection("matches").Document(matchId.ToString()).Collection("events");
            await BatchSetAsync(db, col, docs.Select(d => (d.EventKey, d)));
            total += docs.Count;
        }

        Console.WriteLine($"[Migration] matchEvents: {total} documents written");
    }
}
