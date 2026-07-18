using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmApi.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceCustomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomFields",
                table: "Deals",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CustomFields",
                table: "Contacts",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "CustomFields",
                table: "Companies",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "FieldDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    FieldType = table.Column<string>(type: "text", nullable: false),
                    Options = table.Column<string>(type: "text", nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldDefinitions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkspaceId = table.Column<string>(type: "text", nullable: false),
                    Terminology = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    EnabledModules = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceSettings_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldDefinitions_WorkspaceId_EntityType_Key",
                table: "FieldDefinitions",
                columns: new[] { "WorkspaceId", "EntityType", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSettings_WorkspaceId",
                table: "WorkspaceSettings",
                column: "WorkspaceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldDefinitions");

            migrationBuilder.DropTable(
                name: "WorkspaceSettings");

            migrationBuilder.DropColumn(
                name: "CustomFields",
                table: "Deals");

            migrationBuilder.DropColumn(
                name: "CustomFields",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "CustomFields",
                table: "Companies");
        }
    }
}
