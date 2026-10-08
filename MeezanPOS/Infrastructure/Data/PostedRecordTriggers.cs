using System.Data.Common;

namespace MeezanPOS.Infrastructure.Data;

/// <summary>
/// مشغّلات (Triggers) تمنع تعديل أو حذف السجلات المرحّلة مالياً على مستوى قاعدة البيانات.
/// </summary>
/// <remarks>
/// تُنشأ بترحيل EF (MoveManualSqlToMigrations). إعادة ضبط المنظومة تحذفها مؤقتاً لتفريغ البيانات
/// ثم تعيد إنشاءها بـ <see cref="Recreate"/> لأن الترحيل لا يُعاد تطبيقه.
/// </remarks>
public static class PostedRecordTriggers
{
    public static readonly string[] Names =
    {
        "trg_PreventUpdatePostedDailyJournal",
        "trg_PreventDeletePostedDailyJournal",
        "trg_PreventUpdatePostedGeneralExpense",
        "trg_PreventDeletePostedGeneralExpense",
        "trg_PreventUpdatePostedSupplierTransaction_Journal",
        "trg_PreventDeletePostedSupplierTransaction_Journal",
        "trg_PreventUpdatePostedSupplierTransaction_Expense",
        "trg_PreventDeletePostedSupplierTransaction_Expense",
    };

    // التحديث مسموح عند الانتقال من/إلى "مرحّل" (الترحيل وفك الترحيل)، وممنوع إذا بقي مرحّلاً
    public static readonly string[] CreateStatements =
    {
        @"CREATE TRIGGER trg_PreventUpdatePostedDailyJournal
          BEFORE UPDATE ON DailyJournals
          FOR EACH ROW
          WHEN OLD.FinancialStatus = 2 AND NEW.FinancialStatus = 2
          BEGIN
              SELECT RAISE(FAIL, 'Cannot update a posted daily journal.');
          END;",

        @"CREATE TRIGGER trg_PreventDeletePostedDailyJournal
          BEFORE DELETE ON DailyJournals
          FOR EACH ROW
          WHEN OLD.FinancialStatus = 2
          BEGIN
              SELECT RAISE(FAIL, 'Cannot delete a posted daily journal.');
          END;",

        @"CREATE TRIGGER trg_PreventUpdatePostedGeneralExpense
          BEFORE UPDATE ON GeneralExpenses
          FOR EACH ROW
          WHEN OLD.FinancialStatus = 2 AND NEW.FinancialStatus = 2
          BEGIN
              SELECT RAISE(FAIL, 'Cannot update a posted general expense.');
          END;",

        @"CREATE TRIGGER trg_PreventDeletePostedGeneralExpense
          BEFORE DELETE ON GeneralExpenses
          FOR EACH ROW
          WHEN OLD.FinancialStatus = 2
          BEGIN
              SELECT RAISE(FAIL, 'Cannot delete a posted general expense.');
          END;",

        @"CREATE TRIGGER trg_PreventUpdatePostedSupplierTransaction_Journal
          BEFORE UPDATE ON SupplierTransactions
          FOR EACH ROW
          WHEN OLD.SourceType = 3 AND EXISTS (
              SELECT 1 FROM DailyJournals j
              JOIN DailyExpenseItems e ON e.DailyJournalId = j.Id
              WHERE e.Id = OLD.SourceId AND j.FinancialStatus = 2
          )
          BEGIN
              SELECT RAISE(FAIL, 'Cannot update a supplier transaction linked to a posted daily journal.');
          END;",

        @"CREATE TRIGGER trg_PreventDeletePostedSupplierTransaction_Journal
          BEFORE DELETE ON SupplierTransactions
          FOR EACH ROW
          WHEN OLD.SourceType = 3 AND EXISTS (
              SELECT 1 FROM DailyJournals j
              JOIN DailyExpenseItems e ON e.DailyJournalId = j.Id
              WHERE e.Id = OLD.SourceId AND j.FinancialStatus = 2
          )
          BEGIN
              SELECT RAISE(FAIL, 'Cannot delete a supplier transaction linked to a posted daily journal.');
          END;",

        @"CREATE TRIGGER trg_PreventUpdatePostedSupplierTransaction_Expense
          BEFORE UPDATE ON SupplierTransactions
          FOR EACH ROW
          WHEN OLD.SourceType = 4 AND EXISTS (
              SELECT 1 FROM GeneralExpenses e
              WHERE e.Id = OLD.SourceId AND e.FinancialStatus = 2
          )
          BEGIN
              SELECT RAISE(FAIL, 'Cannot update a supplier transaction linked to a posted general expense.');
          END;",

        @"CREATE TRIGGER trg_PreventDeletePostedSupplierTransaction_Expense
          BEFORE DELETE ON SupplierTransactions
          FOR EACH ROW
          WHEN OLD.SourceType = 4 AND EXISTS (
              SELECT 1 FROM GeneralExpenses e
              WHERE e.Id = OLD.SourceId AND e.FinancialStatus = 2
          )
          BEGIN
              SELECT RAISE(FAIL, 'Cannot delete a supplier transaction linked to a posted general expense.');
          END;",
    };

    public static string DropStatement(string name) => $"DROP TRIGGER IF EXISTS \"{name}\";";

    /// <summary>حذف المشغّلات وإعادة إنشائها على اتصال مفتوح.</summary>
    public static void Recreate(DbConnection connection)
    {
        foreach (var name in Names)
            Execute(connection, DropStatement(name));
        foreach (var sql in CreateStatements)
            Execute(connection, sql);
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
