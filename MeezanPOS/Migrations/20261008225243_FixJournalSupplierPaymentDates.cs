using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <summary>
    /// تصحيح تواريخ دفعات الموردين المسجلة من اليومية قبل إصلاح التاريخ: كانت تأخذ تاريخ الحفظ
    /// بدل تاريخ اليومية، فتظهر في كشف المورد في يوم غير يوم الدفع الفعلي.
    /// يُضبط تاريخ كل دفعة (المصدر = بند مصروف في يومية) على تاريخ يوميتها حين يختلف اليوم.
    /// مشغّلات حماية المرحّل تمنع تعديل دفعات اليوميات المرحّلة، فتُحذف مؤقتاً ثم يعاد إنشاؤها.
    /// لا يغير الأرصدة: المبالغ كما هي، والتصحيح للتاريخ فقط.
    /// </summary>
    public partial class FixJournalSupplierPaymentDates : Migration
    {
        internal const string FixSql = @"
UPDATE SupplierTransactions
SET TransactionDate = (
    SELECT j.JournalDate FROM DailyExpenseItems e
    JOIN DailyJournals j ON j.Id = e.DailyJournalId
    WHERE e.Id = SupplierTransactions.SourceId)
WHERE SourceType = 3
  AND EXISTS (
    SELECT 1 FROM DailyExpenseItems e
    JOIN DailyJournals j ON j.Id = e.DailyJournalId
    WHERE e.Id = SupplierTransactions.SourceId
      AND date(j.JournalDate) <> date(SupplierTransactions.TransactionDate));";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var name in PostedRecordTriggers.Names)
                migrationBuilder.Sql(PostedRecordTriggers.DropStatement(name));

            migrationBuilder.Sql(FixSql);

            foreach (var sql in PostedRecordTriggers.CreateStatements)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // التواريخ القديمة كانت خاطئة ولا تُحفظ، فلا رجوع لها
        }
    }
}
