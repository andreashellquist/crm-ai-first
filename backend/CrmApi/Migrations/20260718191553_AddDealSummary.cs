using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDealSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AiSummarizedAt",
                table: "Deals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiSummary",
                table: "Deals",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiSummarizedAt",
                table: "Deals");

            migrationBuilder.DropColumn(
                name: "AiSummary",
                table: "Deals");
        }
    }
}
