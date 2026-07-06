using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GenPosting.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Friends",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friends", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", nullable: false),
                    PlatformUserId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    MediaUrnsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IgPostType = table.Column<string>(type: "TEXT", nullable: true),
                    FbPostType = table.Column<string>(type: "TEXT", nullable: true),
                    FbTarget = table.Column<string>(type: "TEXT", nullable: true),
                    FbTargetId = table.Column<string>(type: "TEXT", nullable: true),
                    CommentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduledTime = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPosts_ScheduledTime",
                table: "ScheduledPosts",
                column: "ScheduledTime");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledPosts_Status",
                table: "ScheduledPosts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Friends");

            migrationBuilder.DropTable(
                name: "ScheduledPosts");
        }
    }
}
