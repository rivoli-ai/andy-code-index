using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSettingsChangeLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SettingsChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Field = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsChangeLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SettingsChangeLogs_CreatedAt",
                table: "SettingsChangeLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SettingsChangeLogs_UserId",
                table: "SettingsChangeLogs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettingsChangeLogs");
        }
    }
}
