using System;

namespace MeezanPOS.Domain.Entities;

public enum DashboardPeriod
{
    Today,
    ThisWeek,
    ThisMonth,
    LastMonth,
    Custom
}

public enum TrendDirection
{
    Up,
    Down,
    Flat
}

public class DashboardAlert
{
    public string Type { get; set; } = string.Empty;        // "Warning" | "Danger" | "Info"
    public string Message { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;         // اسم أيقونة MaterialDesign (مثل "AlertCircle")
    public string Color { get; set; } = string.Empty;        // Hex أو اسم Brush
    public bool IsDismissible { get; set; } = true;
}

public class RecentActivity
{
    public string ActivityType { get; set; } = string.Empty; // "وردية" | "مصروف عام" | "دفعة مورد" | "سحب مالك" | "حركة خزينة"
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsIncoming { get; set; }     // true = وارد (أخضر), false = صادر (أحمر)
    public DateTime Timestamp { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public class DailySalesPoint
{
    public DateTime Date { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalExpenses { get; set; }
}

public class ExpenseCategory
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
    public string SourceType { get; set; } = string.Empty;
}

public class RecentSaleItem
{
    public string InvoiceNo { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
}

public class DashboardExportData
{
    public string PeriodText { get; set; } = string.Empty;
    public DateTime ExportTime { get; set; }
    
    // KPI Values
    public decimal TotalSales { get; set; }
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetProfit { get; set; }
    public decimal CashBalance { get; set; }
    
    // KPI Trends
    public decimal TotalSalesTrend { get; set; }
    public TrendDirection TotalSalesTrendDirection { get; set; }
    public decimal CashSalesTrend { get; set; }
    public TrendDirection CashSalesTrendDirection { get; set; }
    public decimal CardSalesTrend { get; set; }
    public TrendDirection CardSalesTrendDirection { get; set; }
    public decimal TotalExpensesTrend { get; set; }
    public TrendDirection TotalExpensesTrendDirection { get; set; }
    public decimal NetProfitTrend { get; set; }
    public TrendDirection NetProfitTrendDirection { get; set; }
    public decimal CashBalanceTrend { get; set; }
    public TrendDirection CashBalanceTrendDirection { get; set; }
    
    // Alerts and Activities
    public System.Collections.Generic.List<DashboardAlert> Alerts { get; set; } = new();
    public System.Collections.Generic.List<RecentActivity> RecentActivities { get; set; } = new();
}
