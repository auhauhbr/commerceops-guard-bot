using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommerceOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTriageSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_triage_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    RiskScore = table.Column<int>(type: "integer", nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastFindingCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OrderStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TotalValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notified = table.Column<bool>(type: "boolean", nullable: false),
                    LastNotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_triage_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_triage_snapshots_client_applications_ClientApplicatio~",
                        column: x => x.ClientApplicationId,
                        principalTable: "client_applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_triage_snapshots_ClientApplicationId_OrderId",
                table: "order_triage_snapshots",
                columns: new[] { "ClientApplicationId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_triage_snapshots_RefreshedAt",
                table: "order_triage_snapshots",
                column: "RefreshedAt");

            migrationBuilder.CreateIndex(
                name: "IX_order_triage_snapshots_RiskLevel",
                table: "order_triage_snapshots",
                column: "RiskLevel");

            migrationBuilder.CreateIndex(
                name: "IX_order_triage_snapshots_RiskScore",
                table: "order_triage_snapshots",
                column: "RiskScore",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_triage_snapshots");
        }
    }
}
