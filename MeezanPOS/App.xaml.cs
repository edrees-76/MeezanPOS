using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;
namespace MeezanPOS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // التأكد من تطبيق كل التحديثات على قاعدة البيانات عند بدء التشغيل
        using (var context = new MeezanPOS.Infrastructure.Data.AppDbContext())
        {
            context.Database.Migrate();
        }
        // تفعيل أزرار Enter, Tab, Esc على مستوى المنظومة بالكامل
        EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(Window_PreviewKeyDown));
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var element = e.OriginalSource as UIElement;
        if (element == null) return;

        // 1. تفعيل زر Enter للتنقل مثل Tab
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            // استثناء الأزرار (زر Enter يقوم بالنقر عليها افتراضياً)
            if (element is System.Windows.Controls.Button)
                return;

            // استثناء الحقول النصية متعددة الأسطر (مثل الملاحظات)
            if (element is System.Windows.Controls.TextBox tb && tb.AcceptsReturn)
                return;

            // استثناء الـ DataGrid إذا كان المستخدم يكتب في خلية
            if (element is System.Windows.Controls.DataGridCell || element.GetType().Name.Contains("DataGrid"))
                return;

            element.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            e.Handled = true;
        }
        // 2. تفعيل زر Esc للعودة أو الإغلاق 
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // البحث عن زر الإغلاق أو الإلغاء وتنفيذ الأمر الخاص به
            // في نمط MVVM، يفضل استدعاء الـ CloseCommand إذا كان موجوداً
            if (System.Windows.Application.Current.MainWindow?.DataContext is MeezanPOS.Application.ViewModels.MainViewModel mainVM)
            {
                var currentVM = mainVM.CurrentViewModel;
                if (currentVM != null)
                {
                    // محاولة استدعاء CloseFormCommand من الـ ViewModel الحالي
                    var closeCommandProp = currentVM.GetType().GetProperty("CloseFormCommand");
                    if (closeCommandProp != null)
                    {
                        var command = closeCommandProp.GetValue(currentVM) as System.Windows.Input.ICommand;
                        if (command != null && command.CanExecute(null))
                        {
                            command.Execute(null);
                            e.Handled = true;
                        }
                    }
                }
            }
        }
    }
}

