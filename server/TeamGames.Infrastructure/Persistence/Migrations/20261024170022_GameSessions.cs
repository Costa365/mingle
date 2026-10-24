using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamGames.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class GameSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GameState",
            table: "Rooms",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GameState",
            table: "Rooms");
    }
}
