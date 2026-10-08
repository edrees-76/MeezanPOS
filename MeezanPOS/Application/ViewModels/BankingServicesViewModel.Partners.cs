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

/// <summary>ديون الشركاء والمالك وتسوياتها.</summary>
public partial class BankingServicesViewModel
{
    // --- تبويب 4: مستحقات وتسويات الشركاء والمالك ---

    [RelayCommand]
    public void OpenSettlementForm(OwnerDebt? debt = null)
    {
        SelectedDebtForSettlement = debt;
        SettlementPartnerName = debt != null ? debt.PartnerName : (PartnerNames.FirstOrDefault() ?? string.Empty);
        SettlementAmount = debt != null ? debt.Amount : 0;
        SelectedSettlementSource = "CashRegister";
        SettlementBankAccount = BankAccounts.FirstOrDefault(a => a.IsActive);
        SettlementNotes = debt != null ? $"تسوية دين {debt.ExpenseCategory}" : "سحب أرباح أو تسوية مستحقات";
        SettlementDate = DateTime.Now;
        IsSettlementFormOpen = true;
    }

    [RelayCommand]
    public void OpenDebtForm()
    {
        DebtPartnerName = PartnerNames.FirstOrDefault() ?? string.Empty;
        DebtAmount = 0;
        SelectedDebtDestinationIndex = 0; // النقدية المباشرة
        DebtBankAccount = BankAccounts.FirstOrDefault(a => a.IsActive);
        DebtNotes = "تمويل تشغيلي جديد";
        DebtTransferReference = string.Empty;
        SelectedDebtPaymentMethodIndex = 0; // نقدي افتراضياً
        DebtDate = DateTime.Now;
        IsDebtFormOpen = true;
    }

    [RelayCommand]
    public void CloseDebtForm()
    {
        IsDebtFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveDebtAsync()
    {
        if (string.IsNullOrWhiteSpace(DebtPartnerName) || DebtAmount <= 0)
        {
            Dialogs.Show("يرجى إدخال اسم الشريك والمبلغ بشكل صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isBankDestination = (SelectedDebtDestinationIndex == 1);

        if (isBankDestination && DebtBankAccount == null)
        {
            Dialogs.Show("يجب تحديد الحساب البنكي المستلم للتمويل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            // 1. تسجيل الدين في سجل الشركاء
            var debt = await _ownerDebtService.RecordDebtAsync(
                DebtPartnerName,
                DebtAmount,
                "تمويل تشغيلي",
                DebtNotes,
                DebtDate,
                isBankDestination ? "Bank" : "Cash",
                isBankDestination ? DebtBankAccount?.Id : null,
                SelectedDebtPaymentMethodIndex == 1 ? "Transfer" : "Cash",
                SelectedDebtPaymentMethodIndex == 1 ? DebtTransferReference : null);

            // 2. إذا كانت الوجهة هي البنك، نسجل حركة إيداع في المصرف
            if (isBankDestination && DebtBankAccount != null)
            {
                bool isBankTransfer = (SelectedDebtPaymentMethodIndex == 1);

                // التحقق من إدخال أخر 4 أرقام عند التحويل المصرفي
                if (isBankTransfer && string.IsNullOrWhiteSpace(DebtTransferReference))
                {
                    Dialogs.Show("يرجى إدخال أخر 4 أرقام من عملية التحويل المصرفي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var bankNotes = $"تمويل من الشريك: {DebtPartnerName}";
                if (isBankTransfer && !string.IsNullOrWhiteSpace(DebtTransferReference))
                    bankNotes += $" | رقم العملية: {DebtTransferReference}";
                if (!string.IsNullOrEmpty(DebtNotes)) bankNotes += $" | {DebtNotes}";

                // المرجع: أرقام التحويل أو "تمويل شريك" للنقدي
                string reference = isBankTransfer ? DebtTransferReference : "تمويل شريك";

                // SourceType يميّز نوع الدفع: OwnerDebt_Transfer أو OwnerDebt_Cash
                string sourceType = isBankTransfer ? "OwnerDebt_Transfer" : "OwnerDebt_Cash";

                await _bankService.RecordTransactionAsync(
                    DebtBankAccount.Id,
                    BankTransactionType.Deposit,
                    DebtAmount,
                    reference,
                    bankNotes,
                    sourceType,
                    debt.Id,
                    DebtDate);
            }

            Dialogs.Show("تم حفظ بيانات تمويل الشريك بنجاح.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            IsDebtFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تسجيل التمويل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CloseSettlementForm()
    {
        IsSettlementFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveSettlementAsync()
    {
        if (string.IsNullOrWhiteSpace(SettlementPartnerName) || SettlementAmount <= 0)
        {
            Dialogs.Show("يرجى إدخال اسم الشريك والمبلغ بشكل صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var source = SelectedSettlementSource switch
        {
            "Bank" => OwnerDebtSettlementSource.Bank,
            "PettyCash" => OwnerDebtSettlementSource.PettyCash,
            _ => OwnerDebtSettlementSource.CashRegister
        };

        if (source == OwnerDebtSettlementSource.Bank && SettlementBankAccount == null)
        {
            Dialogs.Show("يجب تحديد الحساب البنكي عند اختيار التسوية المصرفية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            await _ownerDebtService.RecordSettlementAsync(
                SelectedDebtForSettlement?.Id,
                SettlementPartnerName,
                SettlementAmount,
                source,
                source == OwnerDebtSettlementSource.Bank ? SettlementBankAccount?.Id : null,
                SettlementNotes,
                SettlementDate);

            Dialogs.Show("تم حفظ وإتمام تسوية الشريك بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsSettlementFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء إجراء التسوية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteDebtAsync(OwnerDebt? debt)
    {
        if (debt == null) return;

        var result = Dialogs.Show(
            $"هل أنت متأكد من حذف الدين المسجل للشريك '{debt.PartnerName}' بقيمة {debt.Amount:N2} د.ل؟",
            "تأكيد الحذف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.No) return;

        IsLoading = true;
        try
        {
            // حذف الحركة البنكية المرتبطة إن وجدت
            if (debt.SourceType == "Bank")
            {
                // نحاول حذف جميع احتمالات SourceType لضمان التنظيف الكامل
                await _bankService.DeleteTransactionBySourceAsync("OwnerDebt", debt.Id);
                await _bankService.DeleteTransactionBySourceAsync("OwnerDebt_Transfer", debt.Id);
                await _bankService.DeleteTransactionBySourceAsync("OwnerDebt_Cash", debt.Id);
            }

            await _ownerDebtService.DeleteDebtAsync(debt.Id);
            Dialogs.Show("تم حذف قيد الدين بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء حذف الدين:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteSettlementAsync(OwnerDebtSettlement? settlement)
    {
        if (settlement == null) return;

        var result = Dialogs.Show(
            $"هل أنت متأكد من حذف حركة التسوية المسجلة للشريك '{settlement.PartnerName}' بقيمة {settlement.Amount:N2} د.ل؟\nسيؤدي ذلك إلى إعادة الدين للحالة 'غير مسدد' وإلغاء الحركة البنكية المرتبطة به تلقائياً في حال كانت الدفعة مصرفية.",
            "تأكيد الحذف والالغاء",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.No) return;

        IsLoading = true;
        try
        {
            await _ownerDebtService.DeleteSettlementAsync(settlement.Id);
            Dialogs.Show("تم حذف حركة التسوية وإلغاء آثارها بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء إلغاء التسوية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
