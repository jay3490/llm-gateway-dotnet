using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmGateway.Usage.Migrations
{
    /// <inheritdoc />
    public partial class InitialUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usage_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApiKeyName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModelAlias = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Upstream = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UpstreamModel = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Streamed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Estimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_ApiKeyName_TimestampUtc",
                table: "usage_records",
                columns: new[] { "ApiKeyName", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_records_TimestampUtc",
                table: "usage_records",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_records");
        }
    }
}
