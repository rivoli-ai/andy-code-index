using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderBaseUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingBaseUrl",
                table: "UserSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmBaseUrl",
                table: "UserSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddingBaseUrl",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "LlmBaseUrl",
                table: "UserSettings");
        }
    }
}
