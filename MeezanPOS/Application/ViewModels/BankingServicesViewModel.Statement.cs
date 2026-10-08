using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace MeezanPOS.Application.ViewModels;

/// <summary>كشف حساب المصرف.</summary>
public partial class BankingServicesViewModel
{
    // --- كشف حساب المصرف البنكي ---

    partial void OnStatementAccountChanged(BankAccount? value)
    {
        StatementTransactions.Clear();
        StatementOpeningBalance = 0;
        StatementTotalDeposits = 0;
        StatementTotalWithdrawals = 0;
        StatementEndingBalance = 0;
    }

    partial void OnShowPersonalTransactionsChanged(bool value)
    {
        StatementTransactions.Clear();
        StatementOpeningBalance = 0;
        StatementTotalDeposits = 0;
        StatementTotalWithdrawals = 0;
        StatementEndingBalance = 0;
    }

    [RelayCommand]
    public async Task LoadStatementAsync()
    {
        if (StatementAccount == null)
        {
            StatementTransactions.Clear();
            StatementOpeningBalance = 0;
            StatementTotalDeposits = 0;
            StatementTotalWithdrawals = 0;
            StatementEndingBalance = 0;
            return;
        }

        IsLoading = true;
        try
        {
            IsStatementAccountMixed = StatementAccount.AccountType == BankAccountType.PersonalMixed;

            // 1. حساب الرصيد الافتتاحي قبل فترة الكشف
            StatementOpeningBalance = await _bankService.GetOpeningBalanceBeforeDateAsync(
                StatementAccount.Id, StatementStartDate);

            // 2. جلب الحركات ضمن الفترة
            var allTx = await _bankService.GetTransactionsAsync(
                StatementAccount.Id, StatementStartDate, StatementEndDate);

            // 3. تصفية الحسابات المختلطة (الخيار ب المعتمد)
            // ملاحظة: نسمح بظهور الإيداع والسحب اليدوي إذا كان مرتبطاً ببيان (SourceType) مثل تمويل الشريك
            if (IsStatementAccountMixed && !ShowPersonalTransactions)
            {
                allTx = allTx.Where(t =>
                    (t.Type != BankTransactionType.Deposit && t.Type != BankTransactionType.Withdrawal) ||
                    !string.IsNullOrEmpty(t.SourceType))
                    .ToList();
            }

            // تجميع مبيعات الكروت يومياً
            var cardSales = allTx.Where(t => t.Type == BankTransactionType.CardSalesDeposit).ToList();
            var otherTx = allTx.Where(t => t.Type != BankTransactionType.CardSalesDeposit).ToList();

            var groupedCardSales = cardSales
                .GroupBy(t => t.TransactionDate.Date)
                .Select(g =>
                {
                    var lastTx = g.OrderBy(t => t.Id).Last();
                    return new BankTransaction
                    {
                        Id = lastTx.Id,
                        BankAccountId = lastTx.BankAccountId,
                        Type = BankTransactionType.CardSalesDeposit,
                        Amount = g.Sum(x => x.Amount),
                        ReferenceNumber = "مبيعات",
                        TransactionDate = g.Key,
                        Notes = "مبيعات المطعم",
                        BalanceAfter = lastTx.BalanceAfter,
                        SourceType = lastTx.SourceType,
                        SourceId = lastTx.SourceId
                    };
                }).ToList();

            allTx = otherTx.Concat(groupedCardSales).ToList();

            // 4. ترتيب تصاعدي لكافة الحركات لحساب الأرصدة التراكمية والملخصات بدقة
            var ordered = allTx.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).ToList();

            // إعادة حساب الرصيد التراكمي لضمان الدقة لكافة الحركات بالكامل
            decimal currentBalance = StatementOpeningBalance;
            foreach (var tx in ordered)
            {
                if (IsStatementDepositType(tx))
                    currentBalance += tx.Amount;
                else
                    currentBalance -= tx.Amount;

                tx.BalanceAfter = currentBalance;
            }

            // 5. حساب المُلخصات المالية الكاملة وغير المفلترة لكشف الحساب
            StatementTotalDeposits = ordered.Where(IsStatementDepositType).Sum(t => t.Amount);
            StatementTotalWithdrawals = ordered.Where(t => !IsStatementDepositType(t)).Sum(t => t.Amount);
            StatementEndingBalance = currentBalance;

            // 6. تطبيق فلاتر العرض البصري فقط (نوع الحركة والبحث) دون التأثير على أرصدة وملخصات الكشف الحقيقي
            var displayList = ordered.AsEnumerable();

            if (SelectedTransactionTypeFilter != "الكل")
            {
                displayList = displayList.Where(t => GetStatementTypeDisplayName(t.Type) == SelectedTransactionTypeFilter);
            }

            if (!string.IsNullOrWhiteSpace(StatementSearchText))
            {
                var search = StatementSearchText.Trim();
                displayList = displayList.Where(t =>
                    (t.Notes != null && t.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (t.ReferenceNumber != null && t.ReferenceNumber.Contains(search, StringComparison.OrdinalIgnoreCase))
                );
            }

            var displayListWithSeq = displayList.ToList();
            for (int i = 0; i < displayListWithSeq.Count; i++)
            {
                displayListWithSeq[i].SequenceNumber = i + 1;
            }
            StatementTransactions = new ObservableCollection<BankTransaction>(displayListWithSeq);
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تحميل كشف الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void PrintStatement()
    {
        if (StatementAccount == null || !StatementTransactions.Any())
        {
            Dialogs.Show("يرجى اختيار حساب وتحميل كشف الحساب أولاً قبل التصدير.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            string safeName = string.Join("_", StatementAccount.FriendlyName.Split(System.IO.Path.GetInvalidFileNameChars()));
            string fileName = $"BankStatement_{safeName}_{StatementStartDate:yyyyMMdd}_{StatementEndDate:yyyyMMdd_HHmmss}.pdf";
            string filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

            BankStatementPdfReport.GeneratePdf(
                filePath,
                StatementAccount,
                StatementOpeningBalance,
                StatementTotalDeposits,
                StatementTotalWithdrawals,
                StatementEndingBalance,
                StatementTransactions.ToList(),
                StatementStartDate,
                StatementEndDate);

            // فتح ملف PDF تلقائياً
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تصدير التقرير:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
