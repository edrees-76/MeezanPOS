using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Reports;
using MeezanPOS.Tests.Helpers;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// كل تقرير PDF يُولَّد ببيانات عربية نموذجية دون استثناء وينتج ملف PDF صالحاً غير فارغ.
/// يكشف أعطال التخطيط (مثل تعارض قيود الأحجام في QuestPDF) وصحة الرسوم البيانية.
/// </summary>
public class PdfReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MeezanPdfTests_" + Guid.NewGuid().ToString("N"));

    public PdfReportTests()
    {
        MessageBoxMock.Initialize(); // يمنع فتح قارئ PDF أثناء الاختبار
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string NewPath(string name) => Path.Combine(_dir, name + ".pdf");

    private static void AssertValidPdf(string path)
    {
        File.Exists(path).Should().BeTrue();
        var bytes = File.ReadAllBytes(path);
        bytes.Length.Should().BeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    private static readonly DateTime Day = new(2026, 9, 15);

    [Fact]
    public void BankStatement_Generates()
    {
        var account = new BankAccount { Id = 1, FriendlyName = "مصرف الجمهورية", BankName = "الجمهورية", AccountNumber = "123", OpeningBalance = 1000m };
        var txs = new List<BankTransaction>
        {
            new() { BankAccountId = 1, Type = BankTransactionType.CardSalesDeposit, Amount = 500m, BalanceAfter = 1500m, TransactionDate = Day, Notes = "مبيعات إلكترونية" },
            new() { BankAccountId = 1, Type = BankTransactionType.SupplierPayment, Amount = 200m, BalanceAfter = 1300m, TransactionDate = Day, Notes = "سداد مورد" },
        };
        var path = NewPath("bank");
        BankStatementPdfReport.GeneratePdf(path, account, 1000m, 500m, 200m, 1300m, txs, Day.AddDays(-5), Day);
        AssertValidPdf(path);
    }

    [Fact]
    public void PartnerStatement_Generates()
    {
        var entries = new List<PartnerStatementEntryDto>
        {
            new() { TransactionDate = Day, TransactionType = "دين على المطعم", Description = "تمويل", Amount = 1000m, RunningBalance = 1000m, SequenceNumber = 1 },
            new() { TransactionDate = Day, TransactionType = "تسوية ذمة", Description = "تسوية", Amount = 400m, IsSettlement = true, RunningBalance = 600m, SequenceNumber = 2 },
        };
        var path = NewPath("partner");
        PartnerStatementPdfReport.GeneratePdf(path, "شريك", 0m, 1000m, 400m, 600m, PartnerBalanceDirection.RestaurantOwesPartner, entries, Day.AddDays(-5), Day);
        AssertValidPdf(path);
    }

    [Fact]
    public void SupplierStatement_Generates()
    {
        var rows = new List<UnifiedLedgerRow>
        {
            new() { },
        };
        var path = NewPath("supplier");
        SupplierStatementPdfReport.GeneratePdf(path, "مورد اللحوم", 500m, 2000m, 1500m, 1000m, rows, Day.AddDays(-5), Day);
        AssertValidPdf(path);
    }

    [Fact]
    public void WorkerStatement_Generates()
    {
        var entries = new List<WorkerLedgerEntry>
        {
            new() { Date = Day, Source = "استحقاق حضور", Description = "يومية", AccruedAmount = 50m, BalanceAfter = 50m },
            new() { Date = Day, Source = "سداد نقدي", Description = "دفعة", PaidAmount = 30m, BalanceAfter = 20m },
        };
        var path = NewPath("worker");
        WorkerStatementPdfReport.GeneratePdf(path, "عامل", 50m, 30m, 20m, entries, autoOpen: false);
        AssertValidPdf(path);
    }

    [Fact]
    public void Settlement_Generates()
    {
        var path = NewPath("settlement");
        SettlementPdfReport.GeneratePdf(path, 5000m, 1000m, 6000m, "ملاحظات التسوية", "admin", 12, 30000m, 9000m, Guid.NewGuid());
        AssertValidPdf(path);
    }

    internal static ClosingAccountSummary SampleClosing()
    {
        var s = new ClosingAccountSummary { PeriodText = "من 2026-09-01 إلى 2026-09-30" };
        s.IncomeStatement.TotalSales = 169549m; s.IncomeStatement.CashSales = 120000m; s.IncomeStatement.CardSales = 49549m;
        s.IncomeStatement.DailyExpenses = 30000m; s.IncomeStatement.GeneralExpenses = 12000m;
        s.CashLedger = new CashLedgerReport { OpeningBalance = 5000m, TotalCashIn = 90000m, TotalCashOut = 60000m, ClosingBalance = 35000m };
        s.ExpensesByCategory = new() { new() { CategoryName = "مشتريات", Amount = 20000m, Percentage = 47.6, SourceType = "يومي" }, new() { CategoryName = "إيجار", Amount = 8000m, Percentage = 19, SourceType = "عام" } };
        for (int i = 0; i < 60; i++)
            s.Suppliers.Add(new SupplierReportItem { SupplierName = $"مورد رقم {i + 1}", OpeningBalance = 1000m, TotalPurchases = 500m, TotalPayments = 300m, ClosingBalance = 1200m });
        s.Liabilities.TotalSupplierDebt = 72000m;
        s.Liabilities.WorkerAdvanceDetails.Add(new WorkerAdvanceDetailItem { WorkerName = "أحمد", AdvanceAmount = 150m, LastTransactionDate = Day });
        s.Liabilities.WorkerUnpaidWageDetails.Add(new WorkerUnpaidWageDetailItem { WorkerName = "علي", UnpaidAmount = 300m, LastAccrualDate = Day });
        s.Liabilities.OwnerDebtDetails.Add(new OwnerDebtDetailItem { PartnerName = "الشريك الأول", Amount = 2000m, Type = "مستحق له", TransactionDate = Day });
        s.BankAccounts = new() { new BankAccountReportItem { AccountName = "الحساب الجاري", BankName = "الجمهورية", AccountNumber = "001", OpeningBalance = 10000m, TotalDeposits = 49549m, TotalWithdrawals = 8000m, ClosingBalance = 51549m } };
        return s;
    }

    internal static (DashboardExportData Data, List<DailySalesPoint> Points, List<ExpenseCategory> Categories) SampleDashboard()
    {
        var data = new DashboardExportData
        {
            PeriodText = "سبتمبر 2026", ExportTime = Day, TotalSales = 169549m, CashSales = 120000m, CardSales = 49549m,
            TotalExpenses = 42000m, NetProfit = 127549m, CashBalance = 35000m,
            TotalSalesTrend = 12.5m, TotalSalesTrendDirection = TrendDirection.Up,
            TotalExpensesTrend = 3m, TotalExpensesTrendDirection = TrendDirection.Down,
            Alerts = new() { new DashboardAlert { Type = "Danger", Message = "يوجد عجز في وردية 2026-09-14", Color = "#EF4444" }, new DashboardAlert { Type = "Info", Message = "4 يوميات مسودة بانتظار الترحيل", Color = "blue" } },
            RecentActivities = new() { new RecentActivity { ActivityType = "وردية", Description = "ترحيل وردية يوم كامل", Amount = 5400m, IsIncoming = true, Timestamp = Day, UserName = "admin" } }
        };
        var points = new List<DailySalesPoint>();
        for (int i = 0; i < 30; i++)
            points.Add(new DailySalesPoint { Date = Day.AddDays(i - 29), TotalSales = 4000m + (i % 7) * 600m, TotalExpenses = 1000m + (i % 5) * 200m });
        var categories = new List<ExpenseCategory>
        {
            new() { CategoryName = "مشتريات", Amount = 20000m, Percentage = 47.6, SourceType = "يومي" },
            new() { CategoryName = "غاز", Amount = 6000m, Percentage = 14.3, SourceType = "يومي" },
            new() { CategoryName = "إيجار", Amount = 8000m, Percentage = 19, SourceType = "عام" },
            new() { CategoryName = "كهرباء", Amount = 8000m, Percentage = 19.1, SourceType = "عام" },
        };
        return (data, points, categories);
    }

    [Fact]
    public void ClosingAccount_Generates_BothModes()
    {
        foreach (var comprehensive in new[] { false, true })
        {
            var path = NewPath("closing_" + comprehensive);
            ClosingAccountPdfExporter.GenerateReport(path, SampleClosing(), comprehensive);
            AssertValidPdf(path);
        }
        // بيانات فارغة أيضاً
        var empty = NewPath("closing_empty");
        ClosingAccountPdfExporter.GenerateReport(empty, new ClosingAccountSummary(), true);
        AssertValidPdf(empty);
    }

    [Fact]
    public void Dashboard_Generates()
    {
        var (data, points, categories) = SampleDashboard();
        var path = NewPath("dashboard");
        DashboardPdfExporter.GenerateReport(path, data, points, categories);
        AssertValidPdf(path);

        var single = NewPath("dashboard_single");
        DashboardPdfExporter.GenerateReport(single, new DashboardExportData(), new List<DailySalesPoint> { points[0] }, new List<ExpenseCategory> { categories[0] });
        AssertValidPdf(single);

        var empty = NewPath("dashboard_empty");
        DashboardPdfExporter.GenerateReport(empty, new DashboardExportData(), new List<DailySalesPoint>(), new List<ExpenseCategory>());
        AssertValidPdf(empty);
    }
}
