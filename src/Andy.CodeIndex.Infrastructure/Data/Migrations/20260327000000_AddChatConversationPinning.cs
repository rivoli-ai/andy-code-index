using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.CodeIndex.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConversationPinning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "ChatConversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PinnedAt",
                table: "ChatConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_UserId_IsPinned",
                table: "ChatConversations",
                columns: new[] { "UserId", "IsPinned" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_UserId_IsPinned",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "PinnedAt",
                table: "ChatConversations");
        }
    }
}
