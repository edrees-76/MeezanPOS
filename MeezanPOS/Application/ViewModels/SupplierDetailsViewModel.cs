using System;
using System.Collections.ObjectModel;
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
    [ObservableProperty] private decimal? invoiceAmount;
    [ObservableProperty] private DateTime? invoiceDate;
    [ObservableProperty] private string invoiceNumber = string.Empty;
    
    // Payment side
    [ObservableProperty] private decimal? paymentAmount;
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

    [ObservableProperty] private bool isTransferPayment;

    public bool IsDailyJournalPayment => SourceType == TransactionSourceType.DailyJournalPayment;
    public bool IsPlainPayment => !IsDailyJournalPayment && !IsTransferPayment;
}

public partial class SupplierDetailsViewModel : ObservableObject
{
    private readonly AppDbContext _context;
    private readonly ILedgerService _ledgerService;
    
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

    // --- نافذة إضافة فاتورة ---
    [ObservableProperty]
    private bool isInvoiceFormOpen;

    [ObservableProperty]
    private SupplierInvoice newInvoice = new();

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

    public SupplierDetailsViewModel(int supplierId, string supplierName)
    {
        SupplierId = supplierId;
        SupplierName = supplierName;
        _context = new AppDbContext();
        _ledgerService = new LedgerService(_context);
        
        _ = LoadDataAsync();
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
            var supplier = await _context.Suppliers.FindAsync(SupplierId);
            if (supplier != null) CurrentBalance = supplier.CurrentBalance;

            var invoicesList = await _context.SupplierInvoices
                .Where(i => i.SupplierId == SupplierId && !i.IsDeleted)
                .OrderByDescending(i => i.InvoiceDate)
                .ThenByDescending(i => i.Id)
                .ToListAsync();
            Invoices = new ObservableCollection<SupplierInvoice>(invoicesList);

            var transactionsList = await _ledgerService.GetStatementAsync(SupplierId, DateTime.MinValue, DateTime.MaxValue);
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
        if (_context.Suppliers.Find(SupplierId) is var supplier)
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
                SourceId = trans.SourceId
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
                {
                    // تحقق إذا كانت الدفعة تحويل بنكي عبر الملاحظات
                    if (!string.IsNullOrEmpty(trans.Notes) && trans.Notes.Contains("تحويل بنكي"))
                    {
                        row.PaymentMethod = "تحويل";
                        row.IsTransferPayment = true;
                    }
                    else
                    {
                        row.PaymentMethod = "نقدي";
                    }
                }

                row.ReceiptNumber = trans.ReceiptNumber ?? (trans.SourceId > 0 ? trans.SourceId.ToString() : "");
                
                string displayNotes = trans.Notes ?? string.Empty;
                if (row.IsTransferPayment)
                {
                    var bankMatch = System.Text.RegularExpressions.Regex.Match(displayNotes, @"تحويل بنكي:\s*(.+?)\s*\((\d{1,4})\)");
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
        NewInvoice = new SupplierInvoice 
        { 
            SupplierId = SupplierId, 
            InvoiceDate = DateTime.Now 
        };
        IsInvoiceFormOpen = true;
    }

    [RelayCommand]
    public void CloseInvoiceForm() => IsInvoiceFormOpen = false;

    [RelayCommand]
    public async Task SaveInvoiceAsync()
    {
        if (NewInvoice.TotalAmount <= 0)
        {
            MessageBox.Show("يجب إدخال قيمة صحيحة للفاتورة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await _ledgerService.PostInvoiceAsync(NewInvoice);
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
        SelectedInvoiceForPayment = invoice;
        PaymentAmount = invoice != null ? (invoice.TotalAmount - invoice.PaidAmount) : 0;
        PaymentDescription = invoice != null ? $"سداد فاتورة #{invoice.Id}" : "دفعة من الحساب للمورد";
        PaymentDate = DateTime.Now;
        ReceiptNumber = string.Empty;
        SelectedPaymentMethod = "نقدي";
        BankName = string.Empty;
        TransferLast4 = string.Empty;
        IsPaymentFormOpen = true;
    }

    [RelayCommand]
    public void ClosePaymentForm() => IsPaymentFormOpen = false;

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
            // التحقق من الحقول الإضافية إذا كان تحويل
            if (SelectedPaymentMethod == "تحويل" && (string.IsNullOrWhiteSpace(BankName) || string.IsNullOrWhiteSpace(TransferLast4)))
            {
                MessageBox.Show("يجب إدخال اسم المصرف ورقم التحويل اخر 4 ارقام للتحويل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // تجميع الملاحظات
            string finalNotes = PaymentDescription;
            if (!string.IsNullOrWhiteSpace(ReceiptNumber))
                finalNotes += $" | إيصال: {ReceiptNumber}";
            if (SelectedPaymentMethod == "تحويل")
                finalNotes += $" | تحويل بنكي: {BankName} ({TransferLast4})";

            // حسب طلب المستخدم: لا يتم الخصم من الكاشير هنا

            // توجيه الدفعة لمحرك الدفتر (Ledger Engine)
            var paymentId = int.TryParse(ReceiptNumber, out var rId) ? rId : 0;

            await _ledgerService.PostPaymentAsync(
                supplierId: SupplierId,
                amount: PaymentAmount,
                source: SelectedPaymentMethod == "تحويل" ? TransactionSourceType.ExternalPayment : TransactionSourceType.ExternalPayment,
                sourceId: paymentId,
                paymentDate: PaymentDate,
                targetInvoiceId: SelectedInvoiceForPayment?.Id,
                receiptNumber: ReceiptNumber,
                notes: finalNotes
            );

            MessageBox.Show("تم تسجيل الدفعة بنجاح في كشف حساب المورد.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information);
            
            IsPaymentFormOpen = false;
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"حدث خطأ أثناء حفظ الدفعة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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

        try
        {
            string fileName = $"كشف_حساب_{SupplierName}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string filePath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), fileName);

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

        // الملاحظات مخزنة بصيغة: ... | تحويل بنكي: اسم_المصرف (آخر_4_أرقام)
        var bankMatch = System.Text.RegularExpressions.Regex.Match(notesText, @"تحويل بنكي:\s*(.+?)\s*\((\d{1,4})\)");
        if (bankMatch.Success)
        {
            bankName = bankMatch.Groups[1].Value.Trim();
            last4 = bankMatch.Groups[2].Value;
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
