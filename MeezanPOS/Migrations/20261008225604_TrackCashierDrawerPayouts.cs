using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class TrackCashierDrawerPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyJournalId",
                table: "OwnerDebtSettlements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DrawerPayouts",
                table: "DailyJournals",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerDebtSettlements_DailyJournalId",
                table: "OwnerDebtSettlements",
                column: "DailyJournalId");

            migrationBuilder.AddForeignKey(
                name: "FK_OwnerDebtSettlements_DailyJournals_DailyJournalId",
                table: "OwnerDebtSettlements",
                column: "DailyJournalId",
                principalTable: "DailyJournals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OwnerDebtSettlements_DailyJournals_DailyJournalId",
                table: "OwnerDebtSettlements");

            migrationBuilder.DropIndex(
                name: "IX_OwnerDebtSettlements_DailyJournalId",
                table: "OwnerDebtSettlements");

            migrationBuilder.DropColumn(
                name: "DailyJournalId",
                table: "OwnerDebtSettlements");

            migrationBuilder.DropColumn(
                name: "DrawerPayouts",
                table: "DailyJournals");
        }
    }
}
