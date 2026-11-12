using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseCancellationAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "PurchaseEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "PurchaseEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "PurchaseEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PurchaseEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PurchaseRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRefunds_PurchaseEntries_PurchaseEntryId",
                        column: x => x.PurchaseEntryId,
                        principalTable: "PurchaseEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseEntries_CompanyId_NfeNumber",
                table: "PurchaseEntries",
                columns: new[] { "CompanyId", "NfeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRefunds_CompanyId",
                table: "PurchaseRefunds",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRefunds_PurchaseEntryId",
                table: "PurchaseRefunds",
                column: "PurchaseEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseRefunds");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseEntries_CompanyId_NfeNumber",
                table: "PurchaseEntries");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "PurchaseEntries");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "PurchaseEntries");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "PurchaseEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PurchaseEntries");
        }
    }
}
