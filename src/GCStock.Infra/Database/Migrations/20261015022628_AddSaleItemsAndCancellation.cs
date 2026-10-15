using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleItemsAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "SaleRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "SaleRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "SaleRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardFlag",
                table: "SaleRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "SaleRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SaleRecords",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "SaleRecords",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SaleRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "concluida");

            migrationBuilder.CreateTable(
                name: "SaleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    KitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Qty = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PriceKnown = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleItems_SaleRecords_SaleRecordId",
                        column: x => x.SaleRecordId,
                        principalTable: "SaleRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRecords_SaleId",
                table: "SaleRecords",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_SaleRecordId",
                table: "SaleItems",
                column: "SaleRecordId");

            // Vendas antigas (anteriores ao histórico): o número do cupom e os itens vêm das baixas de estoque
            // registradas no mesmo instante da venda.
            migrationBuilder.Sql(@"
                UPDATE ""SaleRecords"" s
                SET ""ReceiptNumber"" = sub.receipt
                FROM (
                    SELECT DISTINCT ON (s2.""Id"") s2.""Id"" AS id, substring(m.""Title"" from 'NFCe-[0-9]+') AS receipt
                    FROM ""SaleRecords"" s2
                    JOIN ""StockMovements"" m ON m.""CompanyId"" = s2.""CompanyId"" AND m.""Type"" = 'baixa_pdv' AND m.""Timestamp"" = s2.""Timestamp""
                    WHERE substring(m.""Title"" from 'NFCe-[0-9]+') IS NOT NULL
                ) sub
                WHERE s.""Id"" = sub.id AND s.""ReceiptNumber"" = '';");

            migrationBuilder.Sql(@"
                INSERT INTO ""SaleItems"" (""Id"", ""SaleRecordId"", ""ProductId"", ""KitId"", ""Name"", ""Qty"", ""Unit"", ""UnitPrice"", ""Subtotal"", ""PriceKnown"")
                SELECT gen_random_uuid(), s.""Id"", p.""Id"", NULL, split_part(m.""Title"", ': ', 2), -m.""AmountKg"", m.""Unit"", 0, 0, false
                FROM ""SaleRecords"" s
                JOIN ""StockMovements"" m ON m.""CompanyId"" = s.""CompanyId"" AND m.""Type"" = 'baixa_pdv' AND m.""Timestamp"" = s.""Timestamp""
                                          AND m.""Title"" LIKE 'Venda %: %'
                LEFT JOIN LATERAL (
                    SELECT ""Id"" FROM ""Products""
                    WHERE ""CompanyId"" = s.""CompanyId"" AND ""Name"" = split_part(m.""Title"", ': ', 2)
                    LIMIT 1
                ) p ON true
                WHERE NOT EXISTS (SELECT 1 FROM ""SaleItems"" i WHERE i.""SaleRecordId"" = s.""Id"");");

            // Venda de um único item: o preço unitário sai do total da venda.
            migrationBuilder.Sql(@"
                UPDATE ""SaleItems"" i
                SET ""UnitPrice"" = ROUND(s.""Total"" / NULLIF(i.""Qty"", 0), 2), ""Subtotal"" = s.""Total"", ""PriceKnown"" = true
                FROM ""SaleRecords"" s
                WHERE s.""Id"" = i.""SaleRecordId"" AND i.""PriceKnown"" = false
                  AND (SELECT count(*) FROM ""SaleItems"" x WHERE x.""SaleRecordId"" = s.""Id"") = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleItems");

            migrationBuilder.DropIndex(
                name: "IX_SaleRecords_SaleId",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "CardFlag",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "ReceiptNumber",
                table: "SaleRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SaleRecords");
        }
    }
}
