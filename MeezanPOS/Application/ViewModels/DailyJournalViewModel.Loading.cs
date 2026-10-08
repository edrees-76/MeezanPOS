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

/// <summary>تحميل يومية قائمة للعرض أو التعديل.</summary>
public partial class DailyJournalViewModel
{
    public void LoadJournalForViewing(MeezanPOS.Domain.Entities.DailyJournal journal)
    {
        IsViewingMode = true;
        editingJournalId = null;
        editingJournalShift = null;
        LoadJournalData(journal);
        StatusMessage = "وضع العرض - لا يمكن تعديل حركة سابقة";
    }

    public void LoadJournalForEditing(MeezanPOS.Domain.Entities.DailyJournal journal)
    {
        IsViewingMode = false;
        editingJournalId = journal.Id;
        editingJournalShift = journal.ShiftType;
        LoadJournalData(journal);
        SaveButtonText = "حفظ";
        StatusMessage = "وضع التعديل - يمكنك تعديل البيانات ثم الضغط على حفظ";
    }

    private void LoadJournalData(MeezanPOS.Domain.Entities.DailyJournal journal)
    {
        JournalDate = journal.JournalDate;
        SelectedShiftType = journal.ShiftType;
        EmployeeName = journal.EmployeeName;
        Notes = journal.Notes ?? "";
        CashFloat = journal.CashFloat;
        DrawerPayouts = journal.DrawerPayouts;

        CashSalesInput = journal.TotalSales - journal.BankingTotal;
        BankingSalesInput = journal.BankingTotal;
        ActualCash = journal.ActualCash;

        ExpenseItems.Clear();
        if (journal.ExpenseItems != null)
        {
            var multiWorkerItemIds = journal.ExpenseItems.Where(x => x.WorkerName == "[متعدد]").Select(x => x.Id).ToList();
            var workerTxsByItem = _journalService.GetWorkerTransactionsByExpenseItem(multiWorkerItemIds);
            foreach (var e in journal.ExpenseItems)
            {
                var item = new ExpenseItemViewModel
                {
                    SequenceNumber = ExpenseItems.Count + 1,
                    ExpenseType = e.Category ?? "",
                    Amount = e.Amount,
                    Description = e.Description ?? "",
                    SelectedSupplierId = e.SupplierId,
                    InvoiceNumber = e.InvoiceNumber ?? "",
                    Notes = e.Notes ?? "",
                    WorkerName = e.WorkerName ?? "",
                    WorkerId = e.WorkerId,
                    IsDetailedWage = e.WorkerName == "[متعدد]"
                };

                if (e.WorkerName == "[متعدد]")
                {
                    var txs = workerTxsByItem.TryGetValue(e.Id, out var itemTxs) ? itemTxs : new System.Collections.Generic.List<WorkerTransaction>();

                    var grouped = txs.GroupBy(t => t.WorkerId);
                    item.SelectedWorkerWagesDetails = grouped.Select(g => {
                        var workerId = g.Key;
                        var workerName = g.First().WorkerName;
                        var accrual = g.FirstOrDefault(t => t.Type == WorkerTransactionType.WageAccrual);
                        var payment = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Payment);
                        var advance = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Advance);
                        var deduction = g.FirstOrDefault(t => t.Type == WorkerTransactionType.Deduction);

                        return new WorkerTransactionDetailDto
                        {
                            WorkerId = workerId,
                            WorkerName = workerName,
                            IsAttended = accrual != null,
                            ActualWage = accrual?.CreditAmount ?? 0m,
                            Advance = advance?.DebitAmount ?? 0m,
                            Deduction = deduction?.DebitAmount ?? 0m,
                            AmountPaid = payment?.DebitAmount ?? 0m,
                            Notes = accrual?.Notes ?? payment?.Notes ?? advance?.Notes ?? deduction?.Notes
                        };
                    }).ToList();
                }
                else
                {
                    item.SelectedWorkerWagesDetails = new System.Collections.Generic.List<WorkerTransactionDetailDto>();
                }

                item.PropertyChanged += (s, e) => RefreshCalculations();
                ExpenseItems.Add(item);
            }
        }

        Returns.Clear();
        FreeOrders.Clear();
        if (journal.Adjustments != null)
        {
            foreach (var a in journal.Adjustments)
            {
                var item = new OrderAdjustmentItemViewModel
                {
                    Amount = a.Amount,
                    InvoiceNumber = a.InvoiceNumber ?? "",
                    Notes = a.Notes ?? "",
                    PersonName = a.PersonName ?? ""
                };
                item.PropertyChanged += (s, e) => RefreshCalculations();
                if (a.IsFreeOrder)
                {
                    item.SequenceNumber = FreeOrders.Count + 1;
                    FreeOrders.Add(item);
                }
                else
                {
                    item.SequenceNumber = Returns.Count + 1;
                    Returns.Add(item);
                }
            }
        }

        BankingItems.Clear();
        if (journal.BankingItems != null)
        {
            foreach (var b in journal.BankingItems)
            {
                string invoiceNo = "";
                string last4 = b.ReferenceNumber ?? "";
                string bankName = "";

                var activeBank = ActiveBankAccounts.FirstOrDefault(x => x.Id == b.BankAccountId);
                if (activeBank != null)
                {
                    bankName = activeBank.DisplayName;
                }

                string desc = b.Description ?? "";
                bool isLegacy = false;
                if (!string.IsNullOrEmpty(desc))
                {
                    foreach (var acc in ActiveBankAccounts)
                    {
                        if (desc.Contains(acc.DisplayName))
                        {
                            isLegacy = true;
                            break;
                        }
                    }
                    if (!isLegacy && desc.Contains(" - "))
                    {
                        isLegacy = true;
                    }
                }

                if (isLegacy)
                {
                    invoiceNo = "";
                    if (string.IsNullOrEmpty(last4) && desc.Contains(" - "))
                    {
                        var parts = desc.Split(new[] { " - " }, StringSplitOptions.None);
                        last4 = parts[parts.Length - 1];
                    }
                }
                else
                {
                    invoiceNo = desc;
                }

                var vmItem = new BankingItemViewModel
                {
                    SequenceNumber = BankingItems.Count + 1,
                    Amount = b.Amount,
                    BankName = bankName,
                    InvoiceNumber = invoiceNo,
                    Last4Digits = last4,
                    BankAccountId = b.BankAccountId
                };
                vmItem.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(BankingItemViewModel.BankAccountId) && s is BankingItemViewModel bankItem && bankItem.BankAccountId.HasValue)
                    {
                        var bank = ActiveBankAccounts.FirstOrDefault(x => x.Id == bankItem.BankAccountId.Value);
                        if (bank != null)
                        {
                            bankItem.BankName = bank.DisplayName;
                        }
                    }
                    RefreshCalculations();
                };
                BankingItems.Add(vmItem);
            }
        }

        RefreshCalculations();
    }
}
