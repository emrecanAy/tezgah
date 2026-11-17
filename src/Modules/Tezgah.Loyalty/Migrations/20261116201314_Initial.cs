using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tezgah.Loyalty.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "loyalty");

            migrationBuilder.CreateTable(
                name: "programs",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stamps_required = table.Column<int>(type: "integer", nullable: false),
                    reward = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stamp_on_appointment = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cards",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stamps = table.Column<int>(type: "integer", nullable: false),
                    rewards_available = table.Column<int>(type: "integer", nullable: false),
                    rewards_redeemed = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_cards_programs_program_id",
                        column: x => x.program_id,
                        principalSchema: "loyalty",
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "card_entries",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_card_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_card_entries_cards_card_id",
                        column: x => x.card_id,
                        principalSchema: "loyalty",
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_entries_card_id_source",
                schema: "loyalty",
                table: "card_entries",
                columns: new[] { "card_id", "source" },
                unique: true,
                filter: "source IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_entries_tenant_id",
                schema: "loyalty",
                table: "card_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_card_entries_tenant_id_created_at",
                schema: "loyalty",
                table: "card_entries",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cards_program_id_customer_id",
                schema: "loyalty",
                table: "cards",
                columns: new[] { "program_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cards_tenant_id",
                schema: "loyalty",
                table: "cards",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_programs_tenant_id",
                schema: "loyalty",
                table: "programs",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_entries",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "cards",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "programs",
                schema: "loyalty");
        }
    }
}
