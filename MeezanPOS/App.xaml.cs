using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using Serilog;
namespace MeezanPOS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static System.Threading.Mutex? _appMutex;

    public App()
    {
        // أخطاء الخيوط الخلفية والمهام غير المنتظرة: تسجيلها على الأقل بدلاً من ضياعها بصمت
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled non-UI exception (terminating: {Terminating})", e.IsTerminating);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        this.DispatcherUnhandledException += (s, e) => 
        {
            var fullError = e.Exception.Message;
            var inner = e.Exception.InnerException;
            while (inner != null)
            {
                fullError += $"\n---\n{inner.Message}";
                inner = inner.InnerException;
            }

            Log.Error(e.Exception, "Unhandled UI exception: {Error}", fullError);
            try
            {
                var logPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(
                        Infrastructure.Data.AppDbContext.GetDatabasePath())!,
                    "crash_log.txt");
                System.IO.File.AppendAllText(logPath,
                    $"[{DateTime.UtcNow:O}] {fullError}\n{"=".PadRight(80, '=')}\n");
            }
            catch { }

            e.Handled = true;

            // الأخطاء التي تعني أن حالة التطبيق أو قاعدة البيانات لم تعد موثوقة: إغلاق.
            // غير ذلك (خطأ في شاشة أو أمر واحد): عمليات الحفظ تتم داخل معاملات فيُلغى ما لم يكتمل،
            // لذا يكفي إبلاغ المستخدم والاستمرار بدلاً من إغلاق المنظومة وضياع ما في الشاشات الأخرى.
            bool isFatal = e.Exception is OutOfMemoryException or InvalidProgramException or AccessViolationException
                || (e.Exception.GetBaseException() is Microsoft.Data.Sqlite.SqliteException sqlEx
                    && (sqlEx.SqliteErrorCode == 11 /* SQLITE_CORRUPT */ || sqlEx.SqliteErrorCode == 26 /* SQLITE_NOTADB */));

            if (isFatal)
            {
                Dialogs.Show($"خطأ غير متوقع:\n{fullError}\n\nسيتم إغلاق المنظومة لحماية البيانات.",
                    "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error,
                    MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                System.Windows.Application.Current.Shutdown(1);
                return;
            }

            Dialogs.Show($"حدث خطأ غير متوقع ولم تكتمل العملية الأخيرة:\n{fullError}\n\nيمكنك متابعة العمل. تم تسجيل التفاصيل في سجل الأخطاء.",
                "خطأ", MessageBoxButton.OK, MessageBoxImage.Error,
                MessageBoxResult.OK, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // تهيئة نظام التسجيل أولاً
        AppLogger.Initialize();
        Log.Information("بدء تشغيل منظومة ميزان POS");

        // تسجيل محلل الخطوط لـ PDFsharp
        try
        {
            PdfSharp.Fonts.GlobalFontSettings.FontResolver = new MeezanPOS.Infrastructure.Reports.AppFontResolver();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "خطأ أثناء تسجيل AppFontResolver لـ PDFsharp");
        }


        const string mutexName = "MeezanPOS_SingleInstance_Mutex";
        bool createdNew;
        _appMutex = new System.Threading.Mutex(true, mutexName, out createdNew);

        if (!createdNew)
        {
            Dialogs.Show("المنظومة مفتوحة بالفعل وهي قيد التشغيل حالياً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            _appMutex.Dispose();
            _appMutex = null;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // قبول الأرقام العربية في كل حقول الإدخال
        MeezanPOS.Presentation.Behaviors.ArabicDigitsInput.Register();

        // إظهار واجهة الانتظار (Splash Screen) فوراً لمنع تجميد التطبيق
        var splash = new MeezanPOS.Presentation.Views.SplashView();
        splash.Show();

        // تشغيل عمليات الترقية والتهيئة الثقيلة في خيط خلفي لمنع تجميد واجهة المستخدم
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var dbPath = MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath();
                // النسخة المأخوذة في هذا التشغيل تحديداً (قبل الترقية) — هي الوحيدة الآمنة للاستعادة التلقائية
                string? thisRunBackup = null;
                if (System.IO.File.Exists(dbPath))
                {
                    try
                    {
                        var backupDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dbPath)!, "Backups");
                        var backupPath = System.IO.Path.Combine(backupDir, $"Meezan_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.db");
                        // Backup API بدلاً من File.Copy: يتضمن البيانات الموجودة في ملف WAL
                        MeezanPOS.Infrastructure.Data.DatabaseBackupHelper.CreateBackup(dbPath, backupPath);
                        thisRunBackup = backupPath;

                        MeezanPOS.Infrastructure.Data.DatabaseBackupHelper.PruneBackups(backupDir, "Meezan_backup_*.db");
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning(ex, "خطأ أثناء أخذ نسخة احتياطية من قاعدة البيانات");
                    }
                }

                using (var context = new MeezanPOS.Infrastructure.Data.AppDbContext())
                {
                    try
                    {
                        context.Database.Migrate();
                        MeezanPOS.Infrastructure.Data.AppDbContext.MigrateDatabase();
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "فشل ترقية قاعدة البيانات");

                        string restoreMessage;
                        if (thisRunBackup != null)
                        {
                            try
                            {
                                context.Dispose();
                                MeezanPOS.Infrastructure.Data.DatabaseBackupHelper.ReplaceDatabase(thisRunBackup, dbPath);
                                restoreMessage = "\nتم استعادة نسخة قاعدة البيانات المأخوذة قبل الترقية تلقائياً.";
                            }
                            catch (System.Exception restoreEx)
                            {
                                Log.Error(restoreEx, "فشلت الاستعادة التلقائية بعد فشل الترقية");
                                restoreMessage = $"\nفشلت محاولة الاستعادة التلقائية: {restoreEx.Message}";
                            }
                        }
                        else
                        {
                            // لا نسترجع نسخة قديمة من تشغيل سابق تلقائياً حتى لا تضيع بيانات أيام دون علم المستخدم
                            restoreMessage = "\nلم تُستعد أي نسخة تلقائياً. يمكنك استعادة نسخة يدوياً من مجلد Backups.";
                        }

                        Dispatcher.Invoke(() =>
                        {
                            Dialogs.Show(
                                $"خطأ فادح أثناء ترقية قاعدة البيانات:\n{ex.Message}{restoreMessage}\n\nسيتم إغلاق المنظومة لحماية البيانات.",
                                "خطأ ترقية قاعدة البيانات",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            
                            System.Windows.Application.Current.Shutdown();
                        });
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
                        Log.Warning(ex, "خطأ أثناء تهيئة WAL Mode");
                    }

                    // تصحيح قيم RowVersion التالفة أو المخزنة كـ BLOB في SQLite لضمان نجاح الترحيل المالي وتجنب تعارض التزامن
                    try
                    {
                        // كل جدول في محاولة مستقلة: فشل أحدها (مثلاً بسبب مشغلات حماية السجلات المرحلة) لا يمنع البقية
                        foreach (var sql in new[]
                        {
                            "UPDATE DailyJournals SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;",
                            "UPDATE SaleHeaders SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;",
                            "UPDATE GeneralExpenses SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;"
                        })
                        {
                            try
                            {
                                context.Database.ExecuteSqlRaw(sql);
                            }
                            catch (System.Exception ex)
                            {
                                Log.Warning(ex, "خطأ أثناء تصحيح حقول RowVersion: {Sql}", sql);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning(ex, "خطأ أثناء تصحيح حقول RowVersion");
                    }
                }

                // تهيئة حاوية حقن التبعيات
                AppServiceProvider.Initialize();
                Log.Information("تم تهيئة حاوية الخدمات");

                Log.Information("جاري فتح شاشة تسجيل الدخول...");
                // إغلاق شاشة الانتظار وفتح واجهة تسجيل الدخول على خيط واجهة المستخدم الرئيسي
                Dispatcher.Invoke(() =>
                {
                    var loginView = new MeezanPOS.Presentation.Views.LoginView();
                    loginView.Show();
                    ShutdownMode = ShutdownMode.OnLastWindowClose;
                    splash.Close();
                });
                Log.Information("تم فتح شاشة تسجيل الدخول بنجاح.");
            }
            catch (System.Exception ex)
            {
                Log.Fatal(ex, "خطأ غير متوقع أثناء تهيئة التطبيق في الخلفية");
                Dispatcher.Invoke(() =>
                {
                    try { splash.Close(); } catch { }
                    Dialogs.Show(
                        $"حدث خطأ غير متوقع أثناء بدء تشغيل التطبيق:\n{ex.Message}\n\nسيتم إغلاق المنظومة لحماية البيانات.",
                        "خطأ بدء التشغيل",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    System.Windows.Application.Current.Shutdown();
                });
            }
        });

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

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("إيقاف منظومة ميزان POS");
        
        // تشغيل عملية النسخ الاحتياطي التلقائي بشكل متزامن وحظر عملية الإغلاق حتى اكتمال الكتابة
        try
        {
            using (var context = new MeezanPOS.Infrastructure.Data.AppDbContext())
            {
                var freqSetting = context.Settings.FirstOrDefault(s => s.Key == "BackupFrequency");
                var pathSetting = context.Settings.FirstOrDefault(s => s.Key == "PreferredBackupPath");
                
                if (freqSetting != null && pathSetting != null && 
                    Enum.TryParse<BackupFrequency>(freqSetting.Value, out var freq) && freq != BackupFrequency.Disabled &&
                    !string.IsNullOrWhiteSpace(pathSetting.Value) && Directory.Exists(pathSetting.Value))
                {
                    var preferredPath = pathSetting.Value;
                    var lastBackupStr = context.Settings.FirstOrDefault(s => s.Key == "LastAutoBackupDate")?.Value;
                    
                    var lastBackup = DateTime.MinValue;
                    if (!string.IsNullOrEmpty(lastBackupStr))
                    {
                        DateTime.TryParse(lastBackupStr, out lastBackup);
                    }

                    bool shouldBackup = false;
                    if (freq == BackupFrequency.Daily && lastBackup.AddDays(1) <= DateTime.Now)
                    {
                        shouldBackup = true;
                    }
                    else if (freq == BackupFrequency.Weekly && lastBackup.AddDays(7) <= DateTime.Now)
                    {
                        shouldBackup = true;
                    }
                    else if (freq == BackupFrequency.Monthly && lastBackup.AddMonths(1) <= DateTime.Now)
                    {
                        shouldBackup = true;
                    }

                    if (shouldBackup)
                    {
                        Log.Information("Starting automatic backup on exit. Frequency: {Freq}, Last Backup: {Last}", freq, lastBackup);
                        var fileName = $"Meezan_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.db";
                        var targetFile = Path.Combine(preferredPath, fileName);
                        var tempFile = targetFile + ".tmp";

                        if (File.Exists(tempFile))
                        {
                            File.Delete(tempFile);
                        }

                        var dbPath = MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath();
                        using (var source = new SqliteConnection($"Data Source={dbPath}"))
                        using (var destination = new SqliteConnection($"Data Source={tempFile};Pooling=False"))
                        {
                            source.Open();
                            destination.Open();
                            source.BackupDatabase(destination);
                        }

                        // Atomic rename
                        if (File.Exists(targetFile))
                        {
                            File.Delete(targetFile);
                        }
                        File.Move(tempFile, targetFile);

                        // Save last backup date
                        var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var dateSetting = context.Settings.FirstOrDefault(s => s.Key == "LastAutoBackupDate");
                        if (dateSetting == null)
                        {
                            context.Settings.Add(new Setting { Key = "LastAutoBackupDate", Value = nowStr });
                        }
                        else
                        {
                            dateSetting.Value = nowStr;
                        }
                        context.SaveChanges();
                        Log.Information("Automatic backup completed successfully on exit to {Path}", targetFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to run automatic backup on application exit");
        }

        if (_appMutex != null)
        {
            try
            {
                _appMutex.ReleaseMutex();
            }
            catch { }
            _appMutex.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

