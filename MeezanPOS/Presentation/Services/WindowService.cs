using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Presentation.Views;

namespace MeezanPOS.Presentation.Services;

/// <summary>سبب مختار من قائمة + شرح تفصيلي (نوافذ فك القفل وحذف الحركات).</summary>
public sealed record ReasonResult(string Reason, string Detail)
{
    public override string ToString() => $"{Reason} - {Detail}";
}

/// <summary>نتيجة نافذة توزيع أجور العمال.</summary>
public sealed record WorkerWagesResult(List<WorkerTransactionDetailDto> Details, decimal TotalPaid);

/// <summary>
/// فتح النوافذ والحوارات بنوايا واضحة بدل إنشاء النوافذ داخل منطق الشاشات (ViewModels).
/// منطق الشاشة يطلب "سبب فك القفل" أو "فترة تاريخ" ويتلقى النتيجة فقط، فيمكن اختباره ببديل لا يفتح نوافذ.
/// </summary>
public interface IWindowService
{
    ReasonResult? AskPeriodUnlockReason();
    ReasonResult? AskDeleteReason();
    (DateTime Start, DateTime End)? AskDateRange(DateTime? start, DateTime? end);
    Task<WorkerWagesResult?> EditWorkerWagesAsync(List<WorkerTransactionDetailDto>? current);
    bool EditWorker(Worker? worker);
    bool ChangePassword(bool forced);
    string? PickFolder(string title, string? initialDirectory);
    string? PickFile(string title, string filter);

    void ShowGeneralExpenseForm(object viewModel);
    void ShowClosingDetails(string category, LiabilitiesSummary liabilities, List<SupplierReportItem> suppliers);
    void ShowTransactionDetails(CashMovement movement);
    void ShowWorkerStatement(WorkerWageSummary summary);
    void ShowUserManagement();

    /// <summary>فتح الواجهة الرئيسية بعد الدخول وإغلاق ما عداها.</summary>
    void ShowMainShell();
    /// <summary>العودة لشاشة الدخول بعد الخروج وإغلاق ما عداها.</summary>
    void ShowLogin();
}

/// <summary>نقطة الوصول الموحدة؛ الاختبارات تستبدل <see cref="Current"/> ببديل.</summary>
public static class AppWindows
{
    public static IWindowService Current { get; set; } = new WindowService();
}

/// <summary>
/// التنفيذ الفعلي: كل نافذة مملوكة للنافذة النشطة، وتُفتح على خيط الواجهة حتى لو طُلبت من خيط خلفي.
/// </summary>
public sealed class WindowService : IWindowService
{
    private static T OnUi<T>(Func<T> action)
    {
        var app = System.Windows.Application.Current;
        return app == null || app.Dispatcher.CheckAccess() ? action() : app.Dispatcher.Invoke(action);
    }

    private static void OnUi(Action action) => OnUi(() => { action(); return true; });

    private static Window? ActiveOwner()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return null;
        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
               ?? (app.MainWindow?.IsVisible == true ? app.MainWindow : null);
    }

    private static bool? ShowModal(Window window)
    {
        var owner = ActiveOwner();
        if (owner != null && !ReferenceEquals(owner, window))
            window.Owner = owner;
        return window.ShowDialog();
    }

    public ReasonResult? AskPeriodUnlockReason() => OnUi(() =>
    {
        var dialog = new PeriodUnlockDialog();
        return ShowModal(dialog) == true ? new ReasonResult(dialog.SelectedReason, dialog.SelectedDetailReason) : null;
    });

    public ReasonResult? AskDeleteReason() => OnUi(() =>
    {
        var dialog = new DeleteReasonDialog();
        return ShowModal(dialog) == true ? new ReasonResult(dialog.SelectedReason, dialog.SelectedDetailReason) : null;
    });

    public (DateTime Start, DateTime End)? AskDateRange(DateTime? start, DateTime? end) => OnUi<(DateTime, DateTime)?>(() =>
    {
        var dialog = new DatePeriodSelectionDialog(start, end);
        return ShowModal(dialog) == true ? (dialog.SelectedStartDate, dialog.SelectedEndDate) : null;
    });

    public async Task<WorkerWagesResult?> EditWorkerWagesAsync(List<WorkerTransactionDetailDto>? current)
    {
        var vm = new WorkerWagesDialogViewModel();
        await vm.LoadWorkersAsync(current);
        return OnUi(() =>
        {
            var dialog = new WorkerWagesDialog { DataContext = vm };
            return ShowModal(dialog) == true ? new WorkerWagesResult(vm.ResultDetails, vm.TotalAmountPaid) : null;
        });
    }

    public bool EditWorker(Worker? worker) => OnUi(() => ShowModal(new WorkerEditDialog(worker)) == true);

    public bool ChangePassword(bool forced) => OnUi(() =>
    {
        // عند الدخول قد لا يكون للتطبيق MainWindow بعد؛ عندها يجعل WPF أول نافذة تُنشأ (الحوار نفسه)
        // هي MainWindow فيفشل ضبط المالك. نثبّت النافذة الظاهرة (شاشة الدخول) قبل إنشاء الحوار.
        var app = System.Windows.Application.Current;
        var owner = ActiveOwner();
        if (app != null && owner != null && app.MainWindow?.IsVisible != true)
            app.MainWindow = owner;
        return ShowModal(new ChangePasswordDialog(isForced: forced)) == true;
    });

    public string? PickFolder(string title, string? initialDirectory) => OnUi(() =>
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, InitialDirectory = initialDirectory ?? string.Empty };
        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FolderName : null;
    });

    public string? PickFile(string title, string filter) => OnUi(() =>
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    });

    public void ShowGeneralExpenseForm(object viewModel) => OnUi(() => ShowModal(new AddGeneralExpenseDialog { DataContext = viewModel }));

    public void ShowClosingDetails(string category, LiabilitiesSummary liabilities, List<SupplierReportItem> suppliers)
        => OnUi(() => ShowModal(new ClosingDetailsDialog(category, liabilities, suppliers)));

    public void ShowTransactionDetails(CashMovement movement) => OnUi(() => ShowModal(new TransactionDetailsViewWindow(movement)));

    public void ShowWorkerStatement(WorkerWageSummary summary) => OnUi(() => ShowModal(new WorkerStatementWindow(summary)));

    public void ShowUserManagement() => OnUi(() => ShowModal(new UserManagementWindow()));

    public void ShowMainShell() => OnUi(() => SwitchShell(new MainView()));

    public void ShowLogin() => OnUi(() => SwitchShell(new LoginView()));

    private static void SwitchShell(Window next)
    {
        var app = System.Windows.Application.Current;
        app.MainWindow = next;
        next.Show();
        foreach (var window in app.Windows.OfType<Window>().ToList())
        {
            if (!ReferenceEquals(window, next))
                window.Close();
        }
    }
}
