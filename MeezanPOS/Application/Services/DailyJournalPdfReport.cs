using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.IO;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Application.Services;

public class DailyJournalPdfReport : IDocument
{
    private readonly DailyJournalViewModel _vm;

    public DailyJournalPdfReport(DailyJournalViewModel vm)
    {
        _vm = vm;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public void Compose(IDocumentContainer container)
    {
        container
            .Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.PageColor("#f4f6f8"); // لون الخلفية العام مثل التطبيق
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial).FontColor(Colors.Black));
                page.ContentFromRightToLeft(); // لدعم العربية

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
                    row.RelativeItem().AlignLeft().Text("منظومة ميزان").FontSize(12).SemiBold().FontColor(Colors.Grey.Medium);
                });
            });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(15).Row(row =>
        {
            row.RelativeItem().AlignCenter().Text("تقرير الحركة اليومية").FontSize(20).SemiBold().FontColor(Colors.Black);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(15);

            // 1. معلومات الوردية
            column.Item().Element(c => DrawCard(c, "معلومات الوردية", content =>
            {
                content.Row(row =>
                {
                    row.RelativeItem().Column(col => { col.Item().Text("التاريخ").SemiBold(); col.Item().Text($"{_vm.JournalDate:yyyy/MM/dd}"); });
                    row.RelativeItem().Column(col => { col.Item().Text("نوع الوردية").SemiBold(); col.Item().Text(DailyJournalViewModel.GetShiftDisplayName(_vm.SelectedShiftType)); });
                    row.RelativeItem().Column(col => { col.Item().Text("اسم الموظف").SemiBold(); col.Item().Text($"{_vm.EmployeeName}"); });
                    row.RelativeItem().Column(col => { col.Item().Text("مبلغ الصرف (الفكة)").SemiBold(); col.Item().Text($"{_vm.CashFloat ?? 0:N2}"); });
                });
            }));

            // 2. المبيعات
            column.Item().Element(c => DrawCard(c, "المبيعات", content =>
            {
                content.Column(innerCol => 
                {
                    innerCol.Item().Row(row =>
                    {
                        row.RelativeItem().Column(col => { col.Item().Text("مبيعات نقدية (كاش)").SemiBold(); col.Item().Text($"{_vm.CashSalesInput ?? 0:N2}"); });
                        row.RelativeItem().Column(col => 
                        { 
                            col.Item().Text("خدمات مصرفية (شبكة / بطاقات)").SemiBold(); 
                            col.Item().Text($"{_vm.BankingSalesInput ?? 0:N2}"); 
                            
                            var activeBankSales = _vm.BankSalesInputs.Where(x => (x.Amount ?? 0) > 0).ToList();
                            if (activeBankSales.Any())
                            {
                                col.Item().PaddingTop(4).Column(banksCol =>
                                {
                                    foreach (var bs in activeBankSales)
                                    {
                                        banksCol.Item().Text($"- {bs.BankFriendlyName}: {bs.Amount ?? 0:N2} د.ل").FontSize(9).FontColor(Colors.Grey.Darken2);
                                    }
                                });
                            }
                        });
                    });
                    innerCol.Item().PaddingTop(10).Background("#ecfdf5").Padding(10).Row(row =>
                    {
                        row.RelativeItem().Text("إجمالي المبيعات:").SemiBold().FontSize(14);
                        row.RelativeItem().AlignRight().Text($"{_vm.TotalSales:N2}").SemiBold().FontSize(14);
                    });
                });
            }));

            // 3. الخدمات المصرفية
            column.Item().Element(c => DrawCard(c, "تفاصيل الخدمات المصرفية (مطابقة مع الكاشير)", content =>
            {
                content.Column(innerCol => 
                {
                    if (_vm.BankingItems.Any())
                    {
                        innerCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(); cols.RelativeColumn(); });
                            table.Header(h => { 
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(4).Text("المبلغ").SemiBold(); 
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(4).Text("المصرف").SemiBold(); 
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#f8fafc").Padding(4).Text("رقم التحويل اخر 4 ارقام").SemiBold(); 
                            });
                            foreach (var item in _vm.BankingItems)
                            {
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text($"{item.Amount:N2}");
                                // حل مشكلة: اسم المصرف لا يظهر قبل الحفظ لأن BankName لا يُعيَّن عند الاختيار من القائمة
                                var bankDisplayName = !string.IsNullOrEmpty(item.BankName)
                                    ? item.BankName
                                    : _vm.ActiveBankAccounts.FirstOrDefault(b => b.Id == item.BankAccountId)?.DisplayName ?? "";
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(bankDisplayName);
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(item.Last4Digits);
                            }
                        });
                    }
                    else
                    {
                        innerCol.Item().Text("لم يتم إدخال تفاصيل الدفتر المصرفي (مطابقة المبيعات الإجمالية فقط)").FontSize(10).Italic().FontColor(Colors.Grey.Medium);
                    }
                    innerCol.Item().PaddingTop(10).Background("#eff6ff").Padding(10).Column(col =>
                    {
                        col.Item().Row(r => { r.RelativeItem().Text("المصرفية من الكاشير:"); r.RelativeItem().AlignRight().Text($"{_vm.BankingSalesInput ?? 0:N2}"); });
                        col.Item().Row(r => { r.RelativeItem().Text("المصرفية من الدفتر:"); r.RelativeItem().AlignRight().Text($"{_vm.BankingItemsTotal:N2}"); });
                        col.Item().PaddingTop(5).Row(r => { r.RelativeItem().Text("نتيجة المطابقة:").SemiBold(); r.RelativeItem().AlignRight().Text($"{_vm.BankingDifferenceStatus}").SemiBold(); });
                    });
                });
            }));

            // 4. المرتجعات والمجانية (جنباً إلى جنب)
            column.Item().Row(row =>
            {
                row.RelativeItem().Element(c => DrawCard(c, "الطلبات المجانية", content =>
                {
                    content.Column(innerCol => 
                    {
                        if (_vm.FreeOrders.Any())
                        {
                            innerCol.Item().Table(t =>
                            {
                                t.ColumnsDefinition(cols => {
                                    cols.RelativeColumn();   // المبلغ
                                    cols.RelativeColumn(2);  // اسم الشخص ← جديد
                                    cols.RelativeColumn(2);  // ملاحظات/فاتورة
                                });
                                t.Header(h => { 
                                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fefff5").Padding(4).Text("المبلغ").SemiBold(); 
                                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fefff5").Padding(4).Text("اسم الشخص").SemiBold(); 
                                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fefff5").Padding(4).Text("ملاحظات/فاتورة").SemiBold(); 
                                });
                                foreach (var item in _vm.FreeOrders) { 
                                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text($"{item.Amount:N2}"); 
                                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(!string.IsNullOrWhiteSpace(item.PersonName) ? item.PersonName : "—"); 
                                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(item.InvoiceNumber ?? item.Notes); 
                                }
                            });
                        }
                        else { innerCol.Item().Text("لا يوجد طلبات مجانية"); }
                        innerCol.Item().PaddingTop(10).Text($"الإجمالي: {_vm.FreeOrdersAmount:N2}").SemiBold();
                    });
                }));

                row.ConstantItem(15); // مسافة بين الكروت

                row.RelativeItem().Element(c => DrawCard(c, "المرتجعات", content =>
                {
                    content.Column(innerCol => 
                    {
                        if (_vm.Returns.Any())
                        {
                            innerCol.Item().Table(t =>
                            {
                                t.ColumnsDefinition(cols => {
                                    cols.RelativeColumn();   // المبلغ
                                    cols.RelativeColumn(2);  // اسم الشخص ← جديد
                                    cols.RelativeColumn(2);  // ملاحظات/فاتورة
                                });
                                t.Header(h => { 
                                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fff5f5").Padding(4).Text("المبلغ").SemiBold(); 
                                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fff5f5").Padding(4).Text("اسم الشخص").SemiBold(); 
                                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fff5f5").Padding(4).Text("ملاحظات/فاتورة").SemiBold(); 
                                });
                                foreach (var item in _vm.Returns) { 
                                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text($"{item.Amount:N2}"); 
                                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(!string.IsNullOrWhiteSpace(item.PersonName) ? item.PersonName : "—"); 
                                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(item.InvoiceNumber ?? item.Notes); 
                                }
                            });
                        }
                        else { innerCol.Item().Text("لا يوجد مرتجعات"); }
                        innerCol.Item().PaddingTop(10).Text($"الإجمالي: {_vm.ReturnsAmount:N2}").SemiBold();
                    });
                }));
            });

            // 5. المصروفات النثرية
            column.Item().Element(c => DrawCard(c, "المصروفات النثرية", content =>
            {
                content.Column(innerCol => 
                {
                    if (_vm.ExpenseItems.Any())
                    {
                        innerCol.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(); cols.RelativeColumn(2); });
                            table.Header(h => { 
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#fffbeb").Padding(4).Text("المبلغ").SemiBold(); 
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#fffbeb").Padding(4).Text("النوع").SemiBold(); 
                                h.Cell().Border(1).BorderColor(Colors.Black).Background("#fffbeb").Padding(4).Text("البيان / المورد").SemiBold(); 
                            });
                            foreach (var item in _vm.ExpenseItems)
                            {
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text($"{item.Amount:N2}");
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(item.ExpenseType);
                                
                                string details = !string.IsNullOrWhiteSpace(item.SupplierName) ? item.SupplierName : item.Description;
                                table.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(details ?? "");
                            }
                        });
                    }
                    innerCol.Item().PaddingTop(10).Background("#fffbeb").Padding(10).Row(r =>
                    {
                        r.RelativeItem().Text("إجمالي المصروفات:").SemiBold();
                        r.RelativeItem().AlignRight().Text($"{_vm.TotalExpenses:N2}").SemiBold();
                    });
                });
            }));

            // 6. المطابقة والتسليم
            column.Item().Row(row =>
            {
                row.Spacing(15);
                
                // الجانب الأيمن: الاستلام والمطابقة
                row.RelativeItem().Element(c => DrawCard(c, "الاستلام والمطابقة", content =>
                {
                    content.Column(col =>
                    {
                        col.Item().Text("النقد الفعلي المستلم:").SemiBold().FontSize(12);
                        col.Item().PaddingTop(5).Border(1).BorderColor(Colors.Black).Background("#f8fafc").PaddingVertical(10).AlignCenter().Text($"{_vm.ActualCash ?? 0:N2}").FontSize(18).SemiBold();
                        
                        col.Item().PaddingTop(15).Text("نتيجة المطابقة:").SemiBold().FontSize(12);
                        
                        string diffStatus = _vm.DifferenceStatus ?? "";
                        string diffBg = diffStatus.Contains("عجز") ? "#fef2f2" : (diffStatus.Contains("زيادة") ? "#f8fafc" : "#ecfdf5");
                        string diffColor = _vm.DifferenceColor;

                        col.Item().PaddingTop(5).Border(1).BorderColor(Colors.Black).Background(diffBg).PaddingVertical(10).AlignCenter().Text(diffStatus).FontSize(16).SemiBold().FontColor(diffColor);

                        if (!string.IsNullOrWhiteSpace(_vm.Notes))
                        {
                            col.Item().PaddingTop(15).Text("ملاحظات:").SemiBold();
                            col.Item().PaddingTop(5).Text(_vm.Notes);
                        }
                    });
                }));

                // الجانب الأيسر: تفاصيل الحسبة
                row.RelativeItem(1.5f).Element(c => DrawCard(c, "تفاصيل الحسبة", content =>
                {
                    content.Table(table => 
                    {
                        table.ColumnsDefinition(cols => {
                            cols.RelativeColumn(2); // البيان
                            cols.RelativeColumn(1); // القيمة
                        });
                        
                        void AddRow(string label, string val, bool isBold, bool hasTopLine, string? bg = null)
                        {
                            var cell1 = table.Cell().BorderBottom(1).BorderColor("#e2e8f0").PaddingVertical(4).PaddingHorizontal(6);
                            var cell2 = table.Cell().BorderBottom(1).BorderColor("#e2e8f0").PaddingVertical(4).PaddingHorizontal(6);
                            
                            if (bg != null) {
                                cell1 = cell1.Background(bg);
                                cell2 = cell2.Background(bg);
                            }
                            
                            if (hasTopLine) {
                                cell1 = cell1.BorderTop(1).BorderColor(Colors.Black);
                                cell2 = cell2.BorderTop(1).BorderColor(Colors.Black);
                            }
                            
                            var text1 = cell1.Text(label);
                            var text2 = cell2.AlignRight().Text(val);
                            if (isBold) { text1.SemiBold(); text2.SemiBold(); }
                        }

                        AddRow("إجمالي المبيعات (دخل الوردية)", $"{_vm.TotalSales:N2}", true, false);
                        AddRow("(-) المرتجعات", $"{_vm.ReturnsAmount:N2}", false, false);
                        AddRow("(-) الطلبات المجانية", $"{_vm.FreeOrdersAmount:N2}", false, false);
                        
                        AddRow("صافي المبيعات", $"{_vm.NetSales:N2}", true, true, "#f8fafc");
                        AddRow("منها مبيعات نقدية", $"{_vm.CashSalesInput ?? 0:N2}", false, false);
                        AddRow("منها خدمات مصرفية", $"{_vm.EffectiveBankingTotal:N2}", false, false);
                        AddRow("(-) المصروفات النثرية", $"{_vm.TotalExpenses:N2}", false, false);
                        AddRow("(+) مبلغ الصرف (الفكة)", $"{_vm.CashFloat ?? 0:N2}", false, false);

                        table.Cell().BorderTop(1).BorderColor(Colors.Black).Background("#e2e8f0").Padding(8).Text("النقد المتوقع في الدرج").SemiBold().FontSize(14);
                        table.Cell().BorderTop(1).BorderColor(Colors.Black).Background("#e2e8f0").Padding(8).AlignRight().Text($"{_vm.ExpectedCash:N2}").SemiBold().FontSize(14);
                    });
                }));
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
                column.Item().PaddingBottom(10).Text(title).FontSize(16).SemiBold();
                column.Item().Element(content);
            });
    }
}
