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
            // Only runs on PostgreSQL — SQLite handles autoincrement natively.
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            // The original migrations were generated for SQLite and used the
            // Sqlite:Autoincrement annotation, which Npgsql ignores. As a result,
            // auto-increment integer PK columns have no SERIAL sequence in PostgreSQL,
            // causing INSERT to fail. The statements below add the missing sequences.

            migrationBuilder.Sql(@"CREATE SEQUENCE IF NOT EXISTS ""Predictions_Id_seq"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Predictions"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""Predictions_Id_seq""');");
            migrationBuilder.Sql(@"SELECT setval('""Predictions_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""Predictions""), 0) + 1, false);");
            migrationBuilder.Sql(@"ALTER SEQUENCE ""Predictions_Id_seq"" OWNED BY ""Predictions"".""Id"";");

            migrationBuilder.Sql(@"CREATE SEQUENCE IF NOT EXISTS ""NotificationLogs_Id_seq"";");
            migrationBuilder.Sql(@"ALTER TABLE ""NotificationLogs"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""NotificationLogs_Id_seq""');");
            migrationBuilder.Sql(@"SELECT setval('""NotificationLogs_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""NotificationLogs""), 0) + 1, false);");
            migrationBuilder.Sql(@"ALTER SEQUENCE ""NotificationLogs_Id_seq"" OWNED BY ""NotificationLogs"".""Id"";");

            migrationBuilder.Sql(@"CREATE SEQUENCE IF NOT EXISTS ""UserAchievements_Id_seq"";");
            migrationBuilder.Sql(@"ALTER TABLE ""UserAchievements"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""UserAchievements_Id_seq""');");
            migrationBuilder.Sql(@"SELECT setval('""UserAchievements_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""UserAchievements""), 0) + 1, false);");
            migrationBuilder.Sql(@"ALTER SEQUENCE ""UserAchievements_Id_seq"" OWNED BY ""UserAchievements"".""Id"";");

            migrationBuilder.Sql(@"CREATE SEQUENCE IF NOT EXISTS ""Friendships_Id_seq"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Friendships"" ALTER COLUMN ""Id"" SET DEFAULT nextval('""Friendships_Id_seq""');");
            migrationBuilder.Sql(@"SELECT setval('""Friendships_Id_seq""', COALESCE((SELECT MAX(""Id"") FROM ""Friendships""), 0) + 1, false);");
            migrationBuilder.Sql(@"ALTER SEQUENCE ""Friendships_Id_seq"" OWNED BY ""Friendships"".""Id"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
