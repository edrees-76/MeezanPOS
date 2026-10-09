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
using MeezanPOS.Application.ViewModels;
using QuestPDF.Fluent;

namespace MeezanPOS.Presentation.Views
{
    public partial class TransactionDetailsViewWindow : Window, INotifyPropertyChanged
    {
        private readonly MeezanPOS.Application.Services.Queries.ITransactionSourceQueryService _queries
            = new MeezanPOS.Application.Services.Queries.TransactionSourceQueryService();
        private CashMovement? _cashMovement;
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
        private bool _isDailyJournal;

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

        // CashMovement fields
        private bool _hasCashMovement;
        private string _cashMovementSequence = string.Empty;
        private string _cashMovementAmountString = string.Empty;
        private string _cashMovementDateString = string.Empty;
        private string _cashMovementTypeString = string.Empty;
        private string _cashMovementBalanceAfterString = string.Empty;
        private string _cashMovementStatusString = string.Empty;
        private Brush _cashMovementStatusColor = Brushes.Gray;
        private string _cashMovementNotes = string.Empty;

        public TransactionDetailsViewWindow(string sourceType, int sourceId)
        {
            InitializeComponent();
            _sourceType = sourceType;
            _sourceId = sourceId;
            DataContext = this;

            Loaded += async (s, e) => await LoadDataAsync();
        }

        public TransactionDetailsViewWindow(CashMovement movement)
        {
            InitializeComponent();
            _cashMovement = movement;
            _sourceType = movement.SourceType ?? string.Empty;
            _sourceId = movement.SourceId ?? 0;
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

        public bool HasCashMovement
        {
            get => _hasCashMovement;
            set { _hasCashMovement = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoSourceDetails)); }
        }

        public bool HasNoSourceDetails => !_isGeneralExpense && !_isSupplierPayment && !_isDailyJournal && !_isOwnerDebt && !_isOwnerSettlement;

        public bool CanGoToSource => _sourceType == "DailyJournal" || _sourceType == "GeneralExpense" || _sourceType == "SupplierTransaction";

        public string CashMovementSequence
        {
            get => _cashMovementSequence;
            set { _cashMovementSequence = value; OnPropertyChanged(); }
        }

        public string CashMovementAmountString
        {
            get => _cashMovementAmountString;
            set { _cashMovementAmountString = value; OnPropertyChanged(); }
        }

        public string CashMovementDateString
        {
            get => _cashMovementDateString;
            set { _cashMovementDateString = value; OnPropertyChanged(); }
        }

        public string CashMovementTypeString
        {
            get => _cashMovementTypeString;
            set { _cashMovementTypeString = value; OnPropertyChanged(); }
        }

        public string CashMovementBalanceAfterString
        {
            get => _cashMovementBalanceAfterString;
            set { _cashMovementBalanceAfterString = value; OnPropertyChanged(); }
        }

        public string CashMovementStatusString
        {
            get => _cashMovementStatusString;
            set { _cashMovementStatusString = value; OnPropertyChanged(); }
        }

        public Brush CashMovementStatusColor
        {
            get => _cashMovementStatusColor;
            set { _cashMovementStatusColor = value; OnPropertyChanged(); }
        }

        public string CashMovementNotes
        {
            get => _cashMovementNotes;
            set { _cashMovementNotes = value; OnPropertyChanged(); }
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
                // 1. Try to load the corresponding CashMovement if not already loaded
                if (_cashMovement == null && _sourceId > 0 && !string.IsNullOrEmpty(_sourceType))
                {
                    _cashMovement = await _queries.GetCashMovementAsync(_sourceType, _sourceId);
                }

                if (_cashMovement != null)
                {
                    HasCashMovement = true;
                    CashMovementSequence = _cashMovement.Sequence > 0 ? _cashMovement.Sequence.ToString() : _cashMovement.Id.ToString();
                    CashMovementAmountString = $"{_cashMovement.Amount:N2} د.ل";
                    CashMovementDateString = _cashMovement.TransactionDate.ToString("yyyy/MM/dd hh:mm tt");
                    CashMovementTypeString = _cashMovement.Type == CashMovementType.CashIn ? "وارد (+)" : "صادر (-)";
                    CashMovementBalanceAfterString = $"{_cashMovement.BalanceAfter:N2} د.ل";
                    CashMovementStatusString = _cashMovement.IsReversed ? "معكوسة" : "سارية";
                    CashMovementStatusColor = _cashMovement.IsReversed
                        ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) // Red 600
                        : new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green 600
                    CashMovementNotes = string.IsNullOrEmpty(_cashMovement.Notes) ? "لا يوجد" : _cashMovement.Notes;

                    // Customize Header
                    HeaderTitle = "تفاصيل الحركة المالية";
                    HeaderIcon = "CashMultiple";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green 600
                }
                else
                {
                    HasCashMovement = false;
                }

                if (_sourceType == "GeneralExpense")
                {
                    IsGeneralExpense = true;
                    if (!HasCashMovement)
                    {
                        HeaderTitle = "تفاصيل المصروف العام";
                        HeaderIcon = "CreditCardOutline";
                        HeaderColor = new SolidColorBrush(Color.FromRgb(225, 29, 72)); // Rose 600
                    }

                    var expense = await _queries.GetGeneralExpenseAsync(_sourceId);

                    if (expense != null)
                    {
                        ExpenseTypeName = expense.ExpenseType == GeneralExpenseType.Other && !string.IsNullOrEmpty(expense.CustomExpenseName)
                            ? expense.CustomExpenseName
                            : GetExpenseTypeName(expense.ExpenseType);
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

                    var trans = await _queries.GetSupplierPaymentAsync(_sourceId);

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
                else if (_sourceType == "OwnerDebt" || _sourceType == "Bank" || _sourceType == "Cash" || _sourceType == "OwnerDebt_Transfer" || _sourceType == "OwnerDebt_Cash")
                {
                    IsOwnerDebt = true;
                    HeaderTitle = "تفاصيل تمويل الشريك";
                    HeaderIcon = "HandCoinOutline";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald 500

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

                    var debt = await _queries.GetOwnerDebtAsync(_sourceId);

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
                            var bank = await _queries.GetBankAccountAsync(debt.SourceId.Value);
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

                    var sett = await _queries.GetSettlementAsync(_sourceId);

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
                else if (_sourceType == "DailyJournal")
                {
                    IsDailyJournal = true;
                    HeaderTitle = "تفاصيل حركة الكاش لليومية/الوردية";
                    HeaderIcon = "CalendarSyncOutline";
                    HeaderColor = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Emerald 500

                    var journal = await _queries.GetJournalAsync(_sourceId);

                    if (journal != null)
                    {
                        SupplierName = journal.EmployeeName;
                        PaymentAmountString = $"{journal.ActualCash - journal.CashFloat:N2} د.ل";
                        PaymentDateString = journal.JournalDate.ToString("yyyy/MM/dd HH:mm");
                        ReceiptNumber = journal.ShiftName;
                        PaymentNotes = string.IsNullOrEmpty(journal.Notes) ? "لا توجد ملاحظات" : journal.Notes;

                        InvoiceNumber = $"{journal.CashFloat:N2} د.ل";
                        InvoiceDateString = $"{journal.ExpectedCash:N2} د.ل";
                        InvoiceTotalString = $"{journal.ActualCash:N2} د.ل";
                        InvoiceStatusName = journal.DifferenceText;

                        StatusColor = journal.Difference < 0
                            ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) // Red 600
                            : (journal.Difference > 0
                                ? new SolidColorBrush(Color.FromRgb(22, 163, 74)) // Green 600
                                : new SolidColorBrush(Color.FromRgb(71, 85, 105))); // Slate 600
                    }
                    else
                    {
                        PaymentNotes = "لم يتم العثور على بيانات اليومية.";
                    }
                }
            }
            catch (Exception ex)
            {
                Dialogs.Show($"حدث خطأ أثناء تحميل تفاصيل العملية:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
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
        public bool IsDailyJournal { get => _isDailyJournal; set => SetField(ref _isDailyJournal, value); }

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

        private void PrintReceipt_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                var filePath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"إيصال_حركة_{_cashMovement?.Id ?? _sourceId}_{System.DateTime.Now:yyyyMMdd_HHmmss}.pdf");

                QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(226, 400, QuestPDF.Infrastructure.Unit.Point);
                        page.Margin(10, QuestPDF.Infrastructure.Unit.Point);
                        page.PageColor(QuestPDF.Helpers.Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial").DirectionFromRightToLeft());
                        page.ContentFromRightToLeft();

                        page.Content().Column(col =>
                        {
                            col.Item().AlignCenter().Text("منظومة ميزان للمطاعم").FontSize(11).Bold();
                            col.Item().AlignCenter().Text("إيصال حركة مالية").FontSize(10).Bold().FontColor(QuestPDF.Helpers.Colors.Green.Darken2);
                            col.Item().LineHorizontal(1).LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                            col.Item().Padding(4);

                            col.Item().Text($"التسلسل: {CashMovementSequence}");
                            col.Item().Text($"التاريخ: {CashMovementDateString}");
                            col.Item().Text($"نوع الحركة: {CashMovementTypeString}");
                            col.Item().Text($"المبلغ: {CashMovementAmountString}").Bold();
                            col.Item().Text($"الرصيد بعد الحركة: {CashMovementBalanceAfterString}");
                            col.Item().Text($"الحالة: {CashMovementStatusString}");
                            col.Item().LineHorizontal(1).LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                            col.Item().Padding(4);

                            if (_isGeneralExpense)
                            {
                                col.Item().Text($"المصدر: مصروف عام ({ExpenseTypeName})");
                                col.Item().Text($"طريقة الدفع: {ExpensePaymentMethodName}");
                                col.Item().Text($"المستلم: {ExpenseWorkerName}");
                            }
                            else if (_isSupplierPayment)
                            {
                                col.Item().Text($"المصدر: سداد مورد ({SupplierName})");
                                col.Item().Text($"رقم الإيصال: {ReceiptNumber}");
                            }
                            else if (_isDailyJournal)
                            {
                                col.Item().Text($"المصدر: يومية عمل ({ReceiptNumber})");
                                col.Item().Text($"الكاشير: {SupplierName}");
                            }

                            col.Item().Text($"البيان: {CashMovementNotes}").FontSize(8.5f).FontColor(QuestPDF.Helpers.Colors.Grey.Darken2);

                            col.Item().PaddingVertical(8);
                            col.Item().LineHorizontal(1).LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                            col.Item().AlignCenter().Text("شكراً لتعاملكم معنا").FontSize(8).Italic();
                        });
                    });
                })
                .GeneratePdf(filePath);

                new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true
                    }
                }.Start();
            }
            catch (Exception ex)
            {
                Dialogs.Show($"حدث خطأ أثناء طباعة الإيصال:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void GoToSource_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await GoToSourceAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "تعذر فتح مصدر الحركة {SourceType}#{SourceId}", _sourceType, _sourceId);
                Dialogs.Show($"تعذر فتح مصدر الحركة:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task GoToSourceAsync()
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow?.DataContext is MainViewModel mainVM)
            {
                if (_sourceType == "DailyJournal")
                {
                    var journal = await _queries.GetJournalWithItemsAsync(_sourceId);

                    if (journal != null)
                    {
                        var journalVM = new DailyJournalViewModel();
                        journalVM.LoadJournalForViewing(journal);
                        journalVM.OnClose = () =>
                        {
                            mainVM.CurrentViewModel = new SalesViewModel();
                            mainVM.Title = "ميزان للمالية - المبيعات والإيرادات";
                        };
                        mainVM.CurrentViewModel = journalVM;
                        mainVM.Title = "ميزان للمالية - عرض تفاصيل الحركة اليومية";
                        Close();
                    }
                    else
                    {
                        Dialogs.Show("تعذر العثور على اليومية المرتبطة بالدوران المالي.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else if (_sourceType == "GeneralExpense")
                {
                    // المصاريف العامة تبويب داخل شاشة إدارة المصروفات، لا شاشة مستقلة
                    // (تعيينها مباشرة كان يعرض اسم الصنف بدل الشاشة)
                    mainVM.NavigateCommand.Execute("Expenses");
                    if (mainVM.CurrentViewModel is ExpenseManagementViewModel expensesVM)
                    {
                        expensesVM.SelectedTabIndex = 1;
                        Close();
                    }
                }
                else if (_sourceType == "SupplierTransaction")
                {
                    var trans = await _queries.GetSupplierPaymentAsync(_sourceId);
                    if (trans != null && trans.SupplierId > 0)
                    {
                        var supplierVM = new SupplierDetailsViewModel(trans.SupplierId, SupplierName);
                        mainVM.CurrentViewModel = supplierVM;
                        mainVM.Title = "ميزان للمالية - تفاصيل حساب المورد";
                        Close();
                    }
                }
            }
        }
    }
}
