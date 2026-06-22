using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Presentation.Views;

public partial class ClosingDetailsDialog : Window
{
    public ClosingDetailsDialog(string category, LiabilitiesSummary liabilities, List<SupplierReportItem> suppliers)
    {
        InitializeComponent();
        ConfigureView(category, liabilities, suppliers);
    }

    private void ConfigureView(string category, LiabilitiesSummary liabilities, List<SupplierReportItem> suppliers)
    {
        txtTitle.Text = category switch
        {
            "Suppliers" => "تفاصيل أرصدة الموردين",
            "Advances" => "تفاصيل سُلف العمال غير المسددة (صافي ما يدين به العمال للمطعم)",
            "UnpaidWages" => "تفاصيل الأجور المستحقة غير المصروفة (ما يدين به المطعم للعمال)",
            "Receivables" => "تفاصيل مستحقات الشركاء (ما لهم)",
            "Obligations" => "تفاصيل التزامات الشركاء (ما عليهم)",
            _ => "تفاصيل الالتزامات والمستحقات"
        };

        gridData.Columns.Clear();

        if (category == "Suppliers")
        {
            gridData.ItemsSource = suppliers;
            AddTextColumn("اسم المورد", "SupplierName", 200);
            AddNumericColumn("رصيد أول المدة", "OpeningBalance");
            AddNumericColumn("إجمالي المشتريات (+)", "TotalPurchases");
            AddNumericColumn("إجمالي المدفوعات (-)", "TotalPayments");
            AddNumericColumn("الرصيد الختامي", "ClosingBalance");
        }
        else if (category == "Advances")
        {
            gridData.ItemsSource = liabilities.WorkerAdvanceDetails;
            AddTextColumn("اسم العامل", "WorkerName", 250);
            AddNumericColumn("صافي مبلغ السلفة", "AdvanceAmount");
            AddDateColumn("تاريخ آخر حركة", "LastTransactionDate");
        }
        else if (category == "UnpaidWages")
        {
            gridData.ItemsSource = liabilities.WorkerUnpaidWageDetails;
            AddTextColumn("اسم العامل", "WorkerName", 250);
            AddNumericColumn("أجور مستحقة غير مصروفة", "UnpaidAmount");
            AddDateColumn("تاريخ آخر استحقاق", "LastAccrualDate");
        }
        else if (category == "Receivables")
        {
            gridData.ItemsSource = liabilities.OwnerDebtDetails.Where(d => d.Type == "مستحق له").ToList();
            AddTextColumn("اسم الشريك/المالك", "PartnerName", 250);
            AddNumericColumn("المستحقات الحالية", "Amount");
            AddDateColumn("تاريخ آخر عملية", "TransactionDate");
        }
        else if (category == "Obligations")
        {
            gridData.ItemsSource = liabilities.OwnerDebtDetails.Where(d => d.Type == "عليه").ToList();
            AddTextColumn("اسم الشريك/المالك", "PartnerName", 250);
            AddNumericColumn("الالتزام المالي", "Amount");
            AddDateColumn("تاريخ آخر عملية", "TransactionDate");
        }
    }

    private void AddTextColumn(string header, string bindingPath, double width = 150)
    {
        gridData.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath),
            Width = new DataGridLength(width),
            ElementStyle = FindResource("RightAlignedTextStyle") as Style
        });
    }

    private void AddNumericColumn(string header, string bindingPath)
    {
        gridData.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath) { StringFormat = "{0:N2} د.ل" },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = FindResource("NumericTextStyle") as Style
        });
    }

    private void AddDateColumn(string header, string bindingPath)
    {
        gridData.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath) { StringFormat = "{0:yyyy-MM-dd}" },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = FindResource("RightAlignedTextStyle") as Style
        });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
