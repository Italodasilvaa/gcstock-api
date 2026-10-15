using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleItemLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaleItemLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItemLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleItemLots_SaleItems_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleItemLots_SaleItemId",
                table: "SaleItemLots",
                column: "SaleItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleItemLots");
        }
    }
}
