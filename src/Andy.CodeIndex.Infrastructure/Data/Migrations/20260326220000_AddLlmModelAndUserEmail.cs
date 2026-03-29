using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmModelAndUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LlmModel",
                table: "UserSettings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "SettingsChangeLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LlmModel",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "SettingsChangeLogs");
        }
    }
}
