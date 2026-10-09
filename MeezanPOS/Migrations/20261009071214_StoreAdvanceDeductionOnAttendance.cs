using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class StoreAdvanceDeductionOnAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceDeducted",
                table: "WorkerAttendances",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            // خصم السلفة القديم كان حركة مدينة (Adjustment = 4) تزيد دين العامل مرتين:
            // يُنقل مبلغه إلى سجل الحضور كبيان، وتُحذف الحركة حذفاً منطقياً فيصح رصيد العامل.
            migrationBuilder.Sql(@"
UPDATE WorkerAttendances
SET AdvanceDeducted = (
    SELECT t.DebitAmount FROM WorkerTransactions t
    WHERE t.AttendanceId = WorkerAttendances.Id AND t.Type = 4 AND t.IsDeleted = 0
      AND t.Notes LIKE '%تسوية سلفة%'
    ORDER BY t.Id DESC LIMIT 1)
WHERE EXISTS (
    SELECT 1 FROM WorkerTransactions t
    WHERE t.AttendanceId = WorkerAttendances.Id AND t.Type = 4 AND t.IsDeleted = 0
      AND t.Notes LIKE '%تسوية سلفة%');");
            migrationBuilder.Sql(@"
UPDATE WorkerTransactions SET IsDeleted = 1
WHERE Type = 4 AND IsDeleted = 0 AND AttendanceId IS NOT NULL AND Notes LIKE '%تسوية سلفة%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvanceDeducted",
                table: "WorkerAttendances");
        }
    }
}
