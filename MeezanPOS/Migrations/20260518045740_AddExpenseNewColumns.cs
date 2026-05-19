using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseNewColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "DailyExpenseItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "DailyExpenseItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "DailyExpenseItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SequenceNumber",
                table: "DailyExpenseItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SupplierName",
                table: "DailyExpenseItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "DailyExpenseItems");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "DailyExpenseItems");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "DailyExpenseItems");

            migrationBuilder.DropColumn(
                name: "SequenceNumber",
                table: "DailyExpenseItems");

            migrationBuilder.DropColumn(
                name: "SupplierName",
                table: "DailyExpenseItems");
        }
    }
}
