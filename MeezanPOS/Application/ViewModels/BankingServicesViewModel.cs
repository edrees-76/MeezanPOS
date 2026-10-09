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

    /// <summary>السحب يدخل نقدية المطعم (الخزينة) فيُسجل وارداً في دفتر النقدية.</summary>
    [ObservableProperty]
    private bool withdrawToRestaurantCash = true;

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

    private readonly MeezanPOS.Application.Services.Queries.IBankingQueryService _bankingQueries = new MeezanPOS.Application.Services.Queries.BankingQueryService();

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
            Dialogs.Show($"حدث خطأ أثناء تحميل البيانات المصرفية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Dialogs.Show($"حدث خطأ أثناء تحميل كشف الحساب:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
            Dialogs.Show($"حدث خطأ أثناء تحميل ديون المالك:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

}
