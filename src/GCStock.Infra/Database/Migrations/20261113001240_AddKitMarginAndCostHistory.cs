using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddKitMarginAndCostHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinMarginPercent",
                table: "Kits",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinKitMarginPercent",
                table: "AlertSettings",
                type: "numeric(9,4)",
                nullable: false,
                defaultValue: 30m);

            migrationBuilder.CreateTable(
                name: "KitCostHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    KitId = table.Column<Guid>(type: "uuid", nullable: false),
                    KitName = table.Column<string>(type: "text", nullable: false),
                    PreviousCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    NewCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PreviousMarginPercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    NewMarginPercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    SuggestedPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    TriggerProduct = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitCostHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KitCostHistories_CompanyId_KitId_CreatedAt",
                table: "KitCostHistories",
                columns: new[] { "CompanyId", "KitId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KitCostHistories");

            migrationBuilder.DropColumn(
                name: "MinMarginPercent",
                table: "Kits");

            migrationBuilder.DropColumn(
                name: "MinKitMarginPercent",
                table: "AlertSettings");
        }
    }
}
