using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleItemCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CostEstimated",
                table: "SaleItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                table: "SaleItems",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "SaleItems",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Vendas antigas (sem custo guardado): estima pelo custo médio atual do produto (kit sem vínculo fica 0).
            migrationBuilder.Sql(@"
                UPDATE ""SaleItems"" i
                SET ""UnitCost"" = p.""CostPrice"",
                    ""TotalCost"" = ROUND(i.""Qty"" * p.""CostPrice"", 2),
                    ""CostEstimated"" = true
                FROM ""Products"" p
                WHERE p.""Id"" = i.""ProductId"";");

            migrationBuilder.Sql(@"UPDATE ""SaleItems"" SET ""CostEstimated"" = true WHERE ""ProductId"" IS NULL AND ""TotalCost"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostEstimated",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "SaleItems");
        }
    }
}
