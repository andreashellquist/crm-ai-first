using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AiSuggested = table.Column<bool>(type: "boolean", nullable: false),
                    ContactId = table.Column<string>(type: "text", nullable: true),
                    CompanyId = table.Column<string>(type: "text", nullable: true),
                    DealId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Deals_DealId",
                        column: x => x.DealId,
                        principalTable: "Deals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CompanyId",
                table: "Tasks",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ContactId",
                table: "Tasks",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DealId",
                table: "Tasks",
                column: "DealId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_CompletedAt_DueAt",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "CompletedAt", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_ContactId",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_DealId",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "DealId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
