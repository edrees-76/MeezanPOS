using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Presentation.Services;

namespace MeezanPOS.Tests.Helpers;

/// <summary>
/// بديل خدمة النوافذ في الاختبارات: لا يفتح نوافذ، يعيد نتائج مضبوطة مسبقاً ويسجل ما طُلب.
/// </summary>
public sealed class FakeWindowService : IWindowService
{
    public ReasonResult? UnlockReason { get; set; }
    public ReasonResult? DeleteReason { get; set; }
    public (DateTime Start, DateTime End)? DateRange { get; set; }
    public WorkerWagesResult? WorkerWages { get; set; }
    public bool EditWorkerResult { get; set; }
    public bool ChangePasswordResult { get; set; } = true;
    public string? Folder { get; set; }
    public string? File { get; set; }

    public List<string> Calls { get; } = new();

    public ReasonResult? AskPeriodUnlockReason() { Calls.Add(nameof(AskPeriodUnlockReason)); return UnlockReason; }
    public ReasonResult? AskDeleteReason() { Calls.Add(nameof(AskDeleteReason)); return DeleteReason; }
    public (DateTime Start, DateTime End)? AskDateRange(DateTime? start, DateTime? end) { Calls.Add(nameof(AskDateRange)); return DateRange; }
    public Task<WorkerWagesResult?> EditWorkerWagesAsync(List<WorkerTransactionDetailDto>? current) { Calls.Add(nameof(EditWorkerWagesAsync)); return Task.FromResult(WorkerWages); }
    public bool EditWorker(Worker? worker) { Calls.Add(nameof(EditWorker)); return EditWorkerResult; }
    public bool ChangePassword(bool forced) { Calls.Add(nameof(ChangePassword)); return ChangePasswordResult; }
    public string? PickFolder(string title, string? initialDirectory) { Calls.Add(nameof(PickFolder)); return Folder; }
    public string? PickFile(string title, string filter) { Calls.Add(nameof(PickFile)); return File; }
    public void ShowGeneralExpenseForm(object viewModel) => Calls.Add(nameof(ShowGeneralExpenseForm));
    public void ShowClosingDetails(string category, LiabilitiesSummary liabilities, List<SupplierReportItem> suppliers) => Calls.Add(nameof(ShowClosingDetails));
    public void ShowTransactionDetails(CashMovement movement) => Calls.Add(nameof(ShowTransactionDetails));
    public void ShowWorkerStatement(WorkerWageSummary summary) => Calls.Add(nameof(ShowWorkerStatement));
    public void ShowUserManagement() => Calls.Add(nameof(ShowUserManagement));
    public void ShowMainShell() => Calls.Add(nameof(ShowMainShell));
    public void ShowLogin() => Calls.Add(nameof(ShowLogin));
}
