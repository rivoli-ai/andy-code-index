using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexingRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IndexingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SnippetsAdded = table.Column<int>(type: "integer", nullable: false),
                    SnippetsUpdated = table.Column<int>(type: "integer", nullable: false),
                    SnippetsDeleted = table.Column<int>(type: "integer", nullable: false),
                    SnippetsUnchanged = table.Column<int>(type: "integer", nullable: false),
                    ApiDocsGenerated = table.Column<int>(type: "integer", nullable: false),
                    CommitsScanned = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexingRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexingRuns_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexingRuns_ChainId",
                table: "IndexingRuns",
                column: "ChainId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingRuns_RepositoryId",
                table: "IndexingRuns",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingRuns_StartedAt",
                table: "IndexingRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexingRuns");
        }
    }
}
