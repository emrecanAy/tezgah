using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tezgah.Orders.Migrations
{
    /// <inheritdoc />
    public partial class OrderCashier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "placed_by_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "placed_by_id",
                schema: "orders",
                table: "orders");
        }
    }
}
