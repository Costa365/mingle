using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamGames.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RoomsAndGuestSessions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Guests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Verifier = table.Column<string>(type: "TEXT", nullable: false),
                ExpiresTicks = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Guests", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Rooms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false),
                JoinLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                FacilitatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                LastActivityTicks = table.Column<long>(type: "INTEGER", nullable: false),
                ClosedTicks = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Rooms", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Memberships",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                GuestId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                Removed = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Memberships", x => x.Id);
                table.ForeignKey(
                    name: "FK_Memberships_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Receipts",
            columns: table => new
            {
                GuestId = table.Column<Guid>(type: "TEXT", nullable: false),
                CommandId = table.Column<Guid>(type: "TEXT", nullable: false),
                RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                AppliedVersion = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Receipts", x => new { x.GuestId, x.CommandId });
                table.ForeignKey(
                    name: "FK_Receipts_Rooms_RoomId",
                    column: x => x.RoomId,
                    principalTable: "Rooms",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Memberships_RoomId_GuestId",
            table: "Memberships",
            columns: new[] { "RoomId", "GuestId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Memberships_RoomId_NormalizedName",
            table: "Memberships",
            columns: new[] { "RoomId", "NormalizedName" },
            unique: true,
            filter: "\"Removed\" = 0");

        migrationBuilder.CreateIndex(
            name: "IX_Receipts_RoomId",
            table: "Receipts",
            column: "RoomId");

        migrationBuilder.CreateIndex(
            name: "IX_Rooms_Code",
            table: "Rooms",
            column: "Code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Guests");

        migrationBuilder.DropTable(
            name: "Memberships");

        migrationBuilder.DropTable(
            name: "Receipts");

        migrationBuilder.DropTable(
            name: "Rooms");
    }
}
