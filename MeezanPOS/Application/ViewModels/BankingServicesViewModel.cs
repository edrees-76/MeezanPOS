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

public partial class BankingServicesViewModel : ObservableObject
{
    private readonly IBankService _bankService;
    private readonly IOwnerDebtService _ownerDebtService;

    public string[] LibyanBanks { get; } = {
        "مصرف الجمهورية",
        "المصرف التجاري الوطني",
        "مصرف الوحدة",
        "مصرف الصحارى",
        "مصرف التجارة والتنمية",
        "مصرف الأمان",
        "مصرف الأندلس",
        "مصرف اليقين",
        "المصرف الإسلامي الليبي",
        "مصرف الاتحاد الوطني",
        "مصرف الواحة",
        "مصرف النوران",
        "مصرف السراي للتجارة والاستثمار"
    };

    [ObservableProperty]
    private bool isLoading;

    // --- تبويب 1: الحسابات البنكية ---
    [ObservableProperty]
    private ObservableCollection<BankAccount> bankAccounts = new();

    [ObservableProperty]
    private BankAccount? selectedAccount;


    // نموذج إضافة/تعديل حساب بنكي
    [ObservableProperty]
    private bool isAccountFormOpen;

    [ObservableProperty]
    private BankAccount editingAccount = new();

    [ObservableProperty]
    private bool isEditMode;

    // نماذج الإيداع والسحب والتحويل
    [ObservableProperty]
    private bool isDepositFormOpen;

    [ObservableProperty]
    private decimal manualTxAmount;

    [ObservableProperty]
    private string manualTxReference = string.Empty;

    [ObservableProperty]
    private string manualTxNotes = string.Empty;

    [ObservableProperty]
    private DateTime manualTxDate = DateTime.Now;

    [ObservableProperty]
    private bool isWithdrawalFormOpen;

    [ObservableProperty]
    private bool isTransferFormOpen;

    [ObservableProperty]
    private BankAccount? transferDestinationAccount;

    [ObservableProperty]
    private ObservableCollection<BankAccount> transferDestinationAccounts = new();

    // --- تبويب 2: سجل الحركات الموحد التاريخي ---
    [ObservableProperty]
    private ObservableCollection<BankTransaction> transactions = new();

    [ObservableProperty]
    private DateTime ledgerStartDate = DateTime.Now.AddMonths(-1);

    [ObservableProperty]
    private DateTime ledgerEndDate = DateTime.Now;

    // --- تبويب كشف حساب المصرف البنكي ---
    [ObservableProperty]
    private BankAccount? statementAccount;

    [ObservableProperty]
    private DateTime statementStartDate = DateTime.Now.AddMonths(-1);

    [ObservableProperty]
    private DateTime statementEndDate = DateTime.Now;

    [ObservableProperty]
    private string selectedTransactionTypeFilter = "الكل";

    public string[] TransactionTypeFilters { get; } = {
        "الكل",
        "خدمات مصرفية",
        "إيداع نقدي",
        "سحب نقدي",
        "سداد مورد",
        "مصروف عام",
        "تحويل داخلي",
        "تحويل مصرفي"
    };

    [ObservableProperty]
    private string statementSearchText = string.Empty;

    [ObservableProperty]
    private bool showPersonalTransactions;

    [ObservableProperty]
    private decimal statementOpeningBalance;

    [ObservableProperty]
    private decimal statementTotalDeposits;

    [ObservableProperty]
    private decimal statementTotalWithdrawals;

    [ObservableProperty]
    private decimal statementEndingBalance;

    [ObservableProperty]
    private ObservableCollection<BankTransaction> statementTransactions = new();

    [ObservableProperty]
    private bool isStatementAccountMixed;

    // --- تبويب 3: مطابقة مبيعات البطاقات الإلكترونية ---
    [ObservableProperty]
    private ObservableCollection<CardPaymentReconciliation> pendingCardPayments = new();

    [ObservableProperty]
    private CardPaymentReconciliation? selectedPendingCardPayment;

    [ObservableProperty]
    private bool isClearCardFormOpen;

    [ObservableProperty]
    private BankAccount? clearingBankAccount;

    [ObservableProperty]
    private DateTime clearCardDate = DateTime.Now;

    // --- تبويب 4: مستحقات وديون الشركاء والمالك ---
    [ObservableProperty]
    private ObservableCollection<OwnerDebt> ownerDebts = new();

    [ObservableProperty]
    private ObservableCollection<OwnerDebtSettlement> ownerSettlements = new();

    [ObservableProperty]
    private string? filterPartnerName;

    [ObservableProperty]
    private ObservableCollection<string> partnerNames = new();

    [ObservableProperty]
    private decimal totalOwnerDebts;

    [ObservableProperty]
    private decimal totalOwnerSettlements;

    [ObservableProperty]
    private decimal netOwnerBalance;

    [ObservableProperty]
    private PartnerDebtsDashboardViewModel partnerDebtsDashboard;

    // نموذج تسوية ديون المالك
    [ObservableProperty]
    private bool isSettlementFormOpen;

    [ObservableProperty]
    private OwnerDebt? selectedDebtForSettlement;

    [ObservableProperty]
    private string settlementPartnerName = string.Empty;

    [ObservableProperty]
    private decimal settlementAmount;

    [ObservableProperty]
    private string selectedSettlementSource = "CashRegister"; // CashRegister, Bank, PettyCash

    [ObservableProperty]
    private BankAccount? settlementBankAccount;

    [ObservableProperty]
    private string settlementNotes = string.Empty;

    [ObservableProperty]
    private DateTime settlementDate = DateTime.Now;

    // نموذج تسجيل تمويل جديد (Debt)
    [ObservableProperty]
    private bool isDebtFormOpen;

    [ObservableProperty]
    private string debtPartnerName = string.Empty;

    [ObservableProperty]
    private decimal debtAmount;

    [ObservableProperty]
    private int selectedDebtDestinationIndex = 0; // 0=CashRegister, 1=Bank, 2=PettyCash

    [ObservableProperty]
    private BankAccount? debtBankAccount;

    [ObservableProperty]
    private string debtNotes = string.Empty;

    [ObservableProperty]
    private string debtTransferReference = string.Empty;

    [ObservableProperty]
    private int selectedDebtPaymentMethodIndex = 0; // 0=نقدي، 1=تحويل مصرفي

    [ObservableProperty]
    private DateTime debtDate = DateTime.Now;

    public BankingServicesViewModel()
    {
        _bankService = AppServiceProvider.Resolve<IBankService>();
        _ownerDebtService = AppServiceProvider.Resolve<IOwnerDebtService>();

        PartnerDebtsDashboard = new PartnerDebtsDashboardViewModel(_ownerDebtService);

        WeakReferenceMessenger.Default.Register<OpenPartnerDebtFormMessage>(this, (r, m) => OpenDebtForm());
        WeakReferenceMessenger.Default.Register<OpenPartnerSettlementFormMessage>(this, (r, m) => OpenSettlementForm());

        _ = LoadDataAsync();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            // 1. جلب الحسابات
            var accountsList = await _bankService.GetAllAccountsAsync();
            BankAccounts = new ObservableCollection<BankAccount>(accountsList);

            // 2. تحديث الحركات للحساب المحدد
            if (SelectedAccount != null)
            {
                await LoadTransactionsAsync();
            }
            else
            {
                Transactions.Clear();
            }

            // 3. جلب معلق الكروت
            var cardPaymentsList = await _bankService.GetPendingCardPaymentsAsync();
            PendingCardPayments = new ObservableCollection<CardPaymentReconciliation>(cardPaymentsList);

            // 4. جلب شركاء الديون لتغذية الفلتر والمدخلات
            var names = await _ownerDebtService.GetPartnerNamesAsync();
            PartnerNames = new ObservableCollection<string>(names);

            // 5. جلب ديون وتسويات المالك
            await LoadOwnerDebtDataAsync();

            // 6. تحديث لوحة الشركاء الجديدة
            await PartnerDebtsDashboard.LoadDashboardAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل البيانات المصرفية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadTransactionsAsync()
    {
        if (SelectedAccount == null) return;

        try
        {
            var txsList = await _bankService.GetTransactionsAsync(SelectedAccount.Id, LedgerStartDate, LedgerEndDate);
            Transactions = new ObservableCollection<BankTransaction>(txsList.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل كشف الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task LoadOwnerDebtDataAsync()
    {
        try
        {
            var debts = await _ownerDebtService.GetDebtsAsync(FilterPartnerName);
            OwnerDebts = new ObservableCollection<OwnerDebt>(debts);

            var settlements = await _ownerDebtService.GetSettlementsAsync(FilterPartnerName);
            OwnerSettlements = new ObservableCollection<OwnerDebtSettlement>(settlements);

            TotalOwnerDebts = await _ownerDebtService.GetTotalOwnerDebtsAsync(FilterPartnerName);
            TotalOwnerSettlements = await _ownerDebtService.GetTotalOwnerSettlementsAsync(FilterPartnerName);
            NetOwnerBalance = await _ownerDebtService.GetNetOwnerBalanceAsync(FilterPartnerName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل ديون المالك:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    partial void OnSelectedAccountChanged(BankAccount? value)
    {
        _ = UpdateSelectedAccountShareAndLedgerAsync();
    }

    private async Task UpdateSelectedAccountShareAndLedgerAsync()
    {
        if (SelectedAccount != null)
        {
            await LoadTransactionsAsync();
        }
        else
        {
            Transactions.Clear();
        }
    }

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
            MessageBox.Show("الرجاء اختيار الحساب المطلوب تعديله أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("يجب إدخال اسم المصرف والفرع.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            if (IsEditMode)
            {
                await _bankService.UpdateAccountAsync(EditingAccount);
                MessageBox.Show("تم تعديل الحساب البنكي بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                await _bankService.CreateAccountAsync(EditingAccount);
                MessageBox.Show("تم إنشاء الحساب البنكي الجديد بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            IsAccountFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حفظ الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("الرجاء اختيار الحساب المطلوب حذفه أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
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
            MessageBox.Show("تم حذف الحساب البنكي بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            SelectedAccount = null;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حذف الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("الرجاء تحديد الحساب البنكي المستهدف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("الرجاء إدخال قيمة صحيحة للإيداع.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MessageBox.Show("تم تسجيل عملية الإيداع بنجاح في كشف حساب المصرف.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsDepositFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تسجيل الإيداع:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("الرجاء تحديد الحساب البنكي المستهدف أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("الرجاء إدخال قيمة صحيحة للسحب.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MessageBox.Show("تم تسجيل عملية السحب بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsWithdrawalFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تسجيل السحب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("الرجاء تحديد الحساب المصدر أولاً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TransferDestinationAccounts = new ObservableCollection<BankAccount>(
            BankAccounts.Where(a => a.Id != SelectedAccount.Id && a.IsActive));

        if (!TransferDestinationAccounts.Any())
        {
            MessageBox.Show("لا يوجد حسابات بنكية نشطة أخرى لتحويل الأموال إليها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("الرجاء التأكد من صحة الحساب المستلم والمبلغ.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MessageBox.Show("تم إجراء التحويل الداخلي وتحديث الحسابين بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsTransferFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء معالجة التحويل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("يجب تحديد الحساب البنكي لتأكيد استلام المبلغ.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            await _bankService.ClearCardPaymentAsync(
                SelectedPendingCardPayment.Id,
                ClearingBankAccount.Id,
                ClearCardDate);

            MessageBox.Show("تم تأكيد تحصيل العملية وإيداع المبلغ في الحساب بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsClearCardFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء مطابقة البطاقة الكروت:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

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
            MessageBox.Show("يرجى إدخال اسم الشريك والمبلغ بشكل صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool isBankDestination = (SelectedDebtDestinationIndex == 1);

        if (isBankDestination && DebtBankAccount == null)
        {
            MessageBox.Show("يجب تحديد الحساب البنكي المستلم للتمويل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show("يرجى إدخال أخر 4 أرقام من عملية التحويل المصرفي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MessageBox.Show("تم حفظ بيانات تمويل الشريك بنجاح.", "نجاح العملية", MessageBoxButton.OK, MessageBoxImage.Information);
            IsDebtFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تسجيل التمويل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("يرجى إدخال اسم الشريك والمبلغ بشكل صحيح.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("يجب تحديد الحساب البنكي عند اختيار التسوية المصرفية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            MessageBox.Show("تم حفظ وإتمام تسوية الشريك بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            IsSettlementFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء إجراء التسوية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

        var result = MessageBox.Show(
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
            MessageBox.Show("تم حذف قيد الدين بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حذف الدين:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

        var result = MessageBox.Show(
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
            MessageBox.Show("تم حذف حركة التسوية وإلغاء آثارها بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء إلغاء التسوية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

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
            MessageBox.Show($"حدث خطأ أثناء تحميل كشف الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show("يرجى اختيار حساب وتحميل كشف الحساب أولاً قبل التصدير.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show($"حدث خطأ أثناء تصدير التقرير:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- تفاصيل حركات الخدمات المصرفية اليومية للورديات ---
    private List<BankingItemDetailDto> _allTransactionDetails = new();

    [ObservableProperty]
    private ObservableCollection<BankingItemDetailDto> selectedTransactionDetails = new();

    [ObservableProperty]
    private DateTime selectedTransactionDate;

    [ObservableProperty]
    private decimal selectedTransactionTotalAmount;

    [ObservableProperty]
    private bool hasNoDetails;

    [ObservableProperty]
    private ObservableCollection<string> cashierFilters = new();

    [ObservableProperty]
    private ObservableCollection<string> shiftFilters = new();

    [ObservableProperty]
    private string selectedCashierFilter = "الكل";

    [ObservableProperty]
    private string selectedShiftFilter = "الكل";

    public class ShiftTotalDto
    {
        public string ShiftName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
    }

    [ObservableProperty]
    private ObservableCollection<ShiftTotalDto> shiftTotals = new();

    partial void OnSelectedCashierFilterChanged(string value) => ApplyFilters();
    partial void OnSelectedShiftFilterChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = _allTransactionDetails.AsEnumerable();

        if (SelectedCashierFilter != "الكل" && !string.IsNullOrEmpty(SelectedCashierFilter))
        {
            filtered = filtered.Where(x => x.CashierName == SelectedCashierFilter);
        }

        if (SelectedShiftFilter != "الكل" && !string.IsNullOrEmpty(SelectedShiftFilter))
        {
            filtered = filtered.Where(x => x.ShiftName == SelectedShiftFilter);
        }

        SelectedTransactionDetails = new ObservableCollection<BankingItemDetailDto>(filtered);

        // احتساب إجماليات الورديات الحالية المعروضة
        var totals = SelectedTransactionDetails
            .GroupBy(x => x.ShiftName)
            .Select(g => new ShiftTotalDto
            {
                ShiftName = g.Key,
                TotalAmount = g.Sum(x => x.Amount)
            })
            .ToList();

        ShiftTotals = new ObservableCollection<ShiftTotalDto>(totals);
    }

    [RelayCommand]
    public async Task LoadTransactionDetailsAsync(BankTransaction? transaction)
    {
        if (transaction == null || transaction.Type != BankTransactionType.CardSalesDeposit)
        {
            _allTransactionDetails.Clear();
            SelectedTransactionDetails.Clear();
            SelectedTransactionTotalAmount = 0;
            HasNoDetails = true;
            CashierFilters.Clear();
            ShiftFilters.Clear();
            ShiftTotals.Clear();
            return;
        }

        SelectedTransactionDate = transaction.TransactionDate;
        SelectedTransactionTotalAmount = transaction.Amount;
        SelectedTransactionDetails.Clear();
        HasNoDetails = false;

        IsLoading = true;
        try
        {
            var targetDate = transaction.TransactionDate.Date;
            var bankAccountId = transaction.BankAccountId;

            // جلب بنود المعاملات البنكية التفصيلية من الورديات لنفس اليوم ولنفس الحساب
            var details = await Task.Run(() =>
            {
                using var context = new AppDbContext();
                return context.BankingItems
                    .Include(b => b.DailyJournal)
                    .Where(b => b.BankAccountId == bankAccountId 
                             && !b.IsDeleted 
                             && b.DailyJournal != null 
                             && b.DailyJournal.JournalDate.Date == targetDate 
                             && !b.DailyJournal.IsDeleted)
                    .Select(b => new BankingItemDetailDto
                    {
                        Id = b.Id,
                        CashierName = b.DailyJournal!.EmployeeName,
                        ShiftName = b.DailyJournal!.ShiftType == ShiftType.FirstShift ? "الوردية الأولى" : b.DailyJournal!.ShiftType == ShiftType.SecondShift ? "الوردية الثانية" : "يوم كامل",
                        Amount = b.Amount,
                        InvoiceNumber = b.Description ?? "غير محدد",
                        TransferReference = b.ReferenceNumber ?? "غير محدد",
                        IsReconciled = b.IsReconciled
                    })
                    .ToList();
            });

            _allTransactionDetails = details;

            // ملء قائمة خيارات الكاشيرز
            var cashiers = new List<string> { "الكل" };
            cashiers.AddRange(details.Select(x => x.CashierName).Distinct().OrderBy(x => x));
            CashierFilters = new ObservableCollection<string>(cashiers);

            // ملء قائمة خيارات الورديات
            var shifts = new List<string> { "الكل" };
            shifts.AddRange(details.Select(x => x.ShiftName).Distinct().OrderBy(x => x));
            ShiftFilters = new ObservableCollection<string>(shifts);

            // إعادة تعيين الفلاتر الافتراضية
            SelectedCashierFilter = "الكل";
            SelectedShiftFilter = "الكل";
            OnPropertyChanged(nameof(SelectedCashierFilter));
            OnPropertyChanged(nameof(SelectedShiftFilter));

            ApplyFilters();

            HasNoDetails = !details.Any();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل تفاصيل الوردية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleReconciliationAsync(BankingItemDetailDto? item)
    {
        if (item == null) return;
        try
        {
            await Task.Run(() =>
            {
                using var context = new AppDbContext();
                var dbItem = context.BankingItems.FirstOrDefault(b => b.Id == item.Id);
                if (dbItem != null)
                {
                    dbItem.IsReconciled = item.IsReconciled;
                    dbItem.UpdatedAt = DateTime.UtcNow;
                    context.SaveChanges();
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحديث حالة المطابقة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void PrintDetailsPdf()
    {
        if (SelectedTransactionDetails == null || !SelectedTransactionDetails.Any())
        {
            MessageBox.Show("لا توجد حركات لطباعتها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var report = new BankingServicesDetailsPdfReport(
                SelectedTransactionDate,
                StatementAccount,
                SelectedTransactionDetails.ToList(),
                ShiftTotals.ToList()
            );

            string bankName = StatementAccount?.DisplayName ?? "حساب بنكي";
            // تنظيف الحروف غير الصالحة لأسماء الملفات في ويندوز
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                bankName = bankName.Replace(c, '_');
            }

            string dateStr = SelectedTransactionDate.ToString("yyyy-MM-dd");
            string timeStr = DateTime.Now.ToString("HH_mm_ss");
            string fileName = $"{bankName} - {dateStr} - {timeStr}.pdf";
            
            var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            report.GeneratePdf(filePath);
            
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء طباعة تقرير التفاصيل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedCashierFilter = "الكل";
        SelectedShiftFilter = "الكل";
    }

    private static bool IsStatementDepositType(BankTransaction tx) =>
        tx.Type == BankTransactionType.CardSalesDeposit ||
        tx.Type == BankTransactionType.Deposit;

    private static string GetStatementTypeDisplayName(BankTransactionType type) => type switch
    {
        BankTransactionType.CardSalesDeposit => "خدمات مصرفية",
        BankTransactionType.Deposit => "إيداع نقدي",
        BankTransactionType.Withdrawal => "سحب نقدي",
        BankTransactionType.InternalTransfer => "تحويل داخلي",
        BankTransactionType.SupplierPayment => "سداد مورد",
        BankTransactionType.ExpensePayment => "مصروف عام",
        BankTransactionType.OwnerDebtSettlement => "تسوية مالك",
        BankTransactionType.ExchangeDifference => "فروقات",
        _ => "أخرى"
    };
}
