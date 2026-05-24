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
    public App()
    {
        this.DispatcherUnhandledException += (s, e) => 
        {
            var fullError = e.Exception.Message;
            var inner = e.Exception.InnerException;
            while (inner != null)
            {
                fullError += $"\n---\n{inner.Message}";
                inner = inner.InnerException;
            }
            MessageBox.Show($"خطأ غير متوقع:\n{fullError}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // التأكد من تطبيق كل التحديثات على قاعدة البيانات عند بدء التشغيل
        // استخدام المسار الموحد من AppDbContext لضمان عدم ضياع البيانات
        var dbPath = MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath();
        if (System.IO.File.Exists(dbPath))
        {
            try
            {
                var backupDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dbPath)!, "Backups");
                if (!System.IO.Directory.Exists(backupDir))
                {
                    System.IO.Directory.CreateDirectory(backupDir);
                }
                var backupPath = System.IO.Path.Combine(backupDir, $"Meezan_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.db");
                System.IO.File.Copy(dbPath, backupPath, true);

                // الاحتفاظ بآخر 10 نسخ احتياطية فقط وحذف الأقدم
                var oldBackups = System.IO.Directory.GetFiles(backupDir, "Meezan_backup_*.db")
                    .Select(f => new System.IO.FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(10);
                foreach (var file in oldBackups)
                {
                    file.Delete();
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطأ أثناء أخذ نسخة احتياطية من قاعدة البيانات: {ex.Message}");
            }
        }

        using (var context = new MeezanPOS.Infrastructure.Data.AppDbContext())
        {
            try
            {
                context.Database.Migrate();
            }
            catch (System.Exception ex)
            {
                var backupDir = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Backups");
                var latestBackup = System.IO.Directory.GetFiles(backupDir, "Meezan_backup_*.db")
                    .Select(f => new System.IO.FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                string restoreMessage = "";
                if (latestBackup != null)
                {
                    try
                    {
                        System.IO.File.Copy(latestBackup.FullName, dbPath, true);
                        restoreMessage = "\nتم استعادة آخر نسخة احتياطية سليمة لقاعدة البيانات تلقائياً.";
                    }
                    catch (System.Exception restoreEx)
                    {
                        restoreMessage = $"\nفشلت محاولة الاستعادة التلقائية: {restoreEx.Message}";
                    }
                }

                MessageBox.Show(
                    $"خطأ فادح أثناء ترقية قاعدة البيانات:\n{ex.Message}{restoreMessage}\n\nسيتم إغلاق المنظومة لحماية البيانات.",
                    "خطأ ترقية قاعدة البيانات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                System.Windows.Application.Current.Shutdown();
                return;
            }

            // تسريع أداء قاعدة بيانات SQLite وتفعيل نمط WAL للوصول المتوازي دون إقفال الملف
            try
            {
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                context.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطأ أثناء تهيئة WAL Mode: {ex.Message}");
            }

            // تصحيح قيم RowVersion التالفة أو المخزنة كـ BLOB في SQLite لضمان نجاح الترحيل المالي وتجنب تعارض التزامن
            try
            {
                context.Database.ExecuteSqlRaw("UPDATE DailyJournals SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;");
                context.Database.ExecuteSqlRaw("UPDATE SaleHeaders SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;");
                context.Database.ExecuteSqlRaw("UPDATE GeneralExpenses SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"خطأ أثناء تصحيح حقول RowVersion: {ex.Message}");
            }

            // إضافة حقل المطابقة لجدول العمليات البنكية في الحركة اليومية إن لم يكن موجوداً
            try
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE BankingItems ADD COLUMN IsReconciled INTEGER NOT NULL DEFAULT 0;");
            }
            catch { /* العمود موجود مسبقاً */ }

            // إضافة حقول طريقة الدفع ومرجع التحويل لجدول ديون الشركاء
            try { context.Database.ExecuteSqlRaw("ALTER TABLE OwnerDebts ADD COLUMN PaymentMethod TEXT;"); } catch { }
            try { context.Database.ExecuteSqlRaw("ALTER TABLE OwnerDebts ADD COLUMN TransferReference TEXT;"); } catch { }
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
            // 1. إغلاق نوافذ MaterialDesign المنبثقة إن وجدت
            try 
            {
                if (MaterialDesignThemes.Wpf.DialogHost.IsDialogOpen(null))
                {
                    MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(null, null);
                    e.Handled = true;
                    return;
                }
            }
            catch { }

            // 2. محاولة إيجاد أمر التراجع أو الإغلاق في الواجهة الحالية
            var window = System.Windows.Window.GetWindow(element);
            if (window != null)
            {
                object dataContext = window.DataContext;

                // إذا كنا في النافذة الرئيسية، نستهدف الـ ViewModel المعروض حالياً
                if (dataContext is MeezanPOS.Application.ViewModels.MainViewModel mainVM && mainVM.CurrentViewModel != null)
                {
                    dataContext = mainVM.CurrentViewModel;
                }

                if (dataContext != null)
                {
                    string[] possibleCommands = { "CloseFormCommand", "GoBackCommand", "CancelCommand" };
                    foreach (var cmdName in possibleCommands)
                    {
                        var prop = dataContext.GetType().GetProperty(cmdName);
                        if (prop != null)
                        {
                            if (prop.GetValue(dataContext) is System.Windows.Input.ICommand command && command.CanExecute(null))
                            {
                                command.Execute(null);
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}

