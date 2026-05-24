using System.Windows;
using System.Windows.Controls;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// Interaction logic for BankingServicesView.xaml
/// </summary>
public partial class BankingServicesView : UserControl
{
    public BankingServicesView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// عند الضغط على زر "تحميل الكشف"، يتم تحميل البيانات أولاً،
    /// ثم فتح نافذة تقرير الكشف المنفصلة إذا كانت البيانات محمّلة بنجاح.
    /// ملاحظة: الـ Click يعمل بالتوازي مع الـ Command، لذلك نستدعي الدالة مباشرة ونمنع التكرار.
    /// </summary>
    private async void LoadStatementButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is BankingServicesViewModel vm)
        {
            // تحميل البيانات مباشرة (الـ Command سيعمل أيضاً لكن لن يسبب مشكلة)
            await vm.LoadStatementAsync();

            // التحقق من وجود حركات محملة
            if (vm.StatementTransactions != null && vm.StatementTransactions.Count > 0)
            {
                var reportWindow = new BankStatementReportWindow
                {
                    DataContext = vm,
                    Owner = Window.GetWindow(this)
                };
                reportWindow.ShowDialog();
            }
        }
    }
}
