using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddCardAnticipation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnticipationFeePercent",
                table: "PaymentFeeSettings",
                type: "numeric(9,4)",
                nullable: false,
                defaultValue: 2.5m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "AnticipatedAt",
                table: "AccountsReceivables",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AnticipationFee",
                table: "AccountsReceivables",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentFeeSettingId",
                table: "AccountsReceivables",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnticipationFeePercent",
                table: "PaymentFeeSettings");

            migrationBuilder.DropColumn(
                name: "AnticipatedAt",
                table: "AccountsReceivables");

            migrationBuilder.DropColumn(
                name: "AnticipationFee",
                table: "AccountsReceivables");

            migrationBuilder.DropColumn(
                name: "PaymentFeeSettingId",
                table: "AccountsReceivables");
        }
    }
}
