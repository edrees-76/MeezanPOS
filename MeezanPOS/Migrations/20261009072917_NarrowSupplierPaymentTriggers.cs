using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <summary>
    /// مشغّلات حركات الموردين المرتبطة بيومية أو مصروف مرحّل كانت تمنع أي تحديث، ومنه الرصيد المشتق
    /// BalanceAfter، فتفشل إعادة بناء دفتر المورد بعد دفعة بتاريخ أقدم. تُعاد بصيغة تمنع الحقول المالية فقط.
    /// </summary>
    public partial class NarrowSupplierPaymentTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var name in PostedRecordTriggers.Names)
                migrationBuilder.Sql(PostedRecordTriggers.DropStatement(name));
            foreach (var sql in PostedRecordTriggers.CreateStatements)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // الصيغة السابقة كانت تمنع إعادة بناء الدفتر؛ لا رجوع إليها
        }
    }
}
