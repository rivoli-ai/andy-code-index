using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SM29_AddHeartbeatAndSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAt",
                table: "IndexingTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Seq",
                table: "IndexingTasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_IndexingTasks_Status_LastHeartbeatAt",
                table: "IndexingTasks",
                columns: new[] { "Status", "LastHeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Commits_RepositoryId_CommittedAt",
                table: "Commits",
                columns: new[] { "RepositoryId", "CommittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndexingTasks_Status_LastHeartbeatAt",
                table: "IndexingTasks");

            migrationBuilder.DropIndex(
                name: "IX_Commits_RepositoryId_CommittedAt",
                table: "Commits");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                table: "IndexingTasks");

            migrationBuilder.DropColumn(
                name: "Seq",
                table: "IndexingTasks");
        }
    }
}
