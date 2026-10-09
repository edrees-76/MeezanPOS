using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <summary>
    /// سجل النشاط (AuditLogs) للقراءة فقط على مستوى قاعدة البيانات: مشغّلان يمنعان تعديل أي سطر أو حذفه.
    /// يُعاد إنشاء كل مشغّلات الحماية من PostedRecordTriggers.
    /// </summary>
    public partial class ProtectActivityLog : Migration
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
            migrationBuilder.Sql(PostedRecordTriggers.DropStatement("trg_PreventUpdateAuditLog"));
            migrationBuilder.Sql(PostedRecordTriggers.DropStatement("trg_PreventDeleteAuditLog"));
        }
    }
}
