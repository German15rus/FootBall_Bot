using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PremierLeagueBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveMatchNotificationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HalftimeNotificationSent",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MatchEventNotifications",
                columns: table => new
                {
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchEventNotifications", x => new { x.MatchId, x.EventKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchEventNotifications_MatchId",
                table: "MatchEventNotifications",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchEventNotifications");

            migrationBuilder.DropColumn(
                name: "HalftimeNotificationSent",
                table: "Matches");
        }
    }
}
