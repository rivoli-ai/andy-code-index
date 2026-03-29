using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileFilterAndTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileFilterOverrides",
                table: "Repositories",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileFilterOverrides",
                table: "Repositories");
        }
    }
}
