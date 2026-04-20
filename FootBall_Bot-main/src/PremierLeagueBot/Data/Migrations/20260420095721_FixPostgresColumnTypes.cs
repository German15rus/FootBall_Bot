using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PremierLeagueBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixPostgresColumnTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration only fixes column types for PostgreSQL.
            // SQLite does not enforce column types, so no changes are needed there.
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            // ── Fix autoincrement sequences (Sqlite:Autoincrement was ignored by Npgsql) ──
            migrationBuilder.Sql(@"
                CREATE SEQUENCE IF NOT EXISTS ""Predictions_Id_seq"";
                SELECT setval('""Predictions_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""Predictions""), 0) + 1, false);
                ALTER TABLE ""Predictions"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""Predictions_Id_seq""');
                ALTER SEQUENCE ""Predictions_Id_seq"" OWNED BY ""Predictions"".""Id"";

                CREATE SEQUENCE IF NOT EXISTS ""NotificationLogs_Id_seq"";
                SELECT setval('""NotificationLogs_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""NotificationLogs""), 0) + 1, false);
                ALTER TABLE ""NotificationLogs"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""NotificationLogs_Id_seq""');
                ALTER SEQUENCE ""NotificationLogs_Id_seq"" OWNED BY ""NotificationLogs"".""Id"";

                CREATE SEQUENCE IF NOT EXISTS ""UserAchievements_Id_seq"";
                SELECT setval('""UserAchievements_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""UserAchievements""), 0) + 1, false);
                ALTER TABLE ""UserAchievements"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""UserAchievements_Id_seq""');
                ALTER SEQUENCE ""UserAchievements_Id_seq"" OWNED BY ""UserAchievements"".""Id"";

                CREATE SEQUENCE IF NOT EXISTS ""Friendships_Id_seq"";
                SELECT setval('""Friendships_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""Friendships""), 0) + 1, false);
                ALTER TABLE ""Friendships"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""Friendships_Id_seq""');
                ALTER SEQUENCE ""Friendships_Id_seq"" OWNED BY ""Friendships"".""Id"";
            ");

            // ── Fix DateTime columns: TEXT → timestamp with time zone ──
            migrationBuilder.Sql(@"
                ALTER TABLE ""Predictions""
                    ALTER COLUMN ""CreatedAt"" TYPE timestamp with time zone USING ""CreatedAt""::timestamp with time zone,
                    ALTER COLUMN ""UpdatedAt"" TYPE timestamp with time zone USING ""UpdatedAt""::timestamp with time zone;

                ALTER TABLE ""NotificationLogs""
                    ALTER COLUMN ""SentAt"" TYPE timestamp with time zone USING ""SentAt""::timestamp with time zone;

                ALTER TABLE ""Matches""
                    ALTER COLUMN ""MatchDate"" TYPE timestamp with time zone USING ""MatchDate""::timestamp with time zone;

                ALTER TABLE ""Users""
                    ALTER COLUMN ""RegisteredAt"" TYPE timestamp with time zone USING ""RegisteredAt""::timestamp with time zone;

                ALTER TABLE ""UserAchievements""
                    ALTER COLUMN ""EarnedAt"" TYPE timestamp with time zone USING ""EarnedAt""::timestamp with time zone;

                ALTER TABLE ""Friendships""
                    ALTER COLUMN ""CreatedAt"" TYPE timestamp with time zone USING ""CreatedAt""::timestamp with time zone;
            ");

            // ── Fix bool columns: INTEGER → boolean ──
            migrationBuilder.Sql(@"
                ALTER TABLE ""Predictions""
                    ALTER COLUMN ""IsScored"" TYPE boolean USING CASE WHEN ""IsScored"" = 1 THEN true ELSE false END;

                ALTER TABLE ""Matches""
                    ALTER COLUMN ""PreMatchNotificationSent"" TYPE boolean USING CASE WHEN ""PreMatchNotificationSent"" = 1 THEN true ELSE false END,
                    ALTER COLUMN ""PostMatchNotificationSent"" TYPE boolean USING CASE WHEN ""PostMatchNotificationSent"" = 1 THEN true ELSE false END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — reversing type changes is not needed.
        }
    }
}
