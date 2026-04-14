using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PremierLeagueBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionIdToMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompetitionId",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompetitionId",
                table: "Matches");
        }
    }
}
