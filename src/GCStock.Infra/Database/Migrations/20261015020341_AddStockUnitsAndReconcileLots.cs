using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddStockUnitsAndReconcileLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "StockMovements",
                type: "text",
                nullable: false,
                defaultValue: "kg");

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "StockBatches",
                type: "text",
                nullable: false,
                defaultValue: "kg");

            // 1) Lotes herdam a unidade do cadastro do produto (kg, pct, un...).
            migrationBuilder.Sql(@"
                UPDATE ""StockBatches"" b
                SET ""Unit"" = p.""Unit""
                FROM ""Products"" p
                WHERE p.""Id"" = b.""ProductId"";");

            // 2) Movimentações de entrada herdam a unidade do produto da compra.
            migrationBuilder.Sql(@"
                UPDATE ""StockMovements"" m
                SET ""Unit"" = p.""Unit""
                FROM ""PurchaseItems"" pi
                JOIN ""Products"" p ON p.""Id"" = pi.""ProductId""
                WHERE m.""PurchaseEntryId"" = pi.""PurchaseEntryId""
                  AND m.""Title"" = 'Entrada: ' || pi.""Name"";");

            // 3) As vendas nunca baixaram os lotes: reduz os lotes de cada produto até que a soma não passe do estoque
            //    dele, consumindo primeiro os que vencem antes (FEFO).
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT b.""Id"",
                           b.""AvailableKg"",
                           GREATEST(0, SUM(b.""AvailableKg"") OVER (PARTITION BY b.""ProductId"") - GREATEST(0, p.""Stock"")) AS excess,
                           COALESCE(SUM(b.""AvailableKg"") OVER (
                               PARTITION BY b.""ProductId""
                               ORDER BY b.""ExpiryDate"", b.""EntryDate"", b.""Id""
                               ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS prev_sum
                    FROM ""StockBatches"" b
                    JOIN ""Products"" p ON p.""Id"" = b.""ProductId""
                    WHERE b.""AvailableKg"" > 0
                )
                UPDATE ""StockBatches"" b
                SET ""AvailableKg"" = r.""AvailableKg"" - LEAST(r.""AvailableKg"", GREATEST(0, r.excess - r.prev_sum))
                FROM ranked r
                WHERE r.""Id"" = b.""Id"" AND r.excess > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unit",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "StockBatches");
        }
    }
}
