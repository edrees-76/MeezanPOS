using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DailyJournals_JournalDate",
                table: "DailyJournals",
                column: "JournalDate");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralExpenses_PaymentDate",
                table: "GeneralExpenses",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierTransactions_TransactionDate",
                table: "SupplierTransactions",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_TransactionDate",
                table: "CashMovements",
                column: "TransactionDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyJournals_JournalDate",
                table: "DailyJournals");

            migrationBuilder.DropIndex(
                name: "IX_GeneralExpenses_PaymentDate",
                table: "GeneralExpenses");

            migrationBuilder.DropIndex(
                name: "IX_SupplierTransactions_TransactionDate",
                table: "SupplierTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_TransactionDate",
                table: "CashMovements");
        }
    }
}
