using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamGames.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Installations",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Installations", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Installations");
    }
}
