using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.IO;

namespace MeezanPOS.Application.Services;

public class SettlementPdfReport : IDocument
{
    private readonly decimal _payoutAmount;
    private readonly decimal _keepAmount;
    private readonly decimal _balanceBefore;
    private readonly string _notes;
    private readonly string _postedBy;
    private readonly int _affectedCount;
    private readonly decimal _totalSales;
    private readonly decimal _totalExpenses;
    private readonly Guid _sessionGuid;

    public SettlementPdfReport(
        decimal payoutAmount, 
        decimal keepAmount, 
        decimal balanceBefore, 
        string notes, 
        string postedBy, 
        int affectedCount, 
        decimal totalSales, 
        decimal totalExpenses, 
        Guid sessionGuid)
    {
        _payoutAmount = payoutAmount;
        _keepAmount = keepAmount;
        _balanceBefore = balanceBefore;
        _notes = notes;
        _postedBy = postedBy;
        _affectedCount = affectedCount;
        _totalSales = totalSales;
        _totalExpenses = totalExpenses;
        _sessionGuid = sessionGuid;
    }

    public static void GeneratePdf(
        string filePath, 
        decimal payoutAmount, 
        decimal keepAmount, 
        decimal balanceBefore, 
        string notes, 
        string postedBy, 
        int affectedCount, 
        decimal totalSales, 
        decimal totalExpenses, 
        Guid sessionGuid)
    {
        var document = new SettlementPdfReport(payoutAmount, keepAmount, balanceBefore, notes, postedBy, affectedCount, totalSales, totalExpenses, sessionGuid);
        document.GeneratePdf(filePath);
    }

    public void Compose(IDocumentContainer container)
    {
        container
            .Page(page =>
            {
                page.Margin(35);
                page.Size(PageSizes.A4);
                page.PageColor("#f4f6f8");
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial).FontColor(Colors.Black));
                page.ContentFromRightToLeft();

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().Row(row =>
                {
                    row.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("صفحة ");
                        x.CurrentPageNumber();
                        x.Span(" من ");
                        x.TotalPages();
                    });
                    row.RelativeItem().AlignLeft().Text(MeezanPOS.Infrastructure.Data.RestaurantContext.ReportFooter).FontSize(12).SemiBold().FontColor(Colors.Grey.Medium);
                });
            });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(20).Row(row =>
        {
            row.RelativeItem().AlignCenter().Text("إيصال تسوية وتسليم سيولة نقدية للمالك").FontSize(22).SemiBold().FontColor(Colors.Black);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(20);

            // 1. معلومات التسوية العامة
            column.Item().Element(c => DrawCard(c, "معلومات التسوية العامة", content =>
            {
                content.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(3);
                    });
                    
                    void AddDetail(string label, string val)
                    {
                        table.Cell().PaddingVertical(4).Text(label).SemiBold();
                        table.Cell().PaddingVertical(4).Text(val);
                    }

                    AddDetail("تاريخ التسوية", $"{DateTime.Now:yyyy/MM/dd HH:mm}");
                    AddDetail("المنفذ للعملية", _postedBy);
                    AddDetail("رقم جلسة التسوية", _sessionGuid.ToString().Substring(0, 8).ToUpper());
                    AddDetail("ملاحظات", string.IsNullOrEmpty(_notes) ? "لا يوجد" : _notes);
                });
            }));

            // 2. تفاصيل المبالغ والسيولة (كبير)
            column.Item().Element(c => DrawCard(c, "التحليل المالي وتسليم السيولة", content =>
            {
                content.Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(3);
                        cols.RelativeColumn(2);
                    });

                    void AddRow(string label, string val, bool isBold, string? bg = null)
                    {
                        var cell1 = table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(8);
                        var cell2 = table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(8).AlignRight();

                        if (bg != null)
                        {
                            cell1 = cell1.Background(bg);
                            cell2 = cell2.Background(bg);
                        }

                        var t1 = cell1.Text(label);
                        var t2 = cell2.Text(val);

                        if (isBold)
                        {
                            t1.SemiBold().FontSize(12);
                            t2.SemiBold().FontSize(12);
                        }
                    }

                    AddRow("إجمالي السيولة النقدية المتوفرة قبل التسليم (الرصيد الفعلي)", $"{_balanceBefore:N2} د.ل", true, "#f8fafc");
                    AddRow("(-) المبلغ النقدي المسلم للمالك (كاش)", $"{_payoutAmount:N2} د.ل", true, "#fff5f5");
                    AddRow("(=) المبلغ المتبقي بالخزينة لبدء العمل (سيولة تشغيلية)", $"{_keepAmount:N2} د.ل", true, "#ecfdf5");
                });
            }));

            // 3. ملخص العمليات المرحلة بالتسوية
            column.Item().Element(c => DrawCard(c, "ملخص الحركات المرحلة في هذه الدورة", content =>
            {
                content.Row(row =>
                {
                    row.Spacing(10);

                    void AddSummaryBox(string label, string val, string bg)
                    {
                        row.RelativeItem().Background(bg).Border(1).BorderColor("#e2e8f0").Padding(10).Column(col =>
                        {
                            col.Item().AlignCenter().Text(label).FontSize(11).FontColor(Colors.Grey.Darken3);
                            col.Item().PaddingTop(5).AlignCenter().Text(val).FontSize(15).SemiBold();
                        });
                    }

                    AddSummaryBox("عدد الحركات والورديات المقفلة", $"{_affectedCount}", "#eff6ff");
                    AddSummaryBox("إجمالي مبيعات الفترة المرحلة", $"{_totalSales:N2} د.ل", "#f0fdf4");
                    AddSummaryBox("إجمالي مصروفات الفترة المرحلة", $"{_totalExpenses:N2} د.ل", "#fdf2f8");
                });
            }));

            // 4. التواقيع
            column.Item().PaddingTop(40).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignRight().Text("توقيع المسلم (الإدارة / المحاسب):").SemiBold();
                    col.Item().PaddingTop(30).AlignRight().Text("___________________________");
                });

                row.ConstantItem(50); // مسافة

                row.RelativeItem().Column(col =>
                {
                    col.Item().AlignLeft().Text("توقيع المستلم (صاحب المطعم / المالك):").SemiBold();
                    col.Item().PaddingTop(30).AlignLeft().Text("___________________________");
                });
            });
        });
    }

    private void DrawCard(IContainer container, string title, Action<IContainer> content)
    {
        container
            .Background(Colors.White)
            .Border(1)
            .BorderColor("#e2e8f0")
            .Padding(15)
            .Column(column =>
            {
                column.Item().PaddingBottom(10).Text(title).FontSize(15).SemiBold().FontColor(Colors.Black);
                column.Item().Element(content);
            });
    }
}
