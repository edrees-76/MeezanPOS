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
        this.DispatcherUnhandledException += (s, e) => 
        {
            var fullError = e.Exception.Message;
            var inner = e.Exception.InnerException;
            while (inner != null)
            {
                fullError += $"\n---\n{inner.Message}";
                inner = inner.InnerException;
            }

            Log.Fatal("FATAL: {Error}", fullError);
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

            MessageBox.Show($"خطأ غير متوقع:\n{fullError}\n\nسيتم إغلاق المنظومة لحماية البيانات.",
                "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true;
            System.Windows.Application.Current.Shutdown(1);
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
            MessageBox.Show("المنظومة مفتوحة بالفعل وهي قيد التشغيل حالياً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            _appMutex.Dispose();
            _appMutex = null;
            System.Windows.Application.Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // إظهار واجهة الانتظار (Splash Screen) فوراً لمنع تجميد التطبيق
        var splash = new MeezanPOS.Presentation.Views.SplashView();
        splash.Show();

        // تشغيل عمليات الترقية والتهيئة الثقيلة في خيط خلفي لمنع تجميد واجهة المستخدم
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
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
                        var dbDir = System.IO.Path.GetDirectoryName(MeezanPOS.Infrastructure.Data.AppDbContext.GetDatabasePath())!;
                        var backupDir = System.IO.Path.Combine(dbDir, "Backups");
                        var latestBackup = System.IO.Directory.Exists(backupDir)
                            ? System.IO.Directory.GetFiles(backupDir, "Meezan_backup_*.db")
                                .Select(f => new System.IO.FileInfo(f))
                                .OrderByDescending(f => f.CreationTime)
                                .FirstOrDefault()
                            : null;

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

                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
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
                        context.Database.ExecuteSqlRaw("UPDATE DailyJournals SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;");
                        context.Database.ExecuteSqlRaw("UPDATE SaleHeaders SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;");
                        context.Database.ExecuteSqlRaw("UPDATE GeneralExpenses SET RowVersion = 1 WHERE typeof(RowVersion) = 'blob' OR RowVersion = 0 OR RowVersion IS NULL;");
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
                    MessageBox.Show(
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

