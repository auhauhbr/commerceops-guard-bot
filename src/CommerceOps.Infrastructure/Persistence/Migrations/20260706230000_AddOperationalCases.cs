using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommerceOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operational_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProblemType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskScore = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operational_cases_client_applications_ClientApplicationId",
                        column: x => x.ClientApplicationId,
                        principalTable: "client_applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_case_findings_operational_cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "operational_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_case_findings_CaseId",
                table: "case_findings",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_case_findings_Type",
                table: "case_findings",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_operational_cases_CaseNumber",
                table: "operational_cases",
                column: "CaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_cases_ClientApplicationId",
                table: "operational_cases",
                column: "ClientApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_operational_cases_open_lookup",
                table: "operational_cases",
                columns: new[] { "ClientApplicationId", "EntityType", "EntityId", "ProblemType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_operational_cases_CreatedAt",
                table: "operational_cases",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_findings");

            migrationBuilder.DropTable(
                name: "operational_cases");
        }
    }
}
