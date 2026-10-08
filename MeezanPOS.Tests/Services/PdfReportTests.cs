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
/// يكشف أعطال التخطيط (مثل تعارض قيود الأحجام في QuestPDF) وأعطال الخطوط في PdfSharp.
/// </summary>
public class PdfReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MeezanPdfTests_" + Guid.NewGuid().ToString("N"));

    public PdfReportTests()
    {
        MessageBoxMock.Initialize(); // يمنع فتح قارئ PDF أثناء الاختبار
        Directory.CreateDirectory(_dir);
        if (PdfSharp.Fonts.GlobalFontSettings.FontResolver == null)
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new AppFontResolver();
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

    [Fact]
    public void ClosingAccount_Generates_BothModes()
    {
        var summary = new ClosingAccountSummary();
        foreach (var comprehensive in new[] { false, true })
        {
            var path = NewPath("closing_" + comprehensive);
            ClosingAccountPdfExporter.GenerateReport(path, summary, comprehensive);
            AssertValidPdf(path);
        }
    }

    [Fact]
    public void Dashboard_Generates()
    {
        var data = new DashboardExportData { PeriodText = "سبتمبر 2026", ExportTime = Day, TotalSales = 1000m, CashSales = 700m, CardSales = 300m, TotalExpenses = 400m, NetProfit = 600m, CashBalance = 2500m };
        var points = new List<DailySalesPoint>();
        var categories = new List<ExpenseCategory>();
        var path = NewPath("dashboard");
        DashboardPdfExporter.GenerateReport(path, data, points, categories);
        AssertValidPdf(path);
    }
}
