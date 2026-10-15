using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GCStock.Infra.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Permissions",
                table: "Employees",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "Employees");
        }
    }
}
