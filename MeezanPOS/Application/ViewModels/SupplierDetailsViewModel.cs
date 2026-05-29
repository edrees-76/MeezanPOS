using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.ViewModels;

public partial class UnifiedLedgerRow : ObservableObject
{
    [ObservableProperty] private int sequence;
    [ObservableProperty] private decimal openingBalance;
    
    // Invoice side
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvoice))]
    private decimal? invoiceAmount;

    [ObservableProperty] private DateTime? invoiceDate;
    [ObservableProperty] private string invoiceNumber = string.Empty;
    
    // Payment side
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPayment))]
    private decimal? paymentAmount;

    [ObservableProperty] private DateTime? paymentDate;
    [ObservableProperty] private string paymentMethod = string.Empty;
    [ObservableProperty] private string receiptNumber = string.Empty;
    
    [ObservableProperty] private decimal remainingBalance;
    [ObservableProperty] private string notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDailyJournalPayment))]
    [NotifyPropertyChangedFor(nameof(IsTransferPayment))]
    [NotifyPropertyChangedFor(nameof(IsPlainPayment))]
    private TransactionSourceType sourceType;

    [ObservableProperty] private int sourceId;
    [ObservableProperty] private int transactionId;

    [ObservableProperty] private bool isTransferPayment;

    public bool IsDailyJournalPayment => SourceType == TransactionSourceType.DailyJournalPayment;
    public bool IsPlainPayment => !IsDailyJournalPayment && !IsTransferPayment;

    public bool HasInvoice => InvoiceAmount.HasValue && InvoiceAmount.Value > 0;
    public bool HasPayment => PaymentAmount.HasValue && PaymentAmount.Value > 0;
}

public partial class SupplierDetailsViewModel : ObservableObject
{
    private int? _editingInvoiceId;
    private int? _editingPaymentTransactionId;
    
    public int SupplierId { get; }
    
    [ObservableProperty]
    private string supplierName;

    [ObservableProperty]
    private decimal currentBalance;

    [ObservableProperty]
    private ObservableCollection<SupplierInvoice> invoices = new();

    [ObservableProperty]
    private ObservableCollection<SupplierTransaction> transactions = new();

    [ObservableProperty]
    private ObservableCollection<UnifiedLedgerRow> unifiedLedger = new();

    // Summaries
    [ObservableProperty] private decimal totalInvoicesSum;
    [ObservableProperty] private decimal totalPaymentsSum;
    [ObservableProperty] private decimal totalOpeningBalance;
    [ObservableProperty] private int totalTransactionsCount;

    [ObservableProperty]
    private bool isLoading;

    public Action? OnBack { get; set; }

    // --- نافذة خيارات الطباعة ---
    [ObservableProperty]
    private bool isPrintSelectionOpen;

    // --- نافذة إضافة فاتورة ---
    [ObservableProperty]
    private bool isInvoiceFormOpen;

    [ObservableProperty]
    private SupplierInvoice newInvoice = new();

    // --- نافذة تفاصيل الفاتورة (الأصناف) ---
    [ObservableProperty]
    private bool isInvoiceDetailsOpen;

    [ObservableProperty]
    private ObservableCollection<InvoiceItemViewModel> invoiceItems = new();

    [ObservableProperty]
    private decimal invoiceItemsTotal;

    [ObservableProperty]
    private bool isTotalMatching;

    [ObservableProperty]
    private string matchingStatusText = string.Empty;

    private void RecalculateItemsTotal()
    {
        InvoiceItemsTotal = InvoiceItems.Sum(i => i.TotalValue);
        IsTotalMatching = NewInvoice.TotalAmount > 0 && InvoiceItemsTotal == NewInvoice.TotalAmount;
        if (NewInvoice.TotalAmount <= 0)
            MatchingStatusText = "أدخل إجمالي الفاتورة أولاً";
        else if (IsTotalMatching)
            MatchingStatusText = "✅ الإجمالي متطابق!";
        else
        {
            var diff = NewInvoice.TotalAmount - InvoiceItemsTotal;
            MatchingStatusText = $"⚠️ الفرق: {diff:N2}";
        }
    }

    // --- نافذة الدفع ---
    [ObservableProperty]
    private bool isPaymentFormOpen;

    [ObservableProperty]
    private decimal paymentAmount;

    [ObservableProperty]
    private DateTime paymentDate = DateTime.Now;

    [ObservableProperty]
    private string receiptNumber = string.Empty;

    [ObservableProperty]
    private string selectedPaymentMethod = "نقدي";

    // --- قائمة المصارف الليبية ---
    public string[] BankNames { get; } = {
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
    private string bankName = string.Empty;

    [ObservableProperty]
    private string transferLast4 = string.Empty;

    [ObservableProperty]
    private string paymentDescription = string.Empty;

    [ObservableProperty]
    private SupplierInvoice? selectedInvoiceForPayment;

    [ObservableProperty]
    private DateTime? _filterStartDate;

    [ObservableProperty]
    private DateTime? _filterEndDate;

    [ObservableProperty]
    private ObservableCollection<BankAccount> bankAccounts = new();

    [ObservableProperty]
    private BankAccount? selectedBankAccountForPayment;

    [ObservableProperty]
    private string partnerNameForPayment = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> partnerNames = new();

    public SupplierDetailsViewModel(int supplierId, string supplierName)
    {
        SupplierId = supplierId;
        SupplierName = supplierName;
        
        _ = LoadDataAsync();
        _ = LoadBankAccountsAndPartnersAsync();
    }

    private async Task LoadBankAccountsAndPartnersAsync()
    {
        try
        {
            using var context = new AppDbContext();
            var bankService = new BankService(context);
            var accountsList = await bankService.GetAllAccountsAsync();
            
            var ownerDebtService = new OwnerDebtService(context, bankService);
            var namesList = await ownerDebtService.GetPartnerNamesAsync();

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                BankAccounts = new ObservableCollection<BankAccount>(accountsList.Where(a => a.IsActive));
                PartnerNames = new ObservableCollection<string>(namesList);
                
                if (BankAccounts.Any())
                    SelectedBankAccountForPayment = BankAccounts.First();
                if (PartnerNames.Any())
                    PartnerNameForPayment = PartnerNames.First();
            });
        }
        catch { /* تجاهل الأخطاء الصامتة */ }
    }

    [RelayCommand]
    public void GoBack()
    {
        OnBack?.Invoke();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            using var context = new AppDbContext();
            var ledgerService = new LedgerService(context);

            var supplier = await context.Suppliers.FindAsync(SupplierId);
            if (supplier != null) CurrentBalance = supplier.CurrentBalance;

            var invoicesList = await context.SupplierInvoices
                .Where(i => i.SupplierId == SupplierId && !i.IsDeleted)
                .OrderByDescending(i => i.InvoiceDate)
                .ThenByDescending(i => i.Id)
                .ToListAsync();
            Invoices = new ObservableCollection<SupplierInvoice>(invoicesList);

            var transactionsList = await ledgerService.GetStatementAsync(SupplierId, DateTime.MinValue, DateTime.MaxValue);
            Transactions = new ObservableCollection<SupplierTransaction>(transactionsList.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id));

            // Generate Unified Ledger
            GenerateUnifiedLedger(supplier?.OpeningBalance ?? 0, transactionsList.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).ToList());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل البيانات:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void FilterStatement()
    {
        using var context = new AppDbContext();
        if (context.Suppliers.Find(SupplierId) is var supplier)
        {
            var allTransactions = Transactions.OrderBy(t => t.TransactionDate).ThenBy(t => t.Id).ToList();
            GenerateUnifiedLedger(supplier?.OpeningBalance ?? 0, allTransactions);
        }
    }

    [RelayCommand]
    public void ClearFilter()
    {
        FilterStartDate = null;
        FilterEndDate = null;
        FilterStatement();
    }

    private void GenerateUnifiedLedger(decimal openingBal, System.Collections.Generic.List<SupplierTransaction> sortedTransactions)
    {
        UnifiedLedger.Clear();
        decimal currentBal = openingBal;
        
        // حساب الرصيد المتراكم للحركات السابقة قبل تاريخ الفلتر
        var pastTransactions = sortedTransactions.Where(t => FilterStartDate.HasValue && t.TransactionDate.Date < FilterStartDate.Value.Date).ToList();
        foreach(var pt in pastTransactions)
        {
            if (pt.Type == SupplierTransactionType.IncreaseDebt) currentBal += pt.Amount;
            if (pt.Type == SupplierTransactionType.DecreaseDebt) currentBal -= pt.Amount;
        }

        TotalOpeningBalance = currentBal; // رصيد أول للفترة المعروضة
        decimal sumInvoices = 0;
        decimal sumPayments = 0;
        int seq = 1;

        // الحركات التي سيتم عرضها بناءً على الفلتر
        var displayTransactions = sortedTransactions.Where(t => 
            (!FilterStartDate.HasValue || t.TransactionDate.Date >= FilterStartDate.Value.Date) &&
            (!FilterEndDate.HasValue || t.TransactionDate.Date <= FilterEndDate.Value.Date)
        ).ToList();

        foreach (var trans in displayTransactions)
        {
            var row = new UnifiedLedgerRow
            {
                Sequence = seq++,
                OpeningBalance = currentBal,
                SourceType = trans.SourceType,
                SourceId = trans.SourceId,
                TransactionId = trans.Id
            };

            if (trans.Type == SupplierTransactionType.IncreaseDebt)
            {
                row.InvoiceAmount = trans.Amount;
                row.InvoiceDate = trans.TransactionDate;
                if (trans.SupplierInvoice != null)
                {
                    row.InvoiceNumber = trans.SupplierInvoice.InvoiceNumber ?? trans.SupplierInvoice.Id.ToString();
                    row.Notes = trans.SupplierInvoice.Notes ?? string.Empty;
                }
                currentBal += trans.Amount;
                sumInvoices += trans.Amount;
            }
            else if (trans.Type == SupplierTransactionType.DecreaseDebt)
            {
                row.PaymentAmount = trans.Amount;
                row.PaymentDate = trans.TransactionDate;
                
                // Try to infer payment method/receipt
                row.PaymentMethod = "نقدي";
                if (trans.SourceType == TransactionSourceType.DailyJournalPayment)
                    row.PaymentMethod = "من اليومية";
                else if (trans.SourceType == TransactionSourceType.ExternalPayment)
                    // تحقق إذا كانت الدفعة تحويل بنكي أو سداد شخصي عبر الملاحظات
                    if (!string.IsNullOrEmpty(trans.Notes) && trans.Notes.Contains("تحويل بنكي"))
                    {
                        row.PaymentMethod = "تحويل";
                        row.IsTransferPayment = true;
                    }
                    else if (!string.IsNullOrEmpty(trans.Notes) && (trans.Notes.Contains("سداد شخصي") || trans.Notes.Contains("شخصي (شريك)")))
                    {
                        row.PaymentMethod = "شخصي (شريك)";
                    }
                    else
                    {
                        row.PaymentMethod = "نقدي";
                    }

                row.ReceiptNumber = trans.ReceiptNumber ?? (trans.SourceId > 0 ? trans.SourceId.ToString() : "");
                
                string displayNotes = trans.Notes ?? string.Empty;
                if (row.IsTransferPayment)
                {
                    var bankMatch = System.Text.RegularExpressions.Regex.Match(displayNotes, @"تحويل بنكي:\s*(.+?)\s*\(([^)]+)\)");
                    if (bankMatch.Success)
                    {
                        string bankName = bankMatch.Groups[1].Value.Trim();
                        string last4 = bankMatch.Groups[2].Value;
                        string description = displayNotes.Split('|')[0].Trim();
                        if (!string.IsNullOrEmpty(description))
                            displayNotes = $"{description} | {bankName} ({last4})";
                        else
                            displayNotes = $"{bankName} ({last4})";
                    }
                }
                row.Notes = displayNotes;
                
                currentBal -= trans.Amount;
                sumPayments += trans.Amount;
            }

            row.RemainingBalance = currentBal;
            
            UnifiedLedger.Add(row);
        }

        TotalInvoicesSum = sumInvoices;
        TotalPaymentsSum = sumPayments;
        TotalTransactionsCount = UnifiedLedger.Count;
    }

    [RelayCommand]
    public void OpenAddInvoiceForm()
    {
        _editingInvoiceId = null;
        NewInvoice = new SupplierInvoice 
        { 
            SupplierId = SupplierId, 
            InvoiceDate = DateTime.Now 
        };
        InvoiceItems.Clear();
        InvoiceItemsTotal = 0;
        IsTotalMatching = false;
        MatchingStatusText = string.Empty;
        IsInvoiceDetailsOpen = false;
        IsInvoiceFormOpen = true;
    }

    [RelayCommand]
    public void OpenInvoiceDetails()
    {
        IsInvoiceDetailsOpen = true;
        if (InvoiceItems.Count == 0)
        {
            AddInvoiceItem();
        }
        RecalculateItemsTotal();
    }

    [RelayCommand]
    public void CloseInvoiceDetails()
    {
        IsInvoiceDetailsOpen = false;
    }

    [RelayCommand]
    public void AddInvoiceItem()
    {
        var item = new InvoiceItemViewModel
        {
            Sequence = InvoiceItems.Count + 1
        };
        item.OnTotalChanged += RecalculateItemsTotal;
        InvoiceItems.Add(item);
        RecalculateItemsTotal();
    }

    [RelayCommand]
    public void RemoveInvoiceItem(InvoiceItemViewModel? item)
    {
        if (item == null) return;
        item.OnTotalChanged -= RecalculateItemsTotal;
        InvoiceItems.Remove(item);
        // إعادة ترقيم التسلسل
        for (int i = 0; i < InvoiceItems.Count; i++)
            InvoiceItems[i].Sequence = i + 1;
        RecalculateItemsTotal();
    }

    [RelayCommand]
    public void CloseInvoiceForm()
    {
        _editingInvoiceId = null;
        IsInvoiceFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveInvoiceAsync()
    {
        if (NewInvoice.TotalAmount <= 0)
        {
            MessageBox.Show("يجب إدخال قيمة صحيحة للفاتورة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // التحقق من تطابق الإجمالي إذا كان المستخدم أدخل تفاصيل
        if (InvoiceItems.Count > 0)
        {
            RecalculateItemsTotal();
            if (!IsTotalMatching)
            {
                var diff = NewInvoice.TotalAmount - InvoiceItemsTotal;
                var result = MessageBox.Show(
                    $"إجمالي الأصناف ({InvoiceItemsTotal:N2}) لا يتطابق مع إجمالي الفاتورة ({NewInvoice.TotalAmount:N2}).\n" +
                    $"الفرق: {diff:N2}\n\n" +
                    "هل تريد الحفظ على أي حال؟",
                    "تحذير: عدم تطابق الإجمالي",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (result == MessageBoxResult.No) return;
            }
        }

        try
        {
            using var context = new AppDbContext();
            var ledgerService = new LedgerService(context);

            if (_editingInvoiceId.HasValue)
            {
                // تعديل فاتورة قائمة
                var listItems = InvoiceItems.Select(item => new SupplierInvoiceItem
                {
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                }).ToList();

                await ledgerService.UpdateInvoiceAsync(NewInvoice, listItems);
                MessageBox.Show("تم تعديل الفاتورة بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // إضافة فاتورة جديدة
                await ledgerService.PostInvoiceAsync(NewInvoice);

                // حفظ تفاصيل الأصناف في قاعدة البيانات
                if (InvoiceItems.Count > 0)
                {
                    foreach (var item in InvoiceItems)
                    {
                        var dbItem = new MeezanPOS.Domain.Entities.SupplierInvoiceItem
                        {
                            SupplierInvoiceId = NewInvoice.Id,
                            Description = item.Description,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice,
                            TotalValue = item.TotalValue
                        };
                        context.SupplierInvoiceItems.Add(dbItem);
                    }
                    await context.SaveChangesAsync();
                }
                MessageBox.Show("تم حفظ الفاتورة بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            _editingInvoiceId = null;
            IsInvoiceDetailsOpen = false;
            IsInvoiceFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حفظ الفاتورة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void OpenPaymentForm(SupplierInvoice? invoice = null)
    {
        _editingPaymentTransactionId = null;
        SelectedInvoiceForPayment = invoice;
        PaymentAmount = invoice != null ? (invoice.TotalAmount - invoice.PaidAmount) : 0;
        PaymentDescription = invoice != null ? $"سداد فاتورة #{invoice.Id}" : "دفعة من الحساب للمورد";
        PaymentDate = DateTime.Now;
        ReceiptNumber = string.Empty;
        SelectedPaymentMethod = "نقدي";
        BankName = string.Empty;
        TransferLast4 = string.Empty;
        SelectedBankAccountForPayment = BankAccounts.FirstOrDefault();
        PartnerNameForPayment = PartnerNames.FirstOrDefault() ?? string.Empty;
        IsPaymentFormOpen = true;
    }

    [RelayCommand]
    public void ClosePaymentForm()
    {
        _editingPaymentTransactionId = null;
        IsPaymentFormOpen = false;
    }

    [RelayCommand]
    public async Task SavePaymentAsync()
    {
        if (PaymentAmount <= 0)
        {
            MessageBox.Show("يجب إدخال قيمة صحيحة للدفعة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            int? bankAccountId = null;
            string? partnerName = null;

            // التحقق من الحقول الإضافية بناءً على طريقة الدفع
            if (SelectedPaymentMethod == "تحويل" || SelectedPaymentMethod == "تحويل بنكي")
            {
                if (SelectedBankAccountForPayment == null)
                {
                    MessageBox.Show("يرجى تحديد الحساب البنكي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(TransferLast4))
                {
                    MessageBox.Show("يرجى إدخال رقم العملية أو آخر 4 أرقام من التحويل البنكي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                bankAccountId = SelectedBankAccountForPayment.Id;
            }
            else if (SelectedPaymentMethod == "شخصي (شريك)")
            {
                if (string.IsNullOrWhiteSpace(PartnerNameForPayment))
                {
                    MessageBox.Show("يرجى إدخال أو تحديد اسم الشريك الممول للدفعة الشخصية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                partnerName = PartnerNameForPayment;
            }

            // تجميع الملاحظات
            string finalNotes = PaymentDescription;
            if (!string.IsNullOrWhiteSpace(ReceiptNumber))
                finalNotes += $" | إيصال: {ReceiptNumber}";

            if ((SelectedPaymentMethod == "تحويل" || SelectedPaymentMethod == "تحويل بنكي") && SelectedBankAccountForPayment != null)
                finalNotes += $" | تحويل بنكي: {SelectedBankAccountForPayment.BankName} - {SelectedBankAccountForPayment.FriendlyName} ({TransferLast4})";
            else if (SelectedPaymentMethod == "شخصي (شريك)")
                finalNotes += $" | سداد شخصي: الشريك {PartnerNameForPayment}";
            else
                finalNotes += " | سداد نقدي";

            using var context = new AppDbContext();
            var ledgerService = new LedgerService(context);

            if (_editingPaymentTransactionId.HasValue)
            {
                // تعديل دفعة قائمة
                await ledgerService.UpdatePaymentAsync(
                    transactionId: _editingPaymentTransactionId.Value,
                    amount: PaymentAmount,
                    paymentDate: PaymentDate,
                    receiptNumber: ReceiptNumber,
                    notes: finalNotes,
                    bankAccountId: bankAccountId,
                    partnerName: partnerName,
                    bankReferenceNumber: TransferLast4
                );
                MessageBox.Show("تم تعديل الدفعة بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // توجيه الدفعة لمحرك الدفتر (Ledger Engine)
                var paymentId = int.TryParse(ReceiptNumber, out var rId) ? rId : 0;

                await ledgerService.PostPaymentAsync(
                    supplierId: SupplierId,
                    amount: PaymentAmount,
                    source: TransactionSourceType.ExternalPayment,
                    sourceId: paymentId,
                    paymentDate: PaymentDate,
                    targetInvoiceId: SelectedInvoiceForPayment?.Id,
                    receiptNumber: ReceiptNumber,
                    notes: finalNotes,
                    bankAccountId: bankAccountId,
                    partnerName: partnerName,
                    bankReferenceNumber: TransferLast4
                );

                MessageBox.Show("تم تسجيل الدفعة بنجاح في كشف حساب المورد.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            
            _editingPaymentTransactionId = null;
            IsPaymentFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حفظ الدفعة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task EditInvoiceAsync(UnifiedLedgerRow row)
    {
        if (row == null || row.SourceType != TransactionSourceType.Invoice) return;

        IsLoading = true;
        try
        {
            using var context = new AppDbContext();
            var invoice = await context.SupplierInvoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == row.SourceId);

            if (invoice == null)
            {
                MessageBox.Show("لم يتم العثور على الفاتورة في قاعدة البيانات.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _editingInvoiceId = invoice.Id;
            NewInvoice = new SupplierInvoice
            {
                Id = invoice.Id,
                SupplierId = invoice.SupplierId,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                TotalAmount = invoice.TotalAmount,
                Notes = invoice.Notes
            };

            InvoiceItems.Clear();
            foreach (var item in invoice.Items)
            {
                var itemVM = new InvoiceItemViewModel
                {
                    Sequence = InvoiceItems.Count + 1,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                };
                itemVM.OnTotalChanged += RecalculateItemsTotal;
                InvoiceItems.Add(itemVM);
            }

            RecalculateItemsTotal();
            IsInvoiceDetailsOpen = false;
            IsInvoiceFormOpen = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل الفاتورة للتعديل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task EditPaymentAsync(UnifiedLedgerRow row)
    {
        if (row == null || row.PaymentAmount == null) return;

        IsLoading = true;
        try
        {
            using var context = new AppDbContext();
            var trans = await context.SupplierTransactions
                .FirstOrDefaultAsync(t => t.Id == row.TransactionId);

            if (trans == null)
            {
                MessageBox.Show("لم يتم العثور على الدفعة في قاعدة البيانات.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _editingPaymentTransactionId = trans.Id;
            PaymentAmount = trans.Amount;
            PaymentDate = trans.TransactionDate;
            ReceiptNumber = trans.ReceiptNumber ?? string.Empty;

            // استخراج طريقة الدفع والملاحظات
            SelectedPaymentMethod = "نقدي";
            SelectedBankAccountForPayment = null;
            PartnerNameForPayment = string.Empty;
            TransferLast4 = string.Empty;

            if (trans.SourceType == TransactionSourceType.DailyJournalPayment)
            {
                SelectedPaymentMethod = "من اليومية";
            }
            else
            {
                var bankTx = await context.BankTransactions
                    .FirstOrDefaultAsync(t => t.SourceType == "SupplierTransaction" && t.SourceId == trans.Id && !t.IsDeleted);

                var ownerDebt = await context.OwnerDebts
                    .FirstOrDefaultAsync(d => d.SourceType == "SupplierTransaction" && d.SourceId == trans.Id && !d.IsDeleted);

                if (bankTx != null)
                {
                    SelectedPaymentMethod = "تحويل";
                    SelectedBankAccountForPayment = BankAccounts.FirstOrDefault(a => a.Id == bankTx.BankAccountId);
                    TransferLast4 = bankTx.ReferenceNumber ?? string.Empty;
                }
                else if (ownerDebt != null)
                {
                    SelectedPaymentMethod = "شخصي (شريك)";
                    PartnerNameForPayment = ownerDebt.PartnerName;
                }
            }

            // استخراج الوصف/البيان الأساسي من الملاحظات قبل علامة البايب |
            if (!string.IsNullOrEmpty(trans.Notes))
            {
                var parts = trans.Notes.Split('|');
                PaymentDescription = parts[0].Trim();
            }
            else
            {
                PaymentDescription = string.Empty;
            }

            // الفاتورة المرتبطة بالدفعة إن وجدت
            if (trans.SupplierInvoiceId.HasValue)
            {
                SelectedInvoiceForPayment = await context.SupplierInvoices.FindAsync(trans.SupplierInvoiceId.Value);
            }
            else
            {
                SelectedInvoiceForPayment = null;
            }

            IsPaymentFormOpen = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل الدفعة للتعديل:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
    [RelayCommand]
    public void PrintStatement()
    {
        if (UnifiedLedger == null || UnifiedLedger.Count == 0)
        {
            MessageBox.Show("لا توجد حركات لطباعتها في كشف الحساب.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsPrintSelectionOpen = true;
    }

    [RelayCommand]
    public void ClosePrintSelection()
    {
        IsPrintSelectionOpen = false;
    }

    [RelayCommand]
    public void PrintStandardStatement()
    {
        IsPrintSelectionOpen = false;
        try
        {
            string fileName = $"كشف_حساب_{SupplierName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

            SupplierStatementPdfReport.GeneratePdf(
                filePath, 
                SupplierName, 
                TotalOpeningBalance, 
                TotalInvoicesSum, 
                TotalPaymentsSum, 
                CurrentBalance, 
                UnifiedLedger.ToList(),
                FilterStartDate,
                FilterEndDate
            );

            // فتح الملف تلقائياً
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }
            };
            process.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء إنشاء التقرير:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task PrintDetailedStatement()
    {
        IsPrintSelectionOpen = false;
        IsLoading = true;
        try
        {
            // 1. استخراج الـ IDs الخاصة بجميع فواتير المشتريات المعروضة في كشف الحساب المفلتر حالياً
            var invoiceIds = UnifiedLedger
                .Where(r => r.HasInvoice && r.SourceType == TransactionSourceType.Invoice)
                .Select(r => r.SourceId)
                .Distinct()
                .ToList();

            List<SupplierInvoice> detailedInvoices = new();

            if (invoiceIds.Any())
            {
                using var context = new AppDbContext();
                // 2. تحميل الفواتير بكافة أصنافها
                detailedInvoices = await context.SupplierInvoices
                    .Include(i => i.Items)
                    .Where(i => invoiceIds.Contains(i.Id) && !i.IsDeleted)
                    .OrderBy(i => i.InvoiceDate)
                    .ThenBy(i => i.Id)
                    .ToListAsync();
            }

            string fileName = $"كشف_حساب_مفصل_{SupplierName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

            SupplierStatementPdfReport.GeneratePdf(
                filePath, 
                SupplierName, 
                TotalOpeningBalance, 
                TotalInvoicesSum, 
                TotalPaymentsSum, 
                CurrentBalance, 
                UnifiedLedger.ToList(),
                FilterStartDate,
                FilterEndDate,
                detailedInvoices
            );

            // فتح الملف تلقائياً
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }
            };
            process.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء إنشاء التقرير التفصيلي:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ShowShiftDetailsAsync(UnifiedLedgerRow row)
    {
        if (row == null || row.SourceType != TransactionSourceType.DailyJournalPayment)
            return;

        try
        {
            using var context = new AppDbContext();
            var expenseItem = await context.DailyExpenseItems
                .Include(e => e.DailyJournal)
                .FirstOrDefaultAsync(e => e.Id == row.SourceId);

            if (expenseItem == null || expenseItem.DailyJournal == null)
            {
                MessageBox.Show("لم يتم العثور على تفاصيل الوردية الخاصة بهذه الحركة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var journal = expenseItem.DailyJournal;
            
            string shiftDetails = $"🔍 تفاصيل عملية الدفع من الكاشير:\n" +
                                 $"------------------------------------------------------\n" +
                                 $"📅 تاريخ الدفع: {expenseItem.CreatedAt:yyyy/MM/dd HH:mm}\n" +
                                 $"🕒 نوع الوردية: {journal.ShiftName}\n" +
                                 $"👤 موظف الكاشير: {journal.EmployeeName}\n" +
                                 $"💰 القيمة المدفوعة: {expenseItem.Amount:N2} د.ل\n" +
                                 $"📝 بيان الصرف: {expenseItem.Description}\n" +
                                 $"------------------------------------------------------\n" +
                                 $"📅 تاريخ الوردية الأساسي: {journal.JournalDate:yyyy/MM/dd}\n" +
                                 $"✍️ ملاحظات الوردية: {journal.Notes ?? "لا يوجد"}";

            MessageBox.Show(shiftDetails, "بيانات وردية الدفع", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء تحميل تفاصيل الوردية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ShowTransferDetails(UnifiedLedgerRow row)
    {
        if (row == null || !row.IsTransferPayment)
            return;

        // استخراج تفاصيل التحويل من الملاحظات المخزنة
        string notesText = row.Notes ?? "";
        string bankName = "غير محدد";
        string last4 = "----";

        // الملاحظات مخزنة بصيغة: ... | تحويل بنكي: اسم_المصرف (رقم_التحويل)
        try
        {
            // محاولة جلب تفاصيل التحويل بدقة مباشرة من قاعدة البيانات لضمان الدقة الكاملة
            using (var db = new AppDbContext())
            {
                var bankTx = db.BankTransactions
                    .Include(t => t.BankAccount)
                    .FirstOrDefault(t => t.SourceType == "SupplierTransaction" && t.SourceId == row.TransactionId && !t.IsDeleted);

                if (bankTx != null)
                {
                    bankName = bankTx.BankAccount?.FriendlyName ?? "غير محدد";
                    last4 = bankTx.ReferenceNumber ?? "----";
                }
            }
        }
        catch
        {
            // في حال حدوث أي خطأ، نعتمد على المعالجة النصية كبديل
        }

        if (bankName == "غير محدد" || last4 == "----")
        {
            var bankMatch = System.Text.RegularExpressions.Regex.Match(notesText, @"تحويل بنكي:\s*(.+?)\s*\(([^)]+)\)");
            if (bankMatch.Success)
            {
                bankName = bankMatch.Groups[1].Value.Trim();
                last4 = bankMatch.Groups[2].Value.Trim();
            }
        }

        string details = $"🏦 تفاصيل التحويل المصرفي:\n" +
                         $"------------------------------------------------------\n" +
                         $"📅 التاريخ: {row.PaymentDate:yyyy/MM/dd}\n" +
                         $"💰 القيمة: {row.PaymentAmount:N2} د.ل\n" +
                         $"🏛️ المصرف: {bankName}\n" +
                         $"🔢 رقم التحويل اخر 4 ارقام: {last4}\n" +
                         $"🧾 رقم الإيصال: {(string.IsNullOrEmpty(row.ReceiptNumber) ? "لا يوجد" : row.ReceiptNumber)}";

        MessageBox.Show(details, "تفاصيل التحويل المصرفي", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
