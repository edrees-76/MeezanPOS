using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Presentation.Views
{
    public partial class TransactionDetailsViewWindow : Window, INotifyPropertyChanged
    {
        private readonly string _sourceType;
        private readonly int _sourceId;

        // UI state properties
        private string _headerIcon = "FileDocumentOutline";
        private Brush _headerColor = Brushes.Gray;
        private string _headerTitle = "تفاصيل الحركة";
        private bool _isLoading = true;
        private bool _isGeneralExpense;
        private bool _isSupplierPayment;
        private bool _isOwnerDebt;
        private bool _isOwnerSettlement;

        // General Expense fields
        private string _expenseTypeName = string.Empty;
        private string _expenseAmountString = string.Empty;
        private string _expenseDateString = string.Empty;
        private string _expensePaymentMethodName = string.Empty;
        private string _expenseWorkerName = string.Empty;
        private string _expenseFinancialStatusName = string.Empty;
        private string _expenseDescription = string.Empty;
        private Brush _statusColor = Brushes.Gray;

        // Supplier Payment fields
        private string _supplierName = string.Empty;
        private string _paymentAmountString = string.Empty;
        private string _paymentDateString = string.Empty;
        private string _receiptNumber = string.Empty;
        private string _paymentNotes = string.Empty;
        private bool _hasInvoice;
        private string _invoiceNumber = string.Empty;
        private string _invoiceDateString = string.Empty;
        private string _invoiceTotalString = string.Empty;
        private string _invoiceStatusName = string.Empty;
        private List<SupplierInvoiceItem> _invoiceItems = new();
        private string _paymentMethod = string.Empty;
        private string _transferReference = string.Empty;

        public TransactionDetailsViewWindow(string sourceType, int sourceId)
        {
            InitializeComponent();
            _sourceType = sourceType;
            _sourceId = sourceId;
            DataContext = this;

            Loaded += async (s, e) => await LoadDataAsync();
        }

        public bool IsOwnerDebt
        {
            get => _isOwnerDebt;
            set { _isOwnerDebt = value; OnPropertyChanged(); }
        }

        public bool IsOwnerSettlement
        {
            get => _isOwnerSettlement;
            set { _isOwnerSettlement = value; OnPropertyChanged(); }
        }

        public string PaymentMethod
        {
            get => _paymentMethod;
            set { _paymentMethod = value; OnPropertyChanged(); }
        }

        public string TransferReference
        {
            get => _transferReference;
            set { _transferReference = value; OnPropertyChanged(); }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                if (_sourceType == "GeneralExpense")
                {
                    IsGeneralExpense = true;
                    HeaderTitle = "تفاصيل المصروف العام";
                    HeaderIcon = "CreditCardOutline";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(225, 29, 72)); // Rose 600

                    using var db = new AppDbContext();
                    var expense = await db.GeneralExpenses
                        .FirstOrDefaultAsync(x => x.Id == _sourceId);

                    if (expense != null)
                    {
                        ExpenseTypeName = GetExpenseTypeName(expense.ExpenseType);
                        ExpenseAmountString = $"{expense.Amount:N2} د.ل";
                        ExpenseDateString = expense.PaymentDate.ToString("yyyy/MM/dd");
                        ExpensePaymentMethodName = GetPaymentMethodName(expense.PaymentMethod);
                        ExpenseWorkerName = expense.WorkerName ?? "غير محدد";
                        ExpenseFinancialStatusName = expense.FinancialStatus == FinancialStatus.Posted ? "معتمد" : "مسودة";
                        ExpenseDescription = string.IsNullOrEmpty(expense.Description) ? "لا يوجد بيان مدخل" : expense.Description;
                        StatusColor = expense.FinancialStatus == FinancialStatus.Posted 
                            ? new SolidColorBrush(Color.FromRgb(22, 163, 74)) // Green 600
                            : new SolidColorBrush(Color.FromRgb(217, 119, 6)); // Amber 600
                    }
                    else
                    {
                        ExpenseDescription = "لم يتم العثور على بيانات المصروف العام في قاعدة البيانات.";
                    }
                }
                else if (_sourceType == "SupplierTransaction")
                {
                    IsSupplierPayment = true;
                    HeaderTitle = "تفاصيل سداد المورد";
                    HeaderIcon = "AccountCashOutline";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Blue 600

                    using var db = new AppDbContext();
                    var trans = await db.SupplierTransactions
                        .Include(t => t.Supplier)
                        .Include(t => t.SupplierInvoice)
                            .ThenInclude(i => i!.Items)
                        .FirstOrDefaultAsync(t => t.Id == _sourceId);

                    if (trans != null)
                    {
                        SupplierName = trans.Supplier?.Name ?? "مورد غير معروف";
                        PaymentAmountString = $"{trans.Amount:N2} د.ل";
                        PaymentDateString = trans.TransactionDate.ToString("yyyy/MM/dd");
                        ReceiptNumber = trans.ReceiptNumber ?? "غير محدد";
                        PaymentNotes = string.IsNullOrEmpty(trans.Notes) ? "لا توجد ملاحظات سداد" : trans.Notes;

                        if (trans.SupplierInvoice != null)
                        {
                            HasInvoice = true;
                            InvoiceNumber = trans.SupplierInvoice.InvoiceNumber ?? "غير محدد";
                            InvoiceDateString = trans.SupplierInvoice.InvoiceDate.ToString("yyyy/MM/dd");
                            InvoiceTotalString = $"{trans.SupplierInvoice.TotalAmount:N2} د.ل";
                            InvoiceStatusName = trans.SupplierInvoice.Status == InvoiceStatus.Paid ? "مسددة" 
                                              : trans.SupplierInvoice.Status == InvoiceStatus.PartiallyPaid ? "مسددة جزئياً" 
                                              : "غير مسددة";
                            InvoiceItems = trans.SupplierInvoice.Items ?? new List<SupplierInvoiceItem>();
                        }
                    }
                    else
                    {
                        PaymentNotes = "لم يتم العثور على تفاصيل حركة السداد في قاعدة البيانات.";
                    }
                }
                else if (_sourceType == "OwnerDebt" || _sourceType == "Bank" || _sourceType == "Cash")
                {
                    IsOwnerDebt = true;
                    HeaderTitle = "تفاصيل تمويل الشريك";
                    HeaderIcon = "HandCoinOutline";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald 500

                    using var db = new AppDbContext();
                    // محاولة جلب الدين حسب الـ ID
                    // ملاحظة: إذا كان الـ sourceType هو "Bank" أو "Cash" فهذا يعني أنه تمويل يدوي (في النسخة الجديدة)
                    // والـ sourceId هو الـ BankAccountId أو null.
                    // لكن في كشف حساب الشريك، الـ ReferenceNumber هو ID الدين.
                    // والـ SourceId في الـ DTO يتم تعبئته من d.SourceId.
                    
                    // لحظة، في GetPartnerStatementAsync:
                    // SourceType = d.SourceType,
                    // SourceId = d.SourceId,
                    // ReferenceNumber = d.Id.ToString()
                    
                    // إذاً يجب أن أستخدم d.Id وهو موجود في الـ ReferenceNumber.
                    // لكن النافذة تستقبل _sourceId.
                    
                    var debt = await db.OwnerDebts
                        .FirstOrDefaultAsync(x => x.Id == _sourceId);

                    if (debt != null)
                    {
                        SupplierName = debt.PartnerName; // استعارة الحقل للاسم
                        PaymentAmountString = $"{debt.Amount:N2} د.ل";
                        PaymentDateString = debt.TransactionDate.ToString("yyyy/MM/dd");
                        ReceiptNumber = debt.ExpenseCategory ?? "تمويل تشغيلي";
                        PaymentNotes = string.IsNullOrEmpty(debt.Notes) ? "لا توجد ملاحظات" : debt.Notes;
                        
                        PaymentMethod = debt.PaymentMethod == "Transfer" ? "تحويل مصرفي" : (debt.PaymentMethod == "Cash" ? "نقدي" : "-");
                        TransferReference = debt.TransferReference ?? "غير محدد";
                        
                        // إذا كان تمويلاً بنكياً، نحاول معرفة البنك
                        if (debt.SourceType == "Bank" && debt.SourceId.HasValue)
                        {
                            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == debt.SourceId.Value);
                            if (bank != null)
                            {
                                InvoiceNumber = bank.FriendlyName; // استعارة الحقل لاسم البنك
                                HasInvoice = true; // سنعرض قسماً للبنك
                            }
                        }
                    }
                    else
                    {
                        PaymentNotes = "لم يتم العثور على بيانات التمويل.";
                    }
                }
                else if (_sourceType == "OwnerDebtSettlement")
                {
                    IsOwnerSettlement = true;
                    HeaderTitle = "تفاصيل تسوية ذمة";
                    HeaderIcon = "CashCheck";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber 500

                    using var db = new AppDbContext();
                    var sett = await db.OwnerDebtSettlements
                        .Include(s => s.BankAccount)
                        .FirstOrDefaultAsync(s => s.Id == _sourceId);

                    if (sett != null)
                    {
                        SupplierName = sett.PartnerName;
                        PaymentAmountString = $"{sett.Amount:N2} د.ل";
                        PaymentDateString = sett.SettlementDate.ToString("yyyy/MM/dd");
                        ReceiptNumber = sett.SettlementSource == OwnerDebtSettlementSource.Bank ? "تسوية مصرفية" 
                                      : sett.SettlementSource == OwnerDebtSettlementSource.PettyCash ? "تسوية من الخزينة" 
                                      : "تسوية من الكاشير";
                        PaymentNotes = string.IsNullOrEmpty(sett.Notes) ? "لا توجد ملاحظات" : sett.Notes;
                        
                        if (sett.BankAccount != null)
                        {
                            InvoiceNumber = sett.BankAccount.FriendlyName;
                            HasInvoice = true;
                        }
                    }
                    else
                    {
                        PaymentNotes = "لم يتم العثور على بيانات التسوية.";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"حدث خطأ أثناء تحميل تفاصيل العملية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string GetExpenseTypeName(GeneralExpenseType type) => type switch
        {
            GeneralExpenseType.Rent => "إيجار",
            GeneralExpenseType.Electricity => "كهرباء",
            GeneralExpenseType.Water => "ماء",
            GeneralExpenseType.Salaries => "رواتب",
            GeneralExpenseType.Internet => "إنترنت",
            GeneralExpenseType.Maintenance => "صيانة",
            GeneralExpenseType.Insurance => "تأمين",
            GeneralExpenseType.Taxes => "ضرائب/رسوم",
            GeneralExpenseType.SupplierPayment => "تسديد قيمة لمورد",
            GeneralExpenseType.Other => "أخرى",
            _ => type.ToString()
        };

        private string GetPaymentMethodName(PaymentMethodType method) => method switch
        {
            PaymentMethodType.Cash => "نقدي",
            PaymentMethodType.BankTransfer => "تحويل بنكي",
            PaymentMethodType.Cheque => "شيك",
            PaymentMethodType.PersonalPartner => "شخصي (شريك)",
            _ => method.ToString()
        };

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // Exposing properties to XAML
        public string HeaderIcon { get => _headerIcon; set => SetField(ref _headerIcon, value); }
        public Brush HeaderColor { get => _headerColor; set => SetField(ref _headerColor, value); }
        public string HeaderTitle { get => _headerTitle; set => SetField(ref _headerTitle, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        public bool IsGeneralExpense { get => _isGeneralExpense; set => SetField(ref _isGeneralExpense, value); }
        public bool IsSupplierPayment { get => _isSupplierPayment; set => SetField(ref _isSupplierPayment, value); }

        public string ExpenseTypeName { get => _expenseTypeName; set => SetField(ref _expenseTypeName, value); }
        public string ExpenseAmountString { get => _expenseAmountString; set => SetField(ref _expenseAmountString, value); }
        public string ExpenseDateString { get => _expenseDateString; set => SetField(ref _expenseDateString, value); }
        public string ExpensePaymentMethodName { get => _expensePaymentMethodName; set => SetField(ref _expensePaymentMethodName, value); }
        public string ExpenseWorkerName { get => _expenseWorkerName; set => SetField(ref _expenseWorkerName, value); }
        public string ExpenseFinancialStatusName { get => _expenseFinancialStatusName; set => SetField(ref _expenseFinancialStatusName, value); }
        public string ExpenseDescription { get => _expenseDescription; set => SetField(ref _expenseDescription, value); }
        public Brush StatusColor { get => _statusColor; set => SetField(ref _statusColor, value); }

        public string SupplierName { get => _supplierName; set => SetField(ref _supplierName, value); }
        public string PaymentAmountString { get => _paymentAmountString; set => SetField(ref _paymentAmountString, value); }
        public string PaymentDateString { get => _paymentDateString; set => SetField(ref _paymentDateString, value); }
        public string ReceiptNumber { get => _receiptNumber; set => SetField(ref _receiptNumber, value); }
        public string PaymentNotes { get => _paymentNotes; set => SetField(ref _paymentNotes, value); }
        public bool HasInvoice { get => _hasInvoice; set => SetField(ref _hasInvoice, value); }
        public string InvoiceNumber { get => _invoiceNumber; set => SetField(ref _invoiceNumber, value); }
        public string InvoiceDateString { get => _invoiceDateString; set => SetField(ref _invoiceDateString, value); }
        public string InvoiceTotalString { get => _invoiceTotalString; set => SetField(ref _invoiceTotalString, value); }
        public string InvoiceStatusName { get => _invoiceStatusName; set => SetField(ref _invoiceStatusName, value); }
        public List<SupplierInvoiceItem> InvoiceItems { get => _invoiceItems; set => SetField(ref _invoiceItems, value); }
    }
}
