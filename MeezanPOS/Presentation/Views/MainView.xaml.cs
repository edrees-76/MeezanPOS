using System.Windows;

namespace MeezanPOS.Presentation.Views;

public partial class MainView : Window
{
    public MainView()
    {
        InitializeComponent();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // تجاوز رسالة التأكيد إذا كان المستخدم يقوم بتسجيل الخروج (تم تغيير النافذة الرئيسية)
        if (System.Windows.Application.Current.MainWindow != this)
        {
            base.OnClosing(e);
            return;
        }

        var result = Dialogs.Show(
            "هل أنت متأكد من الخروج من المنظومة؟\nيرجى التأكد من حفظ كافة البيانات قبل الخروج.",
            "تأكيد الخروج",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No,
            MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);

        if (result == MessageBoxResult.No)
        {
            e.Cancel = true; // إلغاء عملية الخروج
        }
        else
        {
            // بناءً على طلبك: العودة إلى واجهة تسجيل الدخول بدلاً من إغلاق البرنامج بالكامل
            var loginView = new LoginView();
            System.Windows.Application.Current.MainWindow = loginView;
            loginView.Show();

            base.OnClosing(e);
        }
    }
}
