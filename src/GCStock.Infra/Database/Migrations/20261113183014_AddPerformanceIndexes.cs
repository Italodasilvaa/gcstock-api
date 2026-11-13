using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_CompanyId_Timestamp",
                table: "StockMovements",
                columns: new[] { "CompanyId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_CompanyId_ProductId",
                table: "StockBatches",
                columns: new[] { "CompanyId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRecords_CompanyId_ReceiptNumber",
                table: "SaleRecords",
                columns: new[] { "CompanyId", "ReceiptNumber" },
                unique: true,
                filter: "\"ReceiptNumber\" LIKE 'VEN-%'");

            migrationBuilder.CreateIndex(
                name: "IX_SaleRecords_CompanyId_Timestamp",
                table: "SaleRecords",
                columns: new[] { "CompanyId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseEntries_CompanyId_EntryDate",
                table: "PurchaseEntries",
                columns: new[] { "CompanyId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompanyId_Sku",
                table: "Products",
                columns: new[] { "CompanyId", "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_CompanyId_Timestamp",
                table: "AuditLogEntries",
                columns: new[] { "CompanyId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReceivables_CompanyId_Status",
                table: "AccountsReceivables",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountsPayables_CompanyId_Status",
                table: "AccountsPayables",
                columns: new[] { "CompanyId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_CompanyId_Timestamp",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_CompanyId_ProductId",
                table: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_SaleRecords_CompanyId_ReceiptNumber",
                table: "SaleRecords");

            migrationBuilder.DropIndex(
                name: "IX_SaleRecords_CompanyId_Timestamp",
                table: "SaleRecords");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseEntries_CompanyId_EntryDate",
                table: "PurchaseEntries");

            migrationBuilder.DropIndex(
                name: "IX_Products_CompanyId_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogEntries_CompanyId_Timestamp",
                table: "AuditLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_AccountsReceivables_CompanyId_Status",
                table: "AccountsReceivables");

            migrationBuilder.DropIndex(
                name: "IX_AccountsPayables_CompanyId_Status",
                table: "AccountsPayables");
        }
    }
}
