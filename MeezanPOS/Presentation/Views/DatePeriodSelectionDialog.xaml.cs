using System;
using System.Windows;

namespace MeezanPOS.Presentation.Views;

/// <summary>
/// Interaction logic for DatePeriodSelectionDialog.xaml
/// </summary>
public partial class DatePeriodSelectionDialog : Window
{
    public DateTime SelectedStartDate { get; private set; }
    public DateTime SelectedEndDate { get; private set; }

    public DatePeriodSelectionDialog(DateTime? initialStartDate = null, DateTime? initialEndDate = null)
    {
        InitializeComponent();

        // تهيئة التواريخ بالقيم الممررة أو بالقيم الافتراضية
        DpStartDate.SelectedDate = initialStartDate ?? DateTime.Now.AddDays(-7);
        DpEndDate.SelectedDate = initialEndDate ?? DateTime.Now;
    }

    private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            try
            {
                this.DragMove();
            }
            catch { }
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!DpStartDate.SelectedDate.HasValue || !DpEndDate.SelectedDate.HasValue)
        {
            Dialogs.Show("يرجى تحديد تاريخ البدء وتاريخ الانتهاء أولاً للتمكن من قفل الفترة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var start = DpStartDate.SelectedDate.Value.Date;
        var end = DpEndDate.SelectedDate.Value.Date;

        if (start > end)
        {
            Dialogs.Show("تاريخ البدء لا يمكن أن يكون بعد تاريخ الانتهاء.", "خطأ في التحديد", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedStartDate = start;
        SelectedEndDate = end;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
