using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectMD.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScanAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Analysis",
                table: "Scans",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Analysis",
                table: "Scans");
        }
    }
}
