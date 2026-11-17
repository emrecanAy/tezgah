using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tezgah.Fleet.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fleet");

            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    brand = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    odometer_km = table.Column<int>(type: "integer", nullable: false),
                    driver = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    inspection_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    insurance_due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fuel_entries",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    filled_on = table.Column<DateOnly>(type: "date", nullable: false),
                    liters = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    odometer_km = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fuel_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_fuel_entries_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_records",
                schema: "fleet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_on = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    odometer_km = table.Column<int>(type: "integer", nullable: false),
                    next_due_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_records_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalSchema: "fleet",
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_entries_tenant_id",
                schema: "fleet",
                table: "fuel_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_entries_vehicle_id",
                schema: "fleet",
                table: "fuel_entries",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_records_tenant_id",
                schema: "fleet",
                table: "maintenance_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_records_vehicle_id",
                schema: "fleet",
                table: "maintenance_records",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id",
                schema: "fleet",
                table: "vehicles",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_tenant_id_plate",
                schema: "fleet",
                table: "vehicles",
                columns: new[] { "tenant_id", "plate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_entries",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "maintenance_records",
                schema: "fleet");

            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "fleet");
        }
    }
}
