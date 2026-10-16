using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCapitalContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CapitalContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapitalContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapitalContributions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapitalContributions_CompanyId_Date",
                table: "CapitalContributions",
                columns: new[] { "CompanyId", "Date" });

            // O que estava lançado como 'investimento' (contas a pagar) passa a ser aporte de capital: entra no caixa como valor positivo.
            migrationBuilder.Sql(@"
                INSERT INTO ""CapitalContributions"" (""Id"", ""CompanyId"", ""Date"", ""Description"", ""Source"", ""Amount"", ""CreatedBy"", ""CreatedAt"")
                SELECT ""Id"", ""CompanyId"", ""IssueDate"", ""Description"", ""Supplier"", ""Amount"", 'migração', now()
                FROM ""AccountsPayables"" WHERE ""Category"" = 'investimento';
                DELETE FROM ""AccountsPayables"" WHERE ""Category"" = 'investimento';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapitalContributions");
        }
    }
}
