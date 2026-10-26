using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamGames.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Administration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdminAudits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedTicks = table.Column<long>(type: "INTEGER", nullable: false),
                Action = table.Column<string>(type: "TEXT", nullable: false),
                TargetId = table.Column<string>(type: "TEXT", nullable: false),
                Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                AppliedVersion = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdminAudits", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AdminSessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Verifier = table.Column<string>(type: "TEXT", nullable: false),
                CredentialVersion = table.Column<string>(type: "TEXT", nullable: false),
                ExpiresTicks = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AdminSessions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "PromptTombstones",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PromptTombstones", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AdminAudits_CreatedTicks",
            table: "AdminAudits",
            column: "CreatedTicks");

        migrationBuilder.CreateIndex(
            name: "IX_AdminAudits_SessionId_CommandId",
            table: "AdminAudits",
            columns: new[] { "SessionId", "CommandId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AdminAudits");

        migrationBuilder.DropTable(
            name: "AdminSessions");

        migrationBuilder.DropTable(
            name: "PromptTombstones");
    }
}
