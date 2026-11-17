using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tezgah.Loyalty.Migrations
{
    /// <inheritdoc />
    public partial class EarnRulesAndReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_card_entries_card_id_source",
                schema: "loyalty",
                table: "card_entries");

            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "loyalty",
                table: "programs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "earn_rule",
                schema: "loyalty",
                table: "programs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Programs that stamped completed appointments keep doing so. The rest were stamped by hand,
            // which is no longer possible; they now earn a stamp per item sold.
            migrationBuilder.Sql(
                "UPDATE loyalty.programs SET earn_rule = CASE WHEN stamp_on_appointment THEN 'Appointment' ELSE 'Item' END;");

            migrationBuilder.DropColumn(
                name: "stamp_on_appointment",
                schema: "loyalty",
                table: "programs");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "loyalty",
                table: "card_entries",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "quantity",
                schema: "loyalty",
                table: "card_entries",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "voided_sources",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voided_sources", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_card_entries_card_id_kind_source",
                schema: "loyalty",
                table: "card_entries",
                columns: new[] { "card_id", "kind", "source" },
                unique: true,
                filter: "source IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_card_entries_source",
                schema: "loyalty",
                table: "card_entries",
                column: "source",
                filter: "source IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_voided_sources_tenant_id",
                schema: "loyalty",
                table: "voided_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_voided_sources_tenant_id_source",
                schema: "loyalty",
                table: "voided_sources",
                columns: new[] { "tenant_id", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voided_sources",
                schema: "loyalty");

            migrationBuilder.DropIndex(
                name: "ix_card_entries_card_id_kind_source",
                schema: "loyalty",
                table: "card_entries");

            migrationBuilder.DropIndex(
                name: "ix_card_entries_source",
                schema: "loyalty",
                table: "card_entries");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "loyalty",
                table: "programs");

            migrationBuilder.AddColumn<bool>(
                name: "stamp_on_appointment",
                schema: "loyalty",
                table: "programs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE loyalty.programs SET stamp_on_appointment = earn_rule = 'Appointment';");

            migrationBuilder.DropColumn(
                name: "earn_rule",
                schema: "loyalty",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "quantity",
                schema: "loyalty",
                table: "card_entries");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                schema: "loyalty",
                table: "card_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.CreateIndex(
                name: "ix_card_entries_card_id_source",
                schema: "loyalty",
                table: "card_entries",
                columns: new[] { "card_id", "source" },
                unique: true,
                filter: "source IS NOT NULL");
        }
    }
}
