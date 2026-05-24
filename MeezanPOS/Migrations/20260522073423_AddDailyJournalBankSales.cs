using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyJournalBankSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BankAccountId",
                table: "CardPaymentReconciliations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailyJournalBankSales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DailyJournalId = table.Column<int>(type: "INTEGER", nullable: false),
                    BankAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyJournalBankSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyJournalBankSales_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyJournalBankSales_DailyJournals_DailyJournalId",
                        column: x => x.DailyJournalId,
                        principalTable: "DailyJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardPaymentReconciliations_BankAccountId",
                table: "CardPaymentReconciliations",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJournalBankSales_BankAccountId",
                table: "DailyJournalBankSales",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyJournalBankSales_DailyJournalId",
                table: "DailyJournalBankSales",
                column: "DailyJournalId");

            migrationBuilder.AddForeignKey(
                name: "FK_CardPaymentReconciliations_BankAccounts_BankAccountId",
                table: "CardPaymentReconciliations",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CardPaymentReconciliations_BankAccounts_BankAccountId",
                table: "CardPaymentReconciliations");

            migrationBuilder.DropTable(
                name: "DailyJournalBankSales");

            migrationBuilder.DropIndex(
                name: "IX_CardPaymentReconciliations_BankAccountId",
                table: "CardPaymentReconciliations");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "CardPaymentReconciliations");
        }
    }
}
