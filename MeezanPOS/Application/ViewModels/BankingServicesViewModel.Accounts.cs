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

/// <summary>إدارة الحسابات المصرفية، الإيداع والسحب والتحويل اليدوي، ومطابقة الكروت.</summary>
public partial class BankingServicesViewModel
{
    // --- عمليات إدارة الحسابات البنكية ---

    [RelayCommand]
    public void OpenAddAccount()
    {
        IsEditMode = false;
        EditingAccount = new BankAccount
        {
            FriendlyName = string.Empty,
            LegalOwnerName = string.Empty,
            AccountNumber = string.Empty,
            AccountType = BankAccountType.Commercial,
            OpeningBalance = 0,
            IsActive = true
        };
        IsAccountFormOpen = true;
    }

    [RelayCommand]
    public void OpenEditAccount()
    {
        if (SelectedAccount == null)
        {
            Dialogs.Show("الرجاء اختيار الحساب المطلوب تعديله أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsEditMode = true;
        EditingAccount = new BankAccount
        {
            Id = SelectedAccount.Id,
            FriendlyName = SelectedAccount.FriendlyName,
            LegalOwnerName = SelectedAccount.LegalOwnerName,
            AccountNumber = SelectedAccount.AccountNumber,
            AccountType = SelectedAccount.AccountType,
            OpeningBalance = SelectedAccount.OpeningBalance,
            CurrentBalance = SelectedAccount.CurrentBalance,
            IsActive = SelectedAccount.IsActive
        };
        IsAccountFormOpen = true;
    }

    [RelayCommand]
    public void CloseAccountForm()
    {
        IsAccountFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingAccount.FriendlyName))
        {
            Dialogs.Show("يجب إدخال اسم المصرف والفرع.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            if (IsEditMode)
            {
                await _bankService.UpdateAccountAsync(EditingAccount);
                Dialogs.Show("تم تعديل الحساب البنكي بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                await _bankService.CreateAccountAsync(EditingAccount);
                Dialogs.Show("تم إنشاء الحساب البنكي الجديد بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            IsAccountFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء حفظ الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DeleteAccountAsync()
    {
        if (SelectedAccount == null)
        {
            Dialogs.Show("الرجاء اختيار الحساب المطلوب حذفه أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = Dialogs.Show(
            $"هل أنت متأكد من حذف الحساب البنكي '{SelectedAccount.FriendlyName}'؟\nسيتم إخفاؤه من القوائم دون التأثير على الحركات التاريخية الموثقة برمجياً.",
            "تأكيد الحذف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result == MessageBoxResult.No) return;

        IsLoading = true;
        try
        {
            await _bankService.DeleteAccountAsync(SelectedAccount.Id);
            Dialogs.Show("تم حذف الحساب البنكي بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            SelectedAccount = null;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء حذف الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // --- عمليات الإيداع والسحب والتحويل اليدوي ---

    [RelayCommand]
    public void OpenDepositForm()
    {
        if (SelectedAccount == null)
        {
            Dialogs.Show("الرجاء تحديد الحساب البنكي المستهدف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualTxAmount = 0;
        ManualTxReference = string.Empty;
        ManualTxNotes = "إيداع يدوي";
        ManualTxDate = DateTime.Now;
        IsDepositFormOpen = true;
    }

    [RelayCommand]
    public void CloseDepositForm()
    {
        IsDepositFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveDepositAsync()
    {
        if (SelectedAccount == null || ManualTxAmount <= 0)
        {
            Dialogs.Show("الرجاء إدخال قيمة صحيحة للإيداع.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            await _bankService.RecordDepositAsync(
                SelectedAccount.Id,
                ManualTxAmount,
                ManualTxReference,
                ManualTxNotes,
                ManualTxDate);

            Dialogs.Show("تم تسجيل عملية الإيداع بنجاح في كشف حساب المصرف.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsDepositFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تسجيل الإيداع:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenWithdrawalForm()
    {
        if (SelectedAccount == null)
        {
            Dialogs.Show("الرجاء تحديد الحساب البنكي المستهدف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ManualTxAmount = 0;
        ManualTxReference = string.Empty;
        ManualTxNotes = "سحب يدوي لتغذية النقدية";
        ManualTxDate = DateTime.Now;
        IsWithdrawalFormOpen = true;
    }

    [RelayCommand]
    public void CloseWithdrawalForm()
    {
        IsWithdrawalFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveWithdrawalAsync()
    {
        if (SelectedAccount == null || ManualTxAmount <= 0)
        {
            Dialogs.Show("الرجاء إدخال قيمة صحيحة للسحب.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            await _bankService.RecordWithdrawalAsync(
                SelectedAccount.Id,
                ManualTxAmount,
                ManualTxReference,
                ManualTxNotes,
                ManualTxDate);

            Dialogs.Show("تم تسجيل عملية السحب بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsWithdrawalFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء تسجيل السحب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenTransferForm()
    {
        if (SelectedAccount == null)
        {
            Dialogs.Show("الرجاء تحديد الحساب المصدر أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TransferDestinationAccounts = new ObservableCollection<BankAccount>(
            BankAccounts.Where(a => a.Id != SelectedAccount.Id && a.IsActive));

        if (!TransferDestinationAccounts.Any())
        {
            Dialogs.Show("لا يوجد حسابات بنكية نشطة أخرى لتحويل الأموال إليها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TransferDestinationAccount = TransferDestinationAccounts[0];
        ManualTxAmount = 0;
        ManualTxNotes = $"تحويل بين الحسابات المصرفية";
        ManualTxDate = DateTime.Now;
        IsTransferFormOpen = true;
    }

    [RelayCommand]
    public void CloseTransferForm()
    {
        IsTransferFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveTransferAsync()
    {
        if (SelectedAccount == null || TransferDestinationAccount == null || ManualTxAmount <= 0)
        {
            Dialogs.Show("الرجاء التأكد من صحة الحساب المستلم والمبلغ.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            await _bankService.RecordInternalTransferAsync(
                SelectedAccount.Id,
                TransferDestinationAccount.Id,
                ManualTxAmount,
                ManualTxNotes,
                ManualTxDate);

            Dialogs.Show("تم إجراء التحويل الداخلي وتحديث الحسابين بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsTransferFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء معالجة التحويل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // --- تبويب 3: مطابقة الكروت ---

    [RelayCommand]
    public void OpenClearCardForm(CardPaymentReconciliation? item)
    {
        if (item == null) return;

        SelectedPendingCardPayment = item;
        ClearingBankAccount = BankAccounts.FirstOrDefault(a => a.IsActive);
        ClearCardDate = DateTime.Now;
        IsClearCardFormOpen = true;
    }

    [RelayCommand]
    public void CloseClearCardForm()
    {
        IsClearCardFormOpen = false;
    }

    [RelayCommand]
    public async Task ClearCardPaymentAsync()
    {
        if (SelectedPendingCardPayment == null || ClearingBankAccount == null)
        {
            Dialogs.Show("يجب تحديد الحساب البنكي لتأكيد استلام المبلغ.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            await _bankService.ClearCardPaymentAsync(
                SelectedPendingCardPayment.Id,
                ClearingBankAccount.Id,
                ClearCardDate);

            Dialogs.Show("تم تأكيد تحصيل العملية وإيداع المبلغ في الحساب بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsClearCardFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ أثناء مطابقة البطاقة الكروت:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
