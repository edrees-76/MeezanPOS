using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;

namespace MeezanPOS.Application.ViewModels;

public partial class ClosingAccountViewModel : ObservableObject
{
    private readonly IFinancialReportingService _reportingService;
    private readonly IPostingService _postingService;
    private readonly ISessionService _sessionService;

    [ObservableProperty]
    private DashboardPeriod selectedPeriod = DashboardPeriod.ThisMonth;

    [ObservableProperty]
    private DateTime? customStartDate = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime? customEndDate = DateTime.Today;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private ClosingAccountSummary summary = new();

    // Settle & Lock overlays
    [ObservableProperty]
    private bool isSettleDialogOpen;

    [ObservableProperty]
    private decimal settlePayoutAmount;

    [ObservableProperty]
    private decimal settleKeepAmount;

    [ObservableProperty]
    private string settleNotes = string.Empty;

    [ObservableProperty]
    private decimal settleCashBalance;

    [ObservableProperty]
    private bool settleIsProcessing;

    private readonly IAuthenticationService _authService;

    public ClosingAccountViewModel() : this(
        AppServiceProvider.Resolve<IFinancialReportingService>(),
        AppServiceProvider.Resolve<IPostingService>(),
        AppServiceProvider.Resolve<ISessionService>(),
        AppServiceProvider.Resolve<IAuthenticationService>())
    {
    }

    public ClosingAccountViewModel(
        IFinancialReportingService reportingService,
        IPostingService postingService,
        ISessionService sessionService,
        IAuthenticationService authService)
    {
        _reportingService = reportingService ?? throw new ArgumentNullException(nameof(reportingService));
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));

        _ = LoadReportAsync();
    }

    partial void OnSelectedPeriodChanged(DashboardPeriod value)
    {
        if (value != DashboardPeriod.Custom)
        {
            _ = LoadReportAsync();
        }
    }

    [RelayCommand]
    public async Task LoadReportAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        var period = SelectedPeriod;
        var customStart = CustomStartDate;
        var customEnd = CustomEndDate;

        try
        {
            await Task.Run(async () =>
            {
                var today = DateTime.Today;
                DateTime start = today;
                DateTime end = today.AddDays(1).AddTicks(-1);

                if (period == DashboardPeriod.Today)
                {
                    start = today;
                    end = today.AddDays(1).AddTicks(-1);
                }
                else if (period == DashboardPeriod.ThisWeek)
                {
                    int diff = (7 + (today.DayOfWeek - DayOfWeek.Sunday)) % 7;
                    start = today.AddDays(-diff);
                    end = start.AddDays(7).AddTicks(-1);
                }
                else if (period == DashboardPeriod.ThisMonth)
                {
                    start = new DateTime(today.Year, today.Month, 1);
                    end = start.AddMonths(1).AddTicks(-1);
                }
                else if (period == DashboardPeriod.LastMonth)
                {
                    start = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                    end = new DateTime(today.Year, today.Month, 1).AddTicks(-1);
                }
                else if (period == DashboardPeriod.Custom)
                {
                    start = customStart ?? today;
                    end = customEnd ?? today;
                    if (end < start)
                    {
                        var temp = start;
                        start = end;
                        end = temp;
                    }
                    end = end.Date.AddDays(1).AddTicks(-1);
                }

                var data = await _reportingService.GenerateSummaryAsync(start, end);
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Summary = data;
                });
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error loading closing account summary");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Dialogs.Show("حدث خطأ أثناء تحميل التقرير المالي: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenDetails(string category)
    {
        if (Summary == null) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new Presentation.Views.ClosingDetailsDialog(category, Summary.Liabilities, Summary.Suppliers)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();
        });
    }

    [RelayCommand]
    public async Task ExportPdfAsync(string mode)
    {
        if (IsExporting || Summary == null) return;
        IsExporting = true;

        try
        {
            string formatName = mode == "Comprehensive" ? "الشامل" : "المختصر";
            string fileName = $"الحساب الختامي {formatName} - {DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string tempFolder = Path.Combine(Path.GetTempPath(), "MeezanPOS");
            Directory.CreateDirectory(tempFolder);
            string filePath = Path.Combine(tempFolder, fileName);

            bool isComprehensive = mode == "Comprehensive";

            await Task.Run(() =>
            {
                MeezanPOS.Infrastructure.Reports.ClosingAccountPdfExporter.GenerateReport(
                    filePath,
                    Summary,
                    isComprehensive);
            });

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error exporting closing account PDF");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Dialogs.Show("حدث خطأ أثناء تصدير تقرير الـ PDF: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            IsExporting = false;
        }
    }

    // Settle & Lock Period
    [RelayCommand]
    public void OpenSettleDialog()
    {
        if (Summary == null) return;

        SettlePayoutAmount = 0m;
        SettleKeepAmount = 0m;
        SettleNotes = string.Empty;
        SettleCashBalance = Summary.CashLedger.ClosingBalance;
        IsSettleDialogOpen = true;
    }

    [RelayCommand]
    public void CloseSettleDialog()
    {
        IsSettleDialogOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmSettleAsync(object parameter)
    {
        if (SettleIsProcessing) return;

        if (parameter is System.Windows.Controls.PasswordBox passwordBox)
        {
            string password = passwordBox.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                Dialogs.Show("الرجاء إدخال كلمة مرور المدير لتأكيد الإقفال والتسوية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentUsername = _sessionService.CurrentUsername ?? "admin";
            var authService = _authService;
            var authenticatedUser = await authService.AuthenticateAsync(currentUsername, password);

            if (authenticatedUser == null)
            {
                Dialogs.Show("كلمة المرور غير صحيحة. يرجى إدخال كلمة مرور المدير الصحيحة.", "خطأ في المصادقة", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            passwordBox.Clear();
        }
        else
        {
            Dialogs.Show("الرجاء إدخال كلمة مرور المدير لتأكيد الإقفال والتسوية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SettlePayoutAmount < 0 || SettleKeepAmount < 0)
        {
            Dialogs.Show("المبالغ المدخلة يجب أن تكون أكبر من أو تساوي الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SettlePayoutAmount > SettleCashBalance)
        {
            Dialogs.Show("المبلغ المطلوب تسليمه للمالك يتجاوز الرصيد النقدي المتوفر حالياً بالخزينة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettleIsProcessing = true;
        try
        {
            string userId = _sessionService.CurrentUsername ?? "Admin";

            var result = await _postingService.SettleAndLockPeriodAsync(
                SettlePayoutAmount,
                SettleKeepAmount,
                SettleNotes.Trim(),
                userId);

            if (result.Success)
            {
                IsSettleDialogOpen = false;
                await LoadReportAsync();
                
                Dialogs.Show($"تمت التسوية المالية وإقفال الفترة بنجاح وتجميد الحركات.\nتم توليد ملف PDF وحفظه في:\n{result.PdfPath}", 
                    "تم بنجاح", MessageBoxButton.OK, MessageBoxImage.Information);

                if (!string.IsNullOrEmpty(result.PdfPath) && File.Exists(result.PdfPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.PdfPath) { UseShellExecute = true });
                }
            }
            else
            {
                string errors = string.Join("\n", result.Errors);
                Dialogs.Show("تعذر إكمال عملية الإقفال والتسوية المالي:\n" + errors, "فشل الإقفال", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error executing Settle and Lock");
            Dialogs.Show("حدث خطأ أثناء إجراء عملية الإقفال: " + ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SettleIsProcessing = false;
        }
    }
}
