using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerIdToExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SnapshotDailyWage",
                table: "WorkerAttendances",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "WorkerAttendances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkerId",
                table: "WorkerAttendances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEnd",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodEndDate",
                table: "PostingSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodStartDate",
                table: "PostingSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "RowVersion",
                table: "PostingSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SessionType",
                table: "PostingSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CashMovementId",
                table: "PostingSessionDetails",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransactionAmount",
                table: "PostingSessionDetails",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "OwnerDebts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransferReference",
                table: "OwnerDebts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomExpenseName",
                table: "GeneralExpenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkerId",
                table: "GeneralExpenses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkerId",
                table: "DailyExpenseItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReconciled",
                table: "BankingItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsReversed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeriodUnlockHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostingSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    UnlockSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    UnlockedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    UnlockDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DetailReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsReLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReLockedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodUnlockHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodUnlockHistories_PostingSessions_PostingSessionId",
                        column: x => x.PostingSessionId,
                        principalTable: "PostingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Workers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkerName = table.Column<string>(type: "TEXT", nullable: false),
                    DailyWage = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkerTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkerId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkerName = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    DebitAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreditAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    AttendanceId = table.Column<int>(type: "INTEGER", nullable: true),
                    DailyExpenseItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    GeneralExpenseId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedReason = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedBy = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkerTransactions_DailyExpenseItems_DailyExpenseItemId",
                        column: x => x.DailyExpenseItemId,
                        principalTable: "DailyExpenseItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerTransactions_GeneralExpenses_GeneralExpenseId",
                        column: x => x.GeneralExpenseId,
                        principalTable: "GeneralExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerTransactions_WorkerAttendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "WorkerAttendances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkerTransactions_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerAttendances_WorkerId",
                table: "WorkerAttendances",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralExpenses_WorkerId",
                table: "GeneralExpenses",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyExpenseItems_WorkerId",
                table: "DailyExpenseItems",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_PeriodUnlockHistories_PostingSessionId",
                table: "PeriodUnlockHistories",
                column: "PostingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerTransactions_AttendanceId",
                table: "WorkerTransactions",
                column: "AttendanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerTransactions_DailyExpenseItemId",
                table: "WorkerTransactions",
                column: "DailyExpenseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerTransactions_GeneralExpenseId",
                table: "WorkerTransactions",
                column: "GeneralExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerTransactions_WorkerId_AttendanceId_Type",
                table: "WorkerTransactions",
                columns: new[] { "WorkerId", "AttendanceId", "Type" },
                unique: true,
                filter: "AttendanceId IS NOT NULL AND IsDeleted = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_DailyExpenseItems_Workers_WorkerId",
                table: "DailyExpenseItems",
                column: "WorkerId",
                principalTable: "Workers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GeneralExpenses_Workers_WorkerId",
                table: "GeneralExpenses",
                column: "WorkerId",
                principalTable: "Workers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkerAttendances_Workers_WorkerId",
                table: "WorkerAttendances",
                column: "WorkerId",
                principalTable: "Workers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DailyExpenseItems_Workers_WorkerId",
                table: "DailyExpenseItems");

            migrationBuilder.DropForeignKey(
                name: "FK_GeneralExpenses_Workers_WorkerId",
                table: "GeneralExpenses");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkerAttendances_Workers_WorkerId",
                table: "WorkerAttendances");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "PeriodUnlockHistories");

            migrationBuilder.DropTable(
                name: "WorkerTransactions");

            migrationBuilder.DropTable(
                name: "Workers");

            migrationBuilder.DropIndex(
                name: "IX_WorkerAttendances_WorkerId",
                table: "WorkerAttendances");

            migrationBuilder.DropIndex(
                name: "IX_GeneralExpenses_WorkerId",
                table: "GeneralExpenses");

            migrationBuilder.DropIndex(
                name: "IX_DailyExpenseItems_WorkerId",
                table: "DailyExpenseItems");

            migrationBuilder.DropColumn(
                name: "SnapshotDailyWage",
                table: "WorkerAttendances");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WorkerAttendances");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "WorkerAttendances");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PeriodEndDate",
                table: "PostingSessions");

            migrationBuilder.DropColumn(
                name: "PeriodStartDate",
                table: "PostingSessions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PostingSessions");

            migrationBuilder.DropColumn(
                name: "SessionType",
                table: "PostingSessions");

            migrationBuilder.DropColumn(
                name: "CashMovementId",
                table: "PostingSessionDetails");

            migrationBuilder.DropColumn(
                name: "TransactionAmount",
                table: "PostingSessionDetails");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "OwnerDebts");

            migrationBuilder.DropColumn(
                name: "TransferReference",
                table: "OwnerDebts");

            migrationBuilder.DropColumn(
                name: "CustomExpenseName",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "DailyExpenseItems");

            migrationBuilder.DropColumn(
                name: "IsReconciled",
                table: "BankingItems");
        }
    }
}
