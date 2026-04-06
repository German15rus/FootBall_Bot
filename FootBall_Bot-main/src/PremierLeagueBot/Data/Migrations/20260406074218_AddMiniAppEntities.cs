using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PremierLeagueBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMiniAppEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LanguageCode",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Teams",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NameRu = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionRu = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionEn = table.Column<string>(type: "TEXT", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Predictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramId = table.Column<long>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    PredictedHomeScore = table.Column<int>(type: "INTEGER", nullable: false),
                    PredictedAwayScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PointsAwarded = table.Column<int>(type: "INTEGER", nullable: true),
                    IsScored = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Predictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Predictions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "MatchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Predictions_Users_TelegramId",
                        column: x => x.TelegramId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAchievements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramId = table.Column<long>(type: "INTEGER", nullable: false),
                    AchievementCode = table.Column<string>(type: "TEXT", nullable: false),
                    EarnedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAchievements_Achievements_AchievementCode",
                        column: x => x.AchievementCode,
                        principalTable: "Achievements",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAchievements_Users_TelegramId",
                        column: x => x.TelegramId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_IsScored",
                table: "Predictions",
                column: "IsScored");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_MatchId",
                table: "Predictions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_TelegramId_MatchId",
                table: "Predictions",
                columns: new[] { "TelegramId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_AchievementCode",
                table: "UserAchievements",
                column: "AchievementCode");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_TelegramId",
                table: "UserAchievements",
                column: "TelegramId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_TelegramId_AchievementCode",
                table: "UserAchievements",
                columns: new[] { "TelegramId", "AchievementCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Predictions");

            migrationBuilder.DropTable(
                name: "UserAchievements");

            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LanguageCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Teams");
        }
    }
}
