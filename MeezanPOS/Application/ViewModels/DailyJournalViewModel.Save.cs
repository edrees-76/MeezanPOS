using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using QuestPDF.Fluent;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MeezanPOS.Application.ViewModels;

/// <summary>حفظ اليومية: بناء طلب الحفظ للخدمة.</summary>
public partial class DailyJournalViewModel
{
    // --- حفظ الحركة اليومية ---
    [RelayCommand]
    private async System.Threading.Tasks.Task SaveJournalAsync()
    {
        if (string.IsNullOrWhiteSpace(EmployeeName))
        {
            StatusMessage = "الرجاء إدخال اسم الموظف.";
            return;
        }
        if (TotalSales <= 0)
        {
            StatusMessage = "الرجاء إدخال المبيعات.";
            return;
        }

        foreach (var exp in ExpenseItems)
        {
            if (exp.IsWorkerWage && exp.IsDetailedWage && (exp.SelectedWorkerWagesDetails == null || !exp.SelectedWorkerWagesDetails.Any()))
            {
                StatusMessage = $"يرجى تحديد تفاصيل أجور حضور العمال للمصروف رقم {exp.SequenceNumber}.";
                Dialogs.Show($"يرجى تحديد تفاصيل أجور حضور العمال للمصروف رقم {exp.SequenceNumber}.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            var result = await _journalService.SaveAsync(BuildSaveRequest());
            if (!result.Success)
            {
                StatusMessage = result.Error ?? "تعذر الحفظ.";
                Dialogs.Show(StatusMessage, "تعذر الحفظ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            StatusMessage = "تم حفظ الحركة اليومية بنجاح ✓";
            IsSaved = true;

            // إغلاق النافذة والعودة بعد الحفظ
            CloseForm();
        }
        catch (System.Exception ex)
        {
            StatusMessage = "حدث خطأ أثناء الحفظ: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    /// <summary>
    /// تحويل حقول الشاشة إلى بيانات حفظ: البنود ذات المبلغ الموجب فقط، وحركات العمال التفصيلية لكل بند أجور.
    /// </summary>
    private JournalSaveRequest BuildSaveRequest()
    {
        var request = new JournalSaveRequest
        {
            EditingJournalId = editingJournalId,
            JournalDate = JournalDate,
            Shift = SelectedShiftType,
            ShiftDisplayName = GetShiftDisplayName(SelectedShiftType),
            EmployeeName = EmployeeName,
            Notes = Notes,
            CashFloat = CashFloat ?? 0m,
            TotalSales = TotalSales,
            BankingTotal = EffectiveBankingTotal,
            TotalExpenses = TotalExpenses,
            ReturnsTotal = ReturnsAmount,
            FreeOrdersTotal = FreeOrdersAmount,
            ActualCash = ActualCash ?? 0m,
        };

        // المصروفات
        foreach (var exp in ExpenseItems.Where(e => (e.Amount ?? 0m) > 0))
        {
            string desc = exp.ExpenseType;
            ExpenseType dbExpenseType;
            int? dbSupplierId = null;

            if (exp.IsPurchase || exp.IsInvoicePayment || exp.IsSupplierPayment || exp.IsGas || exp.IsCoal || exp.IsBread)
            {
                if (exp.IsPurchase) dbExpenseType = ExpenseType.Purchase;
                else if (exp.IsInvoicePayment) dbExpenseType = ExpenseType.InvoicePayment;
                else if (exp.IsSupplierPayment) dbExpenseType = ExpenseType.SupplierPayment;
                else if (exp.IsGas) dbExpenseType = ExpenseType.Gas;
                else if (exp.IsCoal) dbExpenseType = ExpenseType.Coal;
                else dbExpenseType = ExpenseType.Bread;

                dbSupplierId = exp.SelectedSupplierId;

                var parts = new[] { exp.SupplierName, exp.InvoiceNumber, exp.Notes }
                    .Where(part => !string.IsNullOrWhiteSpace(part));
                if (parts.Any()) desc = string.Join(" - ", parts);
            }
            else if (exp.IsWorkerWage)
            {
                dbExpenseType = ExpenseType.WorkerWage;
                if (!string.IsNullOrWhiteSpace(exp.Description)) desc = exp.Description;
            }
            else
            {
                // الأنواع البسيطة: نظافة، صيانة، مواصلات، مصروف نثري
                dbExpenseType = exp.ExpenseType switch
                {
                    "نظافة" => ExpenseType.Cleaning,
                    "صيانة" => ExpenseType.Maintenance,
                    "مواصلات" => ExpenseType.Transport,
                    "مصروف نثري" => ExpenseType.PettyCash,
                    _ => ExpenseType.Regular
                };
                if (!string.IsNullOrWhiteSpace(exp.Description)) desc = exp.Description;
            }

            request.ExpenseItems.Add(new MeezanPOS.Domain.Entities.DailyExpenseItem
            {
                SequenceNumber = exp.SequenceNumber,
                Amount = exp.Amount ?? 0m,
                Category = exp.ExpenseType,
                CategoryName = exp.ExpenseType,
                Description = desc,
                Type = dbExpenseType,
                SupplierId = dbSupplierId,
                SupplierName = exp.SupplierName,
                Notes = exp.Notes,
                InvoiceNumber = exp.InvoiceNumber,
                WorkerName = exp.IsWorkerWage ? (exp.IsDetailedWage ? "[متعدد]" : (!string.IsNullOrEmpty(exp.WorkerName) ? exp.WorkerName : null)) : null,
                WorkerId = (exp.IsWorkerWage && !exp.IsDetailedWage) ? exp.WorkerId : null,
                CreatedAt = System.DateTime.UtcNow
            });

            if (exp.IsWorkerWage && exp.IsDetailedWage && exp.SelectedWorkerWagesDetails != null && exp.SelectedWorkerWagesDetails.Any())
                request.WorkerTransactionsBySequence[exp.SequenceNumber] = BuildWorkerTransactions(exp.SelectedWorkerWagesDetails);
        }

        // الخدمات المصرفية
        foreach (var bank in BankingItems.Where(b => (b.Amount ?? 0m) > 0))
        {
            request.BankingItems.Add(new MeezanPOS.Domain.Entities.BankingItem
            {
                Amount = bank.Amount ?? 0m,
                Description = bank.InvoiceNumber?.Trim(),
                BankAccountId = bank.BankAccountId,
                ReferenceNumber = bank.Last4Digits?.Trim(),
                CreatedAt = System.DateTime.UtcNow
            });
        }

        // تقسيم مبيعات الخدمات المصرفية
        foreach (var bSale in BankSalesInputs.Where(b => (b.Amount ?? 0m) > 0))
        {
            request.BankSales.Add(new DailyJournalBankSale
            {
                BankAccountId = bSale.BankAccountId,
                BankName = bSale.BankFriendlyName,
                Amount = bSale.Amount ?? 0m,
                CreatedAt = System.DateTime.UtcNow
            });
        }

        // المرتجعات ثم المجاني
        foreach (var ret in Returns.Where(x => (x.Amount ?? 0m) > 0))
            request.Adjustments.Add(NewAdjustment(false, ret.Amount ?? 0m, ret.InvoiceNumber, ret.Notes, ret.PersonName));
        foreach (var free in FreeOrders.Where(x => (x.Amount ?? 0m) > 0))
            request.Adjustments.Add(NewAdjustment(true, free.Amount ?? 0m, free.InvoiceNumber, free.Notes, free.PersonName));

        return request;
    }

    private static MeezanPOS.Domain.Entities.OrderAdjustmentItem NewAdjustment(bool isFree, decimal amount, string? invoice, string? notes, string? person) => new()
    {
        IsFreeOrder = isFree,
        Amount = amount,
        InvoiceNumber = invoice,
        Notes = notes,
        PersonName = person?.Trim(),
        CreatedAt = System.DateTime.UtcNow
    };

    /// <summary>استحقاق وسداد وسلفة وخصم لكل عامل في بند الأجور التفصيلي.</summary>
    private System.Collections.Generic.List<WorkerTransaction> BuildWorkerTransactions(System.Collections.Generic.IEnumerable<WorkerTransactionDetailDto> details)
    {
        var list = new System.Collections.Generic.List<WorkerTransaction>();
        WorkerTransaction Tx(WorkerTransactionDetailDto d, WorkerTransactionType type, decimal debit, decimal credit, string notes) => new()
        {
            WorkerId = d.WorkerId,
            WorkerName = d.WorkerName,
            TransactionDate = JournalDate,
            Type = type,
            DebitAmount = debit,
            CreditAmount = credit,
            Notes = notes
        };

        foreach (var d in details)
        {
            if (d.IsAttended)
            {
                list.Add(Tx(d, WorkerTransactionType.WageAccrual, 0m, d.ActualWage,
                    string.IsNullOrWhiteSpace(d.Notes) ? "استحقاق حضور - وردية يومية" : $"استحقاق: {d.Notes}"));
                if (d.AmountPaid > 0)
                    list.Add(Tx(d, WorkerTransactionType.Payment, d.AmountPaid, 0m,
                        string.IsNullOrWhiteSpace(d.Notes) ? "سداد أجر - وردية يومية" : $"سداد: {d.Notes}"));
            }
            if (d.Advance > 0)
                list.Add(Tx(d, WorkerTransactionType.Advance, d.Advance, 0m,
                    string.IsNullOrWhiteSpace(d.Notes) ? "سلفة - وردية يومية" : $"سلفة: {d.Notes}"));
            if (d.Deduction > 0)
                list.Add(Tx(d, WorkerTransactionType.Deduction, d.Deduction, 0m,
                    string.IsNullOrWhiteSpace(d.Notes) ? "خصم وغرامة - وردية يومية" : $"خصم: {d.Notes}"));
        }
        return list;
    }

    [ObservableProperty]
    private bool isViewingMode = false;

    [ObservableProperty]
    private string saveButtonText = "حفظ وترحيل";

    partial void OnIsViewingModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotViewingMode));
    }

    public bool IsNotViewingMode => !IsViewingMode;
}
