using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Infrastructure.Data;
using Serilog;

namespace MeezanPOS.Application.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ISessionService _sessionService;
    private readonly IAuthenticationService _authService;
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool isResetPasswordDialogOpen;

    [ObservableProperty]
    private string preferredBackupPath = string.Empty;

    [ObservableProperty]
    private BackupFrequency selectedFrequency = BackupFrequency.Disabled;

    [ObservableProperty]
    private string lastAutoBackupDateText = "لا يوجد";

    [ObservableProperty]
    private int selectedTab = 0;

    // Tab navigation support
    public const string BackupTab = "Backup";
    public const string AboutSystemTab = "AboutSystem";
    public const string AboutDesignerTab = "AboutDesigner";

    public void NavigateToTab(string tabIdentifier)
    {
        SelectedTab = tabIdentifier switch
        {
            "AboutSystem" => 1,
            "AboutDesigner" => 2,
            _ => 0
        };
    }

    public SettingsViewModel() : this(
        AppServiceProvider.Resolve<IDbContextFactory<AppDbContext>>(),
        AppServiceProvider.Resolve<ISessionService>(),
        AppServiceProvider.Resolve<IAuthenticationService>())
    {
    }

    public SettingsViewModel(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ISessionService sessionService,
        IAuthenticationService authService)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            using var context = _dbContextFactory.CreateDbContext();
            
            var pathSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "PreferredBackupPath");
            if (pathSetting != null)
            {
                PreferredBackupPath = pathSetting.Value;
            }

            var freqSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "BackupFrequency");
            if (freqSetting != null && Enum.TryParse<BackupFrequency>(freqSetting.Value, out var freq))
            {
                SelectedFrequency = freq;
            }

            var dateSetting = await context.Settings.FirstOrDefaultAsync(s => s.Key == "LastAutoBackupDate");
            if (dateSetting != null)
            {
                LastAutoBackupDateText = dateSetting.Value;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading Settings in SettingsViewModel");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    partial void OnPreferredBackupPathChanged(string value)
    {
        if (!_isInitializing)
        {
            _ = SaveSettingAsync("PreferredBackupPath", value);
        }
    }

    partial void OnSelectedFrequencyChanged(BackupFrequency value)
    {
        if (!_isInitializing)
        {
            _ = SaveSettingAsync("BackupFrequency", value.ToString());
        }
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        try
        {
            using var context = _dbContextFactory.CreateDbContext();
            var setting = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                context.Settings.Add(new Setting { Key = key, Value = value });
            }
            else
            {
                setting.Value = value;
            }
            await context.SaveChangesAsync();
            Log.Information("Saved setting {Key} = {Value}", key, value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error saving setting {Key} in SettingsViewModel", key);
        }
    }

    private async Task<bool> ExecuteBackupAsync(string targetFilePath)
    {
        var tempFilePath = targetFilePath + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            var dbPath = AppDbContext.GetDatabasePath();
            using (var source = new SqliteConnection($"Data Source={dbPath}"))
            using (var destination = new SqliteConnection($"Data Source={tempFilePath};Pooling=False"))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }

            // Atomic rename
            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }
            File.Move(tempFilePath, targetFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during SQLite database backup to {Path}", targetFilePath);
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
            return false;
        }
    }

    public bool CanManageUsers => _sessionService.HasPermission(Permissions.ManageUsers);
    public bool CanRestore => _sessionService.HasPermission(Permissions.RestoreBackup);
    public bool CanResetSystem => _sessionService.HasPermission(Permissions.SystemReset);

    public string AppVersionText => $"الإصدار {UpdateChecker.CurrentVersion}";

    [RelayCommand]
    private Task CheckForUpdatesAsync() => UpdateChecker.NotifyIfNewerAsync(manual: true);

    [RelayCommand]
    private void OpenUserManagement()
    {
        if (!CanManageUsers) return;
        var window = new Presentation.Views.UserManagementWindow { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "اختر مجلد حفظ النسخ الاحتياطية",
            InitialDirectory = string.IsNullOrEmpty(PreferredBackupPath) ? "" : PreferredBackupPath
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = dialog.FolderName;
            PreferredBackupPath = folder; // Auto-save

            var fileName = $"Meezan_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.db";
            var targetFilePath = Path.Combine(folder, fileName);

            bool success = await ExecuteBackupAsync(targetFilePath);
            if (success)
            {
                Dialogs.Show($"تم إنشاء النسخة الاحتياطية بنجاح في المسار:\n{targetFilePath}", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Dialogs.Show("فشلت عملية النسخ الاحتياطي لقاعدة البيانات. تفاصيل الخطأ متوفرة في سجل السيريلوج.", "خطأ في النسخ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task RestoreNowAsync()
    {
        if (!_sessionService.HasPermission(Permissions.RestoreBackup))
        {
            Dialogs.Show(Permissions.DeniedMessage, "صلاحية غير كافية", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Verify no active shifts (Draft)
            using (var context = _dbContextFactory.CreateDbContext())
            {
                var hasActiveShifts = await context.DailyJournals.AnyAsync(j => j.FinancialStatus == FinancialStatus.Draft);
                if (hasActiveShifts)
                {
                    Dialogs.Show("يوجد ورديات مفتوحة (مسودة) غير مرحلة حالياً. يرجى ترحيلها أو حذفها من قائمة المبيعات اليومية أولاً قبل استعادة نسخة قديمة لتجنب فقدان البيانات.", "تنبيه هام", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "اختر ملف قاعدة البيانات لاستعادته",
                Filter = "SQLite Database (*.db)|*.db",
                DefaultExt = ".db"
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedFile = dialog.FileName;

                var result = Dialogs.Show(
                    "تحذير: استعادة قاعدة البيانات ستؤدي إلى استبدال كافة البيانات الحالية بالبيانات الموجودة في الملف المحدد بشكل نهائي ولا يمكن التراجع عن ذلك.\n\nهل أنت متأكد من رغبتك في الاستمرار؟",
                    "تأكيد استعادة البيانات",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.Yes)
                {
                    // 1. التحقق من أن الملف نسخة سليمة من قاعدة بيانات ميزان
                    try
                    {
                        DatabaseBackupHelper.ValidateMeezanDatabase(selectedFile);
                    }
                    catch (InvalidDataException invalid)
                    {
                        Dialogs.Show(invalid.Message, "ملف غير صالح", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var dbPath = AppDbContext.GetDatabasePath();

                    // 2. نسخة أمان من البيانات الحالية قبل استبدالها (للتراجع عند الخطأ)
                    var safetyBackup = Path.Combine(Path.GetDirectoryName(dbPath)!, "Backups",
                        $"Meezan_before_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                    if (!await ExecuteBackupAsync(safetyBackup))
                    {
                        Dialogs.Show("تعذر أخذ نسخة أمان من البيانات الحالية، لذا أُلغيت الاستعادة.", "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 3. الاستبدال الآمن (يفرغ الاتصالات ويحذف ملفات WAL القديمة)
                    await Task.Run(() => DatabaseBackupHelper.ReplaceDatabase(selectedFile, dbPath));
                    Log.Warning("Database restored from {Source}; previous data saved to {Safety}", selectedFile, safetyBackup);

                    Dialogs.Show($"تم استعادة قاعدة البيانات بنجاح!\n\nتم حفظ نسخة من البيانات السابقة في:\n{safetyBackup}\n\nسيتم إغلاق التطبيق الآن، يرجى إعادة تشغيله يدوياً لتطبيق البيانات المسترجعة.", "نجاح الاستعادة", MessageBoxButton.OK, MessageBoxImage.Information);
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error restoring database");
            Dialogs.Show($"فشلت عملية استعادة قاعدة البيانات:\n{ex.Message}", "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetSystem()
    {
        if (!_sessionService.HasPermission(Permissions.SystemReset))
        {
            Dialogs.Show(Permissions.DeniedMessage, "صلاحية غير كافية", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = Dialogs.Show(
            "تحذير حرج للغاية: إعادة ضبط المنظومة ستؤدي إلى حذف جميع البيانات والعمليات المالية والتقارير والنسخ الاحتياطي نهائياً، ولا يمكن التراجع عن ذلك.\n\nسيتم الإبقاء فقط على حسابات المستخدمين.\n\nهل أنت متأكد من رغبتك في الاستمرار؟",
            "تأكيد إعادة الضبط النهائي",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error,
            MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            IsResetPasswordDialogOpen = true;
        }
    }

    [RelayCommand]
    private void CloseResetDialog()
    {
        IsResetPasswordDialogOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmResetAsync(object parameter)
    {
        if (!_sessionService.HasPermission(Permissions.SystemReset))
        {
            Dialogs.Show(Permissions.DeniedMessage, "صلاحية غير كافية", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (parameter is System.Windows.Controls.PasswordBox passwordBox)
        {
            string password = passwordBox.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                Dialogs.Show("الرجاء إدخال كلمة مرور الحساب الحالي لتأكيد إعادة الضبط.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sessionService = _sessionService;
            var authService = _authService;
            string currentUsername = sessionService.CurrentUsername ?? "admin";

            var authenticatedUser = await authService.AuthenticateAsync(currentUsername, password);
            if (authenticatedUser == null)
            {
                Dialogs.Show("كلمة المرور غير صحيحة. يرجى إدخال كلمة المرور الصحيحة لحسابك.", "خطأ في المصادقة", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            passwordBox.Clear();
            IsResetPasswordDialogOpen = false;

            try
            {
                var dbPath = AppDbContext.GetDatabasePath();

                // نسخة أمان إلزامية قبل الحذف: إعادة الضبط لا يمكن التراجع عنها إلا من هذه النسخة
                var safetyBackup = Path.Combine(Path.GetDirectoryName(dbPath)!, "Backups",
                    $"Meezan_before_reset_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                if (!await ExecuteBackupAsync(safetyBackup))
                {
                    Dialogs.Show("تعذر أخذ نسخة أمان من البيانات الحالية، لذا أُلغيت إعادة الضبط.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                Log.Warning("System reset requested by {User}; data saved to {Safety}", currentUsername, safetyBackup);

                var tablesToKeep = new System.Collections.Generic.HashSet<string>
                {
                    "Users",
                    "Roles",
                    "__EFMigrationsHistory",
                    "Settings"
                };

                using (var conn = new SqliteConnection($"Data Source={dbPath}"))
                {
                    await conn.OpenAsync();

                    // Turn off foreign keys temporarily
                    using (var pragmaCmd = conn.CreateCommand())
                    {
                        pragmaCmd.CommandText = "PRAGMA foreign_keys = OFF;";
                        await pragmaCmd.ExecuteNonQueryAsync();
                    }

                    // Drop all triggers that block DELETE on posted records
                    var allTriggers = new System.Collections.Generic.List<string>();
                    using (var getTrigCmd = conn.CreateCommand())
                    {
                        getTrigCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger'";
                        using (var trigReader = await getTrigCmd.ExecuteReaderAsync())
                        {
                            while (await trigReader.ReadAsync())
                                allTriggers.Add(trigReader.GetString(0));
                        }
                    }
                    foreach (var trigger in allTriggers)
                    {
                        try
                        {
                            using (var dropCmd = conn.CreateCommand())
                            {
                                dropCmd.CommandText = $"DROP TRIGGER IF EXISTS \"{trigger}\";";
                                await dropCmd.ExecuteNonQueryAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to drop trigger {TriggerName}", trigger);
                        }
                    }

                    // Get all tables
                    var allTables = new System.Collections.Generic.List<string>();
                    using (var getTablesCmd = conn.CreateCommand())
                    {
                        getTablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                        using (var reader = await getTablesCmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                allTables.Add(reader.GetString(0));
                            }
                        }
                    }

                    // Wrap all destructive operations in a transaction for atomicity
                    using var transaction = conn.BeginTransaction();
                    try
                    {
                        // Clear tables
                        foreach (var table in allTables)
                        {
                            if (!tablesToKeep.Contains(table) && !table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                            {
                                using (var deleteCmd = conn.CreateCommand())
                                {
                                    deleteCmd.Transaction = transaction;
                                    deleteCmd.CommandText = $"DELETE FROM \"{table}\";";
                                    await deleteCmd.ExecuteNonQueryAsync();
                                }
                            }
                        }

                        // Reset auto-increment sequences
                        using (var resetSeqCmd = conn.CreateCommand())
                        {
                            resetSeqCmd.Transaction = transaction;
                            resetSeqCmd.CommandText = "DELETE FROM sqlite_sequence;";
                            await resetSeqCmd.ExecuteNonQueryAsync();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }

                    // إعادة مشغّلات حماية السجلات المرحّلة (حُذفت أعلاه لتفريغ البيانات؛ الترحيل لا يُعاد تطبيقه)
                    MeezanPOS.Infrastructure.Data.PostedRecordTriggers.Recreate(conn);

                    // Turn foreign keys back on
                    using (var pragmaCmd = conn.CreateCommand())
                    {
                        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
                        await pragmaCmd.ExecuteNonQueryAsync();
                    }
                }

                // Clear connection pools to ensure database file can be refreshed
                SqliteConnection.ClearAllPools();

                // Restore triggers and schema by running EF migrations
                await using var restoreContext = await _dbContextFactory.CreateDbContextAsync();
                await restoreContext.Database.MigrateAsync();

                Dialogs.Show("تم إعادة ضبط المنظومة وحذف جميع البيانات بنجاح!\n\nسيتم إغلاق التطبيق الآن، يرجى إعادة تشغيله يدوياً للبدء بقاعدة بيانات نظيفة.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error resetting database in SettingsViewModel");
                Dialogs.Show($"فشلت عملية إعادة ضبط قاعدة البيانات:\n{ex.Message}", "خطأ في إعادة الضبط", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            Dialogs.Show("الرجاء إدخال كلمة المرور لتأكيد إعادة الضبط.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

