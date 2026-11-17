using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tezgah.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "products",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    unit = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    stock = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    reorder_level = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_stock_not_negative", "stock >= 0");
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movements_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "inventory",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id",
                schema: "inventory",
                table: "products",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_name",
                schema: "inventory",
                table: "products",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_product_id_created_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "product_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_tenant_id",
                schema: "inventory",
                table: "stock_movements",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "products",
                schema: "inventory");
        }
    }
}
