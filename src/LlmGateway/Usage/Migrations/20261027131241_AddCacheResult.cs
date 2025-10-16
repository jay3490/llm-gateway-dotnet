using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlmGateway.Usage.Migrations
{
    /// <inheritdoc />
    public partial class AddCacheResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CacheResult",
                table: "usage_records",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "none");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheResult",
                table: "usage_records");
        }
    }
}
