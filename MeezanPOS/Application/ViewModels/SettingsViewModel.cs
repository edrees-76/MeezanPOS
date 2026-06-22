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
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool isResetPasswordDialogOpen;

    [ObservableProperty]
    private string preferredBackupPath = string.Empty;

    [ObservableProperty]
    private BackupFrequency selectedFrequency = BackupFrequency.Disabled;

    [ObservableProperty]
    private string lastAutoBackupDateText = "لا يوجد";

    public SettingsViewModel() : this(
        AppServiceProvider.Resolve<IDbContextFactory<AppDbContext>>())
    {
    }

    public SettingsViewModel(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
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
                MessageBox.Show($"تم إنشاء النسخة الاحتياطية بنجاح في المسار:\n{targetFilePath}", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("فشلت عملية النسخ الاحتياطي لقاعدة البيانات. تفاصيل الخطأ متوفرة في سجل السيريلوج.", "خطأ في النسخ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task RestoreNowAsync()
    {
        try
        {
            // Verify no active shifts (Draft)
            using (var context = _dbContextFactory.CreateDbContext())
            {
                var hasActiveShifts = await context.DailyJournals.AnyAsync(j => j.FinancialStatus == FinancialStatus.Draft);
                if (hasActiveShifts)
                {
                    MessageBox.Show("يوجد ورديات مفتوحة (مسودة) غير مرحلة حالياً. يرجى ترحيلها أو حذفها من قائمة المبيعات اليومية أولاً قبل استعادة نسخة قديمة لتجنب فقدان البيانات.", "تنبيه هام", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                var result = MessageBox.Show(
                    "تحذير: استعادة قاعدة البيانات ستؤدي إلى استبدال كافة البيانات الحالية بالبيانات الموجودة في الملف المحدد بشكل نهائي ولا يمكن التراجع عن ذلك.\n\nهل أنت متأكد من رغبتك في الاستمرار؟",
                    "تأكيد استعادة البيانات",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.Yes)
                {
                    // Clear connection pools to release file lock
                    SqliteConnection.ClearAllPools();

                    var dbPath = AppDbContext.GetDatabasePath();
                    var tempDbPath = dbPath + ".tmp";

                    // Safe copy using Backup API
                    using (var source = new SqliteConnection($"Data Source={selectedFile};Pooling=False"))
                    using (var destination = new SqliteConnection($"Data Source={tempDbPath};Pooling=False"))
                    {
                        await source.OpenAsync();
                        await destination.OpenAsync();
                        source.BackupDatabase(destination);
                    }

                    // Atomic replace
                    if (File.Exists(dbPath))
                    {
                        File.Delete(dbPath);
                    }
                    File.Move(tempDbPath, dbPath);

                    MessageBox.Show("تم استعادة قاعدة البيانات بنجاح!\n\nسيتم إغلاق التطبيق الآن، يرجى إعادة تشغيله يدوياً لتطبيق البيانات المسترجعة.", "نجاح الاستعادة", MessageBoxButton.OK, MessageBoxImage.Information);
                    System.Windows.Application.Current.Shutdown();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error restoring database");
            MessageBox.Show($"فشلت عملية استعادة قاعدة البيانات:\n{ex.Message}", "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetSystem()
    {
        var result = MessageBox.Show(
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
        if (parameter is System.Windows.Controls.PasswordBox passwordBox)
        {
            string password = passwordBox.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("الرجاء إدخال كلمة مرور الحساب الحالي لتأكيد إعادة الضبط.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sessionService = AppServiceProvider.Resolve<ISessionService>();
            var authService = AppServiceProvider.Resolve<IAuthenticationService>();
            string currentUsername = sessionService.CurrentUsername ?? "admin";

            var authenticatedUser = await authService.AuthenticateAsync(currentUsername, password);
            if (authenticatedUser == null)
            {
                MessageBox.Show("كلمة المرور غير صحيحة. يرجى إدخال كلمة المرور الصحيحة لحسابك.", "خطأ في المصادقة", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            passwordBox.Clear();
            IsResetPasswordDialogOpen = false;

            try
            {
                var dbPath = AppDbContext.GetDatabasePath();
                var tablesToKeep = new System.Collections.Generic.HashSet<string>
                {
                    "Users",
                    "Roles",
                    "__EFMigrationsHistory"
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
                        using (var dropCmd = conn.CreateCommand())
                        {
                            dropCmd.CommandText = $"DROP TRIGGER IF EXISTS \"{trigger}\";";
                            await dropCmd.ExecuteNonQueryAsync();
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

                    // Clear tables
                    foreach (var table in allTables)
                    {
                        if (!tablesToKeep.Contains(table))
                        {
                            using (var deleteCmd = conn.CreateCommand())
                            {
                                deleteCmd.CommandText = $"DELETE FROM \"{table}\";";
                                await deleteCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }

                    // Reset auto-increment sequences
                    using (var resetSeqCmd = conn.CreateCommand())
                    {
                        resetSeqCmd.CommandText = "DELETE FROM sqlite_sequence;";
                        await resetSeqCmd.ExecuteNonQueryAsync();
                    }

                    // Turn foreign keys back on
                    using (var pragmaCmd = conn.CreateCommand())
                    {
                        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
                        await pragmaCmd.ExecuteNonQueryAsync();
                    }
                }

                // Clear connection pools to ensure database file can be refreshed
                SqliteConnection.ClearAllPools();

                MessageBox.Show("تم إعادة ضبط المنظومة وحذف جميع البيانات بنجاح!\n\nسيتم إغلاق التطبيق الآن، يرجى إعادة تشغيله يدوياً للبدء بقاعدة بيانات نظيفة.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error resetting database in SettingsViewModel");
                MessageBox.Show($"فشلت عملية إعادة ضبط قاعدة البيانات:\n{ex.Message}", "خطأ في إعادة الضبط", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("الرجاء إدخال كلمة المرور لتأكيد إعادة الضبط.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

