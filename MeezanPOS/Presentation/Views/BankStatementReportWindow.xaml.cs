using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// نافذة عرض تقرير كشف حساب المصرف البنكي بشكل كامل ومريح.
/// تستقبل DataContext من الشاشة الأم (BankingServicesViewModel).
/// </summary>
public partial class BankStatementReportWindow : Window
{
    public BankStatementReportWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// عند النقر المزدوج على سطر في جدول كشف الحساب:
    /// - إذا كان السطر من نوع "خدمات مصرفية" (CardSalesDeposit)، يتم فتح نافذة التفاصيل اليومية للوردية.
    /// - إذا كان من نوع "مصروف عام" (ExpensePayment) أو "سداد مورد" (SupplierPayment)، يتم فتح نافذة تفاصيل العملية الخاصة بها.
    /// </summary>
    private async void StatementDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid dataGrid) return;
        if (dataGrid.SelectedItem is not BankTransaction transaction) return;

        if (transaction.Type == BankTransactionType.CardSalesDeposit)
        {
            if (DataContext is BankingServicesViewModel vm)
            {
                // تحميل التفاصيل من الحركة اليومية
                await vm.LoadTransactionDetailsAsync(transaction);

                // فتح نافذة التفاصيل المنبثقة للوردية
                var detailsDialog = new BankStatementDetailsDialog
                {
                    DataContext = vm,
                    Owner = this
                };
                detailsDialog.ShowDialog();
            }
        }
        else if (!string.IsNullOrEmpty(transaction.SourceType) && transaction.SourceId.HasValue)
        {
            // فتح نافذة تفاصيل العملية المالية المخصصة
            var detailsDialog = new TransactionDetailsViewWindow(transaction.SourceType, transaction.SourceId.Value)
            {
                Owner = this
            };
            detailsDialog.ShowDialog();
        }
    }
}

