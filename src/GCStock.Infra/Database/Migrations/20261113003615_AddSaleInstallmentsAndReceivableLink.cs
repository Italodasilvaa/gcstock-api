using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleInstallmentsAndReceivableLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InstallmentCount",
                table: "AccountsReceivables",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentNumber",
                table: "AccountsReceivables",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleId",
                table: "AccountsReceivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountsReceivables_CompanyId_SaleId",
                table: "AccountsReceivables",
                columns: new[] { "CompanyId", "SaleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccountsReceivables_CompanyId_SaleId",
                table: "AccountsReceivables");

            migrationBuilder.DropColumn(
                name: "InstallmentCount",
                table: "AccountsReceivables");

            migrationBuilder.DropColumn(
                name: "InstallmentNumber",
                table: "AccountsReceivables");

            migrationBuilder.DropColumn(
                name: "SaleId",
                table: "AccountsReceivables");
        }
    }
}
