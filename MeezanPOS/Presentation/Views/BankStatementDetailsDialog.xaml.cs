using System.Windows;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// نافذة حوارية لعرض تفاصيل مبيعات الخدمات المصرفية اليومية مقسمة حسب الورديات والكاشير.
/// تستقبل DataContext من النافذة الأم (BankingServicesViewModel).
/// </summary>
public partial class BankStatementDetailsDialog : Window
{
    public BankStatementDetailsDialog()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
