using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommerceOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PublicId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Secret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TelegramChannel = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "operational_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RawBody = table.Column<string>(type: "text", nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operational_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operational_events_client_applications_ClientApplicationId",
                        column: x => x.ClientApplicationId,
                        principalTable: "client_applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_client_applications_PublicId",
                table: "client_applications",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operational_events_ClientApplicationId",
                table: "operational_events",
                column: "ClientApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_operational_events_EventType",
                table: "operational_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_operational_events_ReceivedAt",
                table: "operational_events",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operational_events");

            migrationBuilder.DropTable(
                name: "client_applications");
        }
    }
}
