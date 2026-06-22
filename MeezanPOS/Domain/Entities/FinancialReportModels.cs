using System;
using System.Collections.Generic;

namespace MeezanPOS.Domain.Entities;

public class IncomeStatementReport
{
    public decimal TotalSales { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal DailyExpenses { get; set; }
    public decimal GeneralExpenses { get; set; }
    public decimal TotalExpenses => DailyExpenses + GeneralExpenses;
    public decimal NetProfit => TotalSales - TotalExpenses;
}

public class ExpenseCategoryReportItem
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
    public string SourceType { get; set; } = string.Empty; // "يومي" or "عام"
}

public class SupplierReportItem
{
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal TotalPurchases { get; set; } // IncreaseDebt
    public decimal TotalPayments { get; set; }  // DecreaseDebt
    public decimal ClosingBalance { get; set; }
}

public class CashLedgerReport
{
    public decimal OpeningBalance { get; set; }
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }
    public decimal ClosingBalance { get; set; }
}

public class LiabilitiesSummary
{
    public decimal TotalSupplierDebt { get; set; }              // مجموع ديون الموردين النشطين
    public decimal TotalWorkerAdvancesOutstanding { get; set; }   // مجموع سلف العمال غير المسددة (عليهم)
    public decimal TotalWorkerWagesUnpaid { get; set; }           // مجموع الأجور المستحقة غير المصروفة (علينا)
    public decimal TotalOwnerReceivables { get; set; }           // مجموع مستحقات المالك/الشركاء (ما لهم)
    public decimal TotalOwnerObligations { get; set; }           // مجموع التزامات المالك/الشركاء (ما عليهم)
    
    public List<WorkerAdvanceDetailItem> WorkerAdvanceDetails { get; set; } = new();
    public List<WorkerUnpaidWageDetailItem> WorkerUnpaidWageDetails { get; set; } = new();
    public List<OwnerDebtDetailItem> OwnerDebtDetails { get; set; } = new();
}

public class WorkerAdvanceDetailItem
{
    public string WorkerName { get; set; } = string.Empty;
    public decimal AdvanceAmount { get; set; }
    public DateTime LastTransactionDate { get; set; }
}

public class WorkerUnpaidWageDetailItem
{
    public string WorkerName { get; set; } = string.Empty;
    public decimal UnpaidAmount { get; set; }
    public DateTime LastAccrualDate { get; set; }
}

public class OwnerDebtDetailItem
{
    public string PartnerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty; // "مستحق له" أو "عليه"
    public DateTime TransactionDate { get; set; }
}

public class ClosingAccountSummary
{
    public string PeriodText { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public IncomeStatementReport IncomeStatement { get; set; } = new();
    public List<ExpenseCategoryReportItem> ExpensesByCategory { get; set; } = new();
    public List<SupplierReportItem> Suppliers { get; set; } = new();
    public CashLedgerReport CashLedger { get; set; } = new();
    public LiabilitiesSummary Liabilities { get; set; } = new();
}
