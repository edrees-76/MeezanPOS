using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <summary>
    /// نقل آخر أوامر SQL اليدوية من بدء التشغيل (AppDbContext.MigrateDatabase سابقاً) إلى ترحيلات EF،
    /// لتصبح ترحيلات EF المصدر الوحيد لتغييرات المخطط:
    /// - مشغّلات حماية السجلات المرحّلة.
    /// - تحويل حركات التحويل الداخلي القديمة (النوع 3) إلى وارد/صادر (لا تأثير إن سبق تحويلها).
    /// الجداول والأعمدة التي كان يضيفها الكود اليدوي موجودة أصلاً في ترحيل AddWorkerIdToExpenses.
    /// </summary>
    public partial class MoveManualSqlToMigrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var name in PostedRecordTriggers.Names)
                migrationBuilder.Sql(PostedRecordTriggers.DropStatement(name));
            foreach (var sql in PostedRecordTriggers.CreateStatements)
                migrationBuilder.Sql(sql);

            migrationBuilder.Sql("UPDATE BankTransactions SET Type = 10 WHERE Type = 3 AND IsDeleted = 0 AND (Notes LIKE 'تحويل من%' OR Notes LIKE '%من %');");
            migrationBuilder.Sql("UPDATE BankTransactions SET Type = 9 WHERE Type = 3 AND IsDeleted = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var name in PostedRecordTriggers.Names)
                migrationBuilder.Sql(PostedRecordTriggers.DropStatement(name));
        }
    }
}
