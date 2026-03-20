using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CloneUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PersonalAccessToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LastIndexedCommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HeadCommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branches_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Commits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    AuthorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AuthorEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsIndexed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commits_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IndexingTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    ChainId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'"),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexingTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexingTasks_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Enrichments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subtype = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StartLine = table.Column<int>(type: "integer", nullable: true),
                    EndLine = table.Column<int>(type: "integer", nullable: true),
                    Language = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "to_tsvector('english', coalesce(\"Content\", ''))", stored: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Enrichments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Enrichments_Commits_CommitId",
                        column: x => x.CommitId,
                        principalTable: "Commits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Enrichments_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Language = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryFiles_Commits_CommitId",
                        column: x => x.CommitId,
                        principalTable: "Commits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkLineRanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrichmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartLine = table.Column<int>(type: "integer", nullable: false),
                    EndLine = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkLineRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChunkLineRanges_Enrichments_EnrichmentId",
                        column: x => x.EnrichmentId,
                        principalTable: "Enrichments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrichmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmbeddingVector = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    IndexType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW() AT TIME ZONE 'UTC'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentEmbeddings_Enrichments_EnrichmentId",
                        column: x => x.EnrichmentId,
                        principalTable: "Enrichments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_RepositoryId_Name",
                table: "Branches",
                columns: new[] { "RepositoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChunkLineRanges_EnrichmentId",
                table: "ChunkLineRanges",
                column: "EnrichmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Commits_CommittedAt",
                table: "Commits",
                column: "CommittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Commits_RepositoryId_Sha",
                table: "Commits",
                columns: new[] { "RepositoryId", "Sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commits_Sha",
                table: "Commits",
                column: "Sha");

            migrationBuilder.CreateIndex(
                name: "IX_ContentEmbeddings_EmbeddingVector",
                table: "ContentEmbeddings",
                column: "EmbeddingVector")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEmbeddings_EnrichmentId",
                table: "ContentEmbeddings",
                column: "EnrichmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentEmbeddings_IndexType",
                table: "ContentEmbeddings",
                column: "IndexType");

            migrationBuilder.CreateIndex(
                name: "IX_Enrichments_CommitId",
                table: "Enrichments",
                column: "CommitId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrichments_FilePath",
                table: "Enrichments",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_Enrichments_Language",
                table: "Enrichments",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_Enrichments_RepositoryId",
                table: "Enrichments",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Enrichments_SearchVector",
                table: "Enrichments",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_Enrichments_Type_Subtype",
                table: "Enrichments",
                columns: new[] { "Type", "Subtype" });

            migrationBuilder.CreateIndex(
                name: "IX_IndexingTasks_ChainId",
                table: "IndexingTasks",
                column: "ChainId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingTasks_RepositoryId",
                table: "IndexingTasks",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingTasks_Status",
                table: "IndexingTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IndexingTasks_Status_Priority_CreatedAt",
                table: "IndexingTasks",
                columns: new[] { "Status", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Name",
                table: "Repositories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Status",
                table: "Repositories",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Url",
                table: "Repositories",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryFiles_CommitId_Path",
                table: "RepositoryFiles",
                columns: new[] { "CommitId", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryFiles_Language",
                table: "RepositoryFiles",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_RepositoryId_Name",
                table: "Tags",
                columns: new[] { "RepositoryId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "ChunkLineRanges");

            migrationBuilder.DropTable(
                name: "ContentEmbeddings");

            migrationBuilder.DropTable(
                name: "IndexingTasks");

            migrationBuilder.DropTable(
                name: "RepositoryFiles");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Enrichments");

            migrationBuilder.DropTable(
                name: "Commits");

            migrationBuilder.DropTable(
                name: "Repositories");
        }
    }
}
