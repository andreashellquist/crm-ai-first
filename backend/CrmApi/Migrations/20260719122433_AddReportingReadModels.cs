using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddReportingReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityMetrics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    RefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityMetrics_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ForecastSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    PipelineId = table.Column<string>(type: "text", nullable: false),
                    ForecastCategory = table.Column<string>(type: "text", nullable: false),
                    DealCount = table.Column<int>(type: "integer", nullable: false),
                    DealValueCents = table.Column<int>(type: "integer", nullable: false),
                    WeightedValueCents = table.Column<int>(type: "integer", nullable: false),
                    RefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastSnapshots_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PipelineSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    PipelineId = table.Column<string>(type: "text", nullable: false),
                    StageId = table.Column<string>(type: "text", nullable: false),
                    DealCount = table.Column<int>(type: "integer", nullable: false),
                    DealValueCents = table.Column<int>(type: "integer", nullable: false),
                    WeightedValueCents = table.Column<int>(type: "integer", nullable: false),
                    RefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineSnapshots_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityMetrics_WorkspaceId_Date_Type",
                table: "ActivityMetrics",
                columns: new[] { "WorkspaceId", "Date", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSnapshots_WorkspaceId_PipelineId_ForecastCategory",
                table: "ForecastSnapshots",
                columns: new[] { "WorkspaceId", "PipelineId", "ForecastCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineSnapshots_WorkspaceId_PipelineId_StageId",
                table: "PipelineSnapshots",
                columns: new[] { "WorkspaceId", "PipelineId", "StageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityMetrics");

            migrationBuilder.DropTable(
                name: "ForecastSnapshots");

            migrationBuilder.DropTable(
                name: "PipelineSnapshots");
        }
    }
}
