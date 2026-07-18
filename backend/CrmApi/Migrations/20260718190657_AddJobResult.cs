using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddJobResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "Jobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Result",
                table: "Jobs");
        }
    }
}
