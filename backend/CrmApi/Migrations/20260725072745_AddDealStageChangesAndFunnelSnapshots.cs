using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDealStageChangesAndFunnelSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DealStageChanges",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    DealId = table.Column<string>(type: "text", nullable: false),
                    PipelineId = table.Column<string>(type: "text", nullable: false),
                    FromStageId = table.Column<string>(type: "text", nullable: true),
                    ToStageId = table.Column<string>(type: "text", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DealStageChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DealStageChanges_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FunnelSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    PipelineId = table.Column<string>(type: "text", nullable: false),
                    StageId = table.Column<string>(type: "text", nullable: false),
                    EntryCount = table.Column<int>(type: "integer", nullable: false),
                    RefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunnelSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FunnelSnapshots_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DealStageChanges_WorkspaceId_DealId",
                table: "DealStageChanges",
                columns: new[] { "WorkspaceId", "DealId" });

            migrationBuilder.CreateIndex(
                name: "IX_DealStageChanges_WorkspaceId_PipelineId_ToStageId",
                table: "DealStageChanges",
                columns: new[] { "WorkspaceId", "PipelineId", "ToStageId" });

            migrationBuilder.CreateIndex(
                name: "IX_FunnelSnapshots_WorkspaceId_PipelineId_StageId",
                table: "FunnelSnapshots",
                columns: new[] { "WorkspaceId", "PipelineId", "StageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DealStageChanges");

            migrationBuilder.DropTable(
                name: "FunnelSnapshots");
        }
    }
}
