using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountToGeneralExpense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "GeneralExpenses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralExpenses_BankAccountId",
                table: "GeneralExpenses",
                column: "BankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralExpenses_BankAccounts_BankAccountId",
                table: "GeneralExpenses",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GeneralExpenses_BankAccounts_BankAccountId",
                table: "GeneralExpenses");

            migrationBuilder.DropIndex(
                name: "IX_GeneralExpenses_BankAccountId",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "GeneralExpenses");
        }
    }
}
