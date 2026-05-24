using System;
using System.Collections.Generic;
using System.Linq;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MeezanPOS.Application.Services;

public class BankStatementPdfReport
{
    public static void GeneratePdf(
        string filePath,
        BankAccount account,
        decimal totalOpeningBalance,
        decimal totalDeposits,
        decimal totalWithdrawals,
        decimal endingBalance,
        List<BankTransaction> transactions,
        DateTime startDate,
        DateTime endDate)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(1.0f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));
                
                // تفعيل اتجاه اليمين إلى اليسار للصفحة بالكامل
                page.ContentFromRightToLeft();

                page.Header().Element(c => ComposeHeader(c, account, startDate, endDate, endingBalance));
                page.Content().Element(c => ComposeContent(c, totalOpeningBalance, totalDeposits, totalWithdrawals, endingBalance, transactions));
                page.Footer().Element(ComposeFooter);
            });
        })
        .GeneratePdf(filePath);
    }

    private static void ComposeHeader(IContainer container, BankAccount account, DateTime startDate, DateTime endDate, decimal endingBalance)
    {
        container.PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("كشف حساب مصرفي").FontSize(22).Bold().FontColor(Colors.Blue.Darken3);
                column.Item().Text($"الحساب: {account.DisplayName}").FontSize(12).SemiBold().FontColor(Colors.Grey.Darken3);
                
                if (account.AccountNumber != null)
                {
                    column.Item().Text($"رقم الحساب: {account.AccountNumber}").FontSize(10).FontColor(Colors.Grey.Darken2);
                }
                
                if (account.LegalOwnerName != null)
                {
                    column.Item().Text($"صاحب الحساب: {account.LegalOwnerName}").FontSize(10).FontColor(Colors.Grey.Darken2);
                }

                string dateText = $"الفترة: من {startDate:yyyy/MM/dd} إلى {endDate:yyyy/MM/dd}";
                column.Item().PaddingTop(2).Text(dateText).FontSize(9).FontColor(Colors.Grey.Darken1);
            });

            // لوغو أو صندوق ملخص سريع في الترويسة
            row.ConstantItem(140).Background(Colors.Blue.Lighten5)
                .Border(1)
                .BorderColor(Colors.Blue.Lighten2)
                .Padding(8)
                .Column(column =>
                {
                    column.Item().Text("الرصيد النهائي").FontSize(9).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken3);
                    column.Item().Text($"{endingBalance:N2} د.ل").FontSize(13).Bold().AlignCenter().FontColor(Colors.Blue.Darken3);
                    
                    string typeStr = account.AccountType switch
                    {
                        BankAccountType.Commercial => "حساب تجاري للمطعم",
                        BankAccountType.PersonalMixed => "حساب مختلط (شخصي/تجاري)",
                        _ => "حساب مصرفي"
                    };
                    column.Item().PaddingTop(2).Text(typeStr).FontSize(8).SemiBold().AlignCenter().FontColor(Colors.Grey.Darken2);
                });
        });
    }

    private static void ComposeContent(
        IContainer container,
        decimal totalOpeningBalance,
        decimal totalDeposits,
        decimal totalWithdrawals,
        decimal endingBalance,
        List<BankTransaction> transactions)
    {
        container.Column(column =>
        {
            // بطاقات الخلاصة المالية
            column.Item().PaddingBottom(0.5f, Unit.Centimetre).Row(row =>
            {
                row.RelativeItem().Element(c => DrawSummaryCard(c, "رصيد البداية المالي", totalOpeningBalance, Colors.Grey.Darken1, Colors.Black));
                row.RelativeItem().PaddingRight(8).Element(c => DrawSummaryCard(c, "إجمالي الإيداعات (+)", totalDeposits, Colors.Green.Darken2, Colors.Green.Darken3));
                row.RelativeItem().PaddingRight(8).Element(c => DrawSummaryCard(c, "إجمالي السحوبات (-)", totalWithdrawals, Colors.Red.Darken2, Colors.Red.Darken3));
                row.RelativeItem().PaddingRight(8).Element(c => DrawSummaryCard(c, "الرصيد الصافي النهائي", endingBalance, Colors.Blue.Darken2, Colors.Blue.Darken3));
            });

            // جدول الحركات التفصيلي
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(0.4f);  // م (التسلسل)
                    columns.RelativeColumn(1.2f);  // التاريخ والوقت
                    columns.RelativeColumn(1.0f);  // نوع العملية
                    columns.RelativeColumn(1.0f);  // الرقم المرجعي
                    columns.RelativeColumn(2.6f);  // البيان والتفاصيل
                    columns.RelativeColumn(1.0f);  // إيداع (+)
                    columns.RelativeColumn(1.0f);  // سحب (-)
                    columns.RelativeColumn(1.2f);  // الرصيد التراكمي
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderStyle).Text("م").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("التاريخ والوقت").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("نوع العملية").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("المرجع").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("البيان والتفاصيل").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("إيداع (+)").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("سحب (-)").SemiBold();
                    header.Cell().Element(HeaderStyle).Text("الرصيد التراكمي").SemiBold();

                    static IContainer HeaderStyle(IContainer hContainer)
                    {
                        return hContainer.Background(Colors.Blue.Darken3)
                                         .DefaultTextStyle(x => x.SemiBold().FontSize(8.5f).FontColor(Colors.White))
                                         .Padding(5)
                                         .Border(0.5f)
                                         .BorderColor(Colors.Grey.Lighten1)
                                         .AlignCenter();
                    }
                });

                int seq = 1;
                foreach (var tx in transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id))
                {
                    bool isDeposit = tx.Type == BankTransactionType.CardSalesDeposit ||
                                     tx.Type == BankTransactionType.Deposit;

                    table.Cell().Element(CellStyle).Text(seq++.ToString());
                    table.Cell().Element(CellStyle).Text(tx.TransactionDate.ToString("yyyy/MM/dd HH:mm"));
                    table.Cell().Element(CellStyle).Text(GetTransactionTypeDisplayName(tx.Type));
                    table.Cell().Element(CellStyle).Text(tx.ReferenceNumber ?? "-");
                    table.Cell().Element(CellStyle).AlignLeft().Text(tx.DisplayNotes).FontSize(8f);
                    
                    table.Cell().Element(CellStyle).Text(isDeposit ? $"{tx.Amount:N2}" : "").FontColor(Colors.Green.Darken3).SemiBold();
                    table.Cell().Element(CellStyle).Text(!isDeposit ? $"{tx.Amount:N2}" : "").FontColor(Colors.Red.Darken3).SemiBold();
                    
                    table.Cell().Element(CellStyle).Text($"{tx.BalanceAfter:N2}").Bold().FontColor(Colors.Blue.Darken4);

                    static IContainer CellStyle(IContainer cContainer)
                    {
                        return cContainer.Padding(4)
                                         .BorderBottom(0.5f)
                                         .BorderColor(Colors.Grey.Lighten2)
                                         .AlignCenter();
                    }
                }
            });
        });
    }

    private static void DrawSummaryCard(IContainer container, string title, decimal value, string textColor, string valueColor)
    {
        container.Background(Colors.Grey.Lighten5)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten3)
            .Padding(6)
            .Column(col =>
            {
                col.Item().Text(title).FontSize(7.5f).SemiBold().AlignCenter().FontColor(textColor);
                col.Item().PaddingTop(2).Text($"{value:N2} د.ل").FontSize(10.5f).Bold().AlignCenter().FontColor(valueColor);
            });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingTop(8)
            .Row(row =>
            {
                row.RelativeItem().AlignRight().Text(x =>
                {
                    x.Span("صفحة ");
                    x.CurrentPageNumber();
                    x.Span(" من ");
                    x.TotalPages();
                });
                row.RelativeItem().AlignLeft().Text("منظومة ميزان - كشف حساب مصرفي موثق").FontSize(9).SemiBold().FontColor(Colors.Grey.Medium);
            });
    }

    private static string GetTransactionTypeDisplayName(BankTransactionType type)
    {
        return type switch
        {
            BankTransactionType.CardSalesDeposit => "خدمات مصرفية",
            BankTransactionType.Deposit => "إيداع نقدي",
            BankTransactionType.Withdrawal => "سحب نقدي",
            BankTransactionType.InternalTransfer => "تحويل داخلي",
            BankTransactionType.SupplierPayment => "سداد مورد",
            BankTransactionType.ExpensePayment => "مصروف عام",
            BankTransactionType.OwnerDebtSettlement => "تسوية مالك",
            BankTransactionType.ExchangeDifference => "فروقات",
            _ => "حركة أخرى"
        };
    }
}
