using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogAnalytics.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Alerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    min_level = table.Column<short>(type: "smallint", nullable: false),
                    threshold = table.Column<int>(type: "integer", nullable: false),
                    window = table.Column<TimeSpan>(type: "interval", nullable: false),
                    cooldown = table.Column<TimeSpan>(type: "interval", nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    webhook_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alert_firings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    sample_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    delivered = table.Column<bool>(type: "boolean", nullable: true),
                    delivery_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_firings", x => x.id);
                    table.ForeignKey(
                        name: "fk_alert_firings_alert_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "alert_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_firings_fired_at",
                table: "alert_firings",
                column: "fired_at");

            migrationBuilder.CreateIndex(
                name: "ix_alert_firings_rule_id",
                table: "alert_firings",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_rules_name",
                table: "alert_rules",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_firings");

            migrationBuilder.DropTable(
                name: "alert_rules");
        }
    }
}
