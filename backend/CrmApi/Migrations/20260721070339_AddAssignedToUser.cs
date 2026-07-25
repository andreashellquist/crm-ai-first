using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedToUserId",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedToUserId",
                table: "Deals",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedToUserId",
                table: "Tasks",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Deals_AssignedToUserId",
                table: "Deals",
                column: "AssignedToUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Deals_Users_AssignedToUserId",
                table: "Deals",
                column: "AssignedToUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_AssignedToUserId",
                table: "Tasks",
                column: "AssignedToUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Deals_Users_AssignedToUserId",
                table: "Deals");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Users_AssignedToUserId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_AssignedToUserId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Deals_AssignedToUserId",
                table: "Deals");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Deals");
        }
    }
}
