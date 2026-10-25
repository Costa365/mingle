using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamGames.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PromptLibrary : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Prompts",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: true),
                GameId = table.Column<string>(type: "TEXT", nullable: false),
                Category = table.Column<string>(type: "TEXT", nullable: false),
                Tags = table.Column<string>(type: "TEXT", nullable: false),
                Text = table.Column<string>(type: "TEXT", nullable: false),
                OptionA = table.Column<string>(type: "TEXT", nullable: false),
                OptionB = table.Column<string>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                NormalizedKey = table.Column<string>(type: "TEXT", nullable: false),
                SeedFingerprint = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Prompts", x => x.Id);
                table.ForeignKey(
                    name: "FK_Prompts_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Prompts_GameId_NormalizedKey",
            table: "Prompts",
            columns: new[] { "GameId", "NormalizedKey" },
            unique: true,
            filter: "\"RoomId\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Prompts_RoomId_GameId_NormalizedKey",
            table: "Prompts",
            columns: new[] { "RoomId", "GameId", "NormalizedKey" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Prompts");
    }
}
