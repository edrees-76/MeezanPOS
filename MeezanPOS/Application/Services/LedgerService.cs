using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services;

public class LedgerService : ILedgerService
{
    private readonly AppDbContext _context;
    private readonly IBankService _bankService;
    private readonly IOwnerDebtService _ownerDebtService;
    private readonly ICashLedgerService _cashLedgerService;
    private readonly ISessionService _session;
    private readonly AuditService _auditService;

    public LedgerService(
        AppDbContext context,
        ISessionService session,
        IBankService bankService,
        IOwnerDebtService ownerDebtService,
        ICashLedgerService cashLedgerService,
        AuditService auditService)
    {
        _context = context;
        _session = session;
        _bankService = bankService;
        _ownerDebtService = ownerDebtService;
        _cashLedgerService = cashLedgerService;
        _auditService = auditService;
    }


    public async Task PostInvoiceAsync(SupplierInvoice invoice)
    {
        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            var supplier = await _context.Suppliers.FindAsync(invoice.SupplierId);
            if (supplier == null) throw new Exception("المورد غير موجود في قاعدة البيانات.");

            // 1. إضافة الفاتورة كمدخل مبدئي
            invoice.Status = InvoiceStatus.Unpaid;
            invoice.PaidAmount = 0;
            _context.SupplierInvoices.Add(invoice);
            await _context.SaveChangesAsync(); // للحصول على المعرف (Id)

            // 2. تحديث رصيد المورد
            supplier.CurrentBalance += invoice.TotalAmount;

            // 3. توثيق الحركة المحاسبية (زيادة الالتزام المالي)
            var ledgerTx = new SupplierTransaction
            {
                SupplierId = invoice.SupplierId,
                Type = SupplierTransactionType.IncreaseDebt,
                Amount = invoice.TotalAmount,
                BalanceAfter = supplier.CurrentBalance,
                SourceType = TransactionSourceType.Invoice,
                SourceId = invoice.Id,
                SupplierInvoiceId = invoice.Id,
                TransactionDate = invoice.InvoiceDate
            };

            _context.SupplierTransactions.Add(ledgerTx);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// تسجيل دفعة سداد لمورد.
    /// </summary>
    /// <remarks>
    /// SourceId يحمل معنى مزدوجاً في SupplierTransaction:
    /// - عند الإنشاء: معرف المصدر الأصلي (sourceId parameter)
    /// - بعد ربط المصروف/الدين: يُستبدل بمعرف GeneralExpense أو OwnerDebt
    /// هذا السلوك مقصود لربط الحركة بالكيان المالي النهائي.
    /// </remarks>
    public async Task PostPaymentAsync(int supplierId, decimal amount, TransactionSourceType source, int sourceId, DateTime paymentDate, int? targetInvoiceId = null, string? receiptNumber = null, string? notes = null, int? bankAccountId = null, string? partnerName = null, string? bankReferenceNumber = null)
    {
        if (amount <= 0)
            throw new ArgumentException("مبلغ الدفعة يجب أن يكون أكبر من صفر.");

        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            var supplier = await _context.Suppliers.FindAsync(supplierId);
            if (supplier == null) throw new Exception("المورد غير موجود في قاعدة البيانات.");

            // 1. تحديث الفاتورة إن وُجدت (تتبع الدفع الجزئي أو الكلي)
            if (targetInvoiceId.HasValue)
            {
                var invoice = await _context.SupplierInvoices.FindAsync(targetInvoiceId.Value);
                // كانت الفاتورة تُحدَّث بمعرفها وحده: دفعة لمورد على فاتورة مورد آخر تسدد فاتورة الأول
                // وتخصم رصيد الثاني
                if (invoice == null || invoice.IsDeleted)
                    throw new InvalidOperationException("الفاتورة المحددة للدفعة غير موجودة.");
                if (invoice.SupplierId != supplierId)
                    throw new InvalidOperationException("الفاتورة المحددة لا تخص هذا المورد.");
                {
                    var remaining = invoice.TotalAmount - invoice.PaidAmount;
                    if (amount > remaining)
                        throw new InvalidOperationException($"مبلغ الدفعة ({amount:N2}) أكبر من المتبقي على الفاتورة ({remaining:N2}).");

                    invoice.PaidAmount += amount;
                    if (invoice.PaidAmount >= invoice.TotalAmount)
                        invoice.Status = InvoiceStatus.Paid;
                    else if (invoice.PaidAmount > 0)
                        invoice.Status = InvoiceStatus.PartiallyPaid;
                }
            }

            // 2. تحديث رصيد المورد وتقليص المديونية
            supplier.CurrentBalance -= amount;

            // 3. توثيق الحركة المحاسبية (تخفيض الالتزام المالي)
            var ledgerTx = new SupplierTransaction
            {
                SupplierId = supplierId,
                Type = SupplierTransactionType.DecreaseDebt,
                Amount = amount,
                BalanceAfter = supplier.CurrentBalance,
                SourceType = source,
                SourceId = sourceId,
                SupplierInvoiceId = targetInvoiceId,
                TransactionDate = paymentDate,
                ReceiptNumber = receiptNumber,
                Notes = notes
            };

            _context.SupplierTransactions.Add(ledgerTx);
            await _context.SaveChangesAsync();

            // 4. ربط السداد بالمصرف أو مستحقات المالك أو المصاريف العامة
            if (bankAccountId.HasValue)
            {
                var bankTxNotes = $"سداد للمورد: {supplier.Name}" + (string.IsNullOrEmpty(notes) ? "" : $" | {notes}");
                var bankTx = await _bankService.RecordTransactionAsync(
                    bankAccountId.Value,
                    BankTransactionType.SupplierPayment,
                    amount,
                    bankReferenceNumber ?? receiptNumber,
                    bankTxNotes,
                    "SupplierTransaction",
                    ledgerTx.Id,
                    paymentDate
                );

                // إنشاء مسودة مصروف عام تحويل مصرفي
                var expenseNotes = GetCleanExpenseNotes(supplier.Name, notes);
                var expense = new GeneralExpense
                {
                    ExpenseType = GeneralExpenseType.SupplierPayment,
                    Amount = amount,
                    PaymentDate = paymentDate,
                    PaymentMethod = PaymentMethodType.BankTransfer,
                    BankAccountId = bankAccountId.Value,
                    Description = expenseNotes,
                    FinancialStatus = FinancialStatus.Draft,
                    CreatedAt = DateTime.UtcNow
                };
                _context.GeneralExpenses.Add(expense);
                await _context.SaveChangesAsync();

                ledgerTx.SourceId = expense.Id;
            }
            else if (!string.IsNullOrEmpty(partnerName))
            {
                var debtNotes = $"سداد شخصي للمورد: {supplier.Name}" + (string.IsNullOrEmpty(notes) ? "" : $" | {notes}");
                var debt = await _ownerDebtService.RecordDebtAsync(
                    partnerName,
                    amount,
                    "SupplierPayment",
                    debtNotes,
                    paymentDate,
                    "SupplierTransaction",
                    ledgerTx.Id
                );

                ledgerTx.SourceId = debt.Id;
            }
            else
            {
                // سداد نقدي من سيولة المطعم (المبالغ طرف المالك)
                // يتم تسجيله تلقائياً كمصروف عام بوضعية Draft (مسودة) لتجنب الازدواجية وتوثيق حركة السيولة الخارجة
                if (source == TransactionSourceType.ExternalPayment)
                {
                    var expenseNotes = GetCleanExpenseNotes(supplier.Name, notes);
                    var expense = new GeneralExpense
                    {
                        ExpenseType = GeneralExpenseType.SupplierPayment,
                        Amount = amount,
                        PaymentDate = paymentDate,
                        PaymentMethod = PaymentMethodType.Cash,
                        Description = expenseNotes,
                        FinancialStatus = FinancialStatus.Draft,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.GeneralExpenses.Add(expense);
                    await _context.SaveChangesAsync();

                    ledgerTx.SourceId = expense.Id;

                    await _cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashOut,
                        amount,
                        "GeneralExpense",
                        expense.Id,
                        expenseNotes,
                        paymentDate
                    );
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }


    private async Task RebuildSupplierLedgerInternalAsync(int supplierId)
    {
        var supplier = await _context.Suppliers.FindAsync(supplierId);
        if (supplier == null) throw new Exception("المورد غير موجود.");

        var transactions = await _context.SupplierTransactions
            .Where(t => t.SupplierId == supplierId && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        decimal runningBalance = supplier.OpeningBalance;

        foreach (var tx in transactions)
        {
            if (tx.Type == SupplierTransactionType.IncreaseDebt)
                runningBalance += tx.Amount;
            else if (tx.Type == SupplierTransactionType.DecreaseDebt)
                runningBalance -= tx.Amount;

            tx.BalanceAfter = runningBalance;
        }

        supplier.CurrentBalance = runningBalance;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// ملاحظة أمنية: يجب تقييد هذه الدالة لمدراء النظام فقط، حيث تقوم بإعادة بناء الدفتر وتصحيح الأرصدة.
    /// </summary>
    public async Task RebuildSupplierLedgerAsync(int supplierId)
    {
        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            await RebuildSupplierLedgerInternalAsync(supplierId);

            await _auditService.LogAsync(_session.CurrentUsername, "RebuildSupplierLedger", "Supplier", supplierId, null, "Rebuilt");

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateInvoiceAsync(SupplierInvoice invoice, List<SupplierInvoiceItem> newItems)
    {
        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            var dbInvoice = await _context.SupplierInvoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == invoice.Id);

            if (dbInvoice == null) throw new Exception("الفاتورة غير موجودة في قاعدة البيانات.");

            // تحديث الحقول الأساسية للفاتورة
            dbInvoice.InvoiceNumber = invoice.InvoiceNumber;
            dbInvoice.InvoiceDate = invoice.InvoiceDate;
            dbInvoice.TotalAmount = invoice.TotalAmount;
            dbInvoice.Notes = invoice.Notes;
            dbInvoice.UpdatedAt = DateTime.UtcNow;

            // حذف العناصر القديمة وإضافة الجديدة
            _context.SupplierInvoiceItems.RemoveRange(dbInvoice.Items);
            foreach (var item in newItems)
            {
                item.SupplierInvoiceId = dbInvoice.Id;
                item.TotalValue = item.Quantity * item.UnitPrice;
                _context.SupplierInvoiceItems.Add(item);
            }

            // إعادة حساب المدفوعات والوضع المالي للفاتورة
            var payments = await _context.SupplierTransactions
                .Where(t => t.SupplierInvoiceId == dbInvoice.Id && t.Type == SupplierTransactionType.DecreaseDebt && !t.IsDeleted)
                .ToListAsync();

            dbInvoice.PaidAmount = payments.Sum(p => p.Amount);
            if (dbInvoice.PaidAmount >= dbInvoice.TotalAmount)
                dbInvoice.Status = InvoiceStatus.Paid;
            else if (dbInvoice.PaidAmount > 0)
                dbInvoice.Status = InvoiceStatus.PartiallyPaid;
            else
                dbInvoice.Status = InvoiceStatus.Unpaid;

            // تحديث الحركة المحاسبية للفاتورة (زيادة الدين)
            var ledgerTx = await _context.SupplierTransactions
                .FirstOrDefaultAsync(t => t.SupplierInvoiceId == dbInvoice.Id && t.Type == SupplierTransactionType.IncreaseDebt && !t.IsDeleted);

            if (ledgerTx != null)
            {
                ledgerTx.Amount = dbInvoice.TotalAmount;
                ledgerTx.TransactionDate = dbInvoice.InvoiceDate;
                ledgerTx.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                ledgerTx = new SupplierTransaction
                {
                    SupplierId = dbInvoice.SupplierId,
                    Type = SupplierTransactionType.IncreaseDebt,
                    Amount = dbInvoice.TotalAmount,
                    SourceType = TransactionSourceType.Invoice,
                    SourceId = dbInvoice.Id,
                    SupplierInvoiceId = dbInvoice.Id,
                    TransactionDate = dbInvoice.InvoiceDate
                };
                _context.SupplierTransactions.Add(ledgerTx);
            }

            await _context.SaveChangesAsync();

            // إعادة بناء أرصدة الحسابات
            await RebuildSupplierLedgerInternalAsync(dbInvoice.SupplierId);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdatePaymentAsync(int transactionId, decimal amount, DateTime paymentDate, string? receiptNumber, string? notes, int? bankAccountId = null, string? partnerName = null, string? bankReferenceNumber = null)
    {
        if (amount <= 0)
            throw new ArgumentException("مبلغ الدفعة يجب أن يكون أكبر من صفر.");

        using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
        try
        {
            var ledgerTx = await _context.SupplierTransactions
                .Include(t => t.SupplierInvoice)
                .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted);

            if (ledgerTx == null) throw new Exception("الحركة المحاسبية غير موجودة.");

            // دفعات اليومية مصدرها بند في الوردية (SourceId = معرف البند): تعديلها من هنا كان يقطع الربط
            // ويعامل معرف البند كأنه معرف مصروف عام. تُعدَّل من اليومية نفسها فقط.
            if (ledgerTx.SourceType == TransactionSourceType.DailyJournalPayment)
                throw new InvalidOperationException("هذه الدفعة مسجلة من اليومية. عدّلها من شاشة اليومية نفسها.");

            var supplier = await _context.Suppliers.FindAsync(ledgerTx.SupplierId);
            if (supplier == null) throw new Exception("المورد غير موجود.");

            int? oldInvoiceId = ledgerTx.SupplierInvoiceId;
            if (ledgerTx.SupplierInvoice != null)
            {
                // المتبقي على الفاتورة بعد استبعاد هذه الدفعة نفسها
                var remaining = ledgerTx.SupplierInvoice.TotalAmount - (ledgerTx.SupplierInvoice.PaidAmount - ledgerTx.Amount);
                if (amount > remaining)
                    throw new InvalidOperationException($"مبلغ الدفعة ({amount:N2}) أكبر من المتبقي على الفاتورة ({remaining:N2}).");
            }

            // تحديث قيم الحركة المحاسبية للدفعة
            ledgerTx.Amount = amount;
            ledgerTx.TransactionDate = paymentDate;
            ledgerTx.ReceiptNumber = receiptNumber;
            ledgerTx.Notes = notes;
            ledgerTx.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // معالجة وتحديث الربط البنكي أو ديون المالك أو المصاريف العامة
            var oldBankTx = await _context.BankTransactions
                .FirstOrDefaultAsync(t => t.SourceType == "SupplierTransaction" && t.SourceId == ledgerTx.Id && !t.IsDeleted);

            var oldOwnerDebt = await _context.OwnerDebts
                .FirstOrDefaultAsync(d => d.SourceType == "SupplierTransaction" && d.SourceId == ledgerTx.Id && !d.IsDeleted);

            // SourceId يشير إلى مصروف عام فقط إذا كانت الدفعة بنكية أو نقدية خارجية. في السداد الشخصي يحمل
            // معرف دين الشريك، وفي دفعات الفواتير قد يحمل معرف الفاتورة؛ البحث عنه كمصروف كان يلتقط مصروفاً
            // آخر لا علاقة له بالدفعة فيعدّله أو يحذفه.
            GeneralExpense? oldExpense = null;
            bool sourceIsExpense = oldOwnerDebt == null
                && (oldBankTx != null || ledgerTx.SourceType == TransactionSourceType.ExternalPayment);
            if (sourceIsExpense && ledgerTx.SourceId > 0)
            {
                oldExpense = await _context.GeneralExpenses
                    .FirstOrDefaultAsync(e => e.Id == ledgerTx.SourceId &&
                                             (e.ExpenseType == GeneralExpenseType.SupplierPayment ||
                                              e.ExpenseType == GeneralExpenseType.Other) &&
                                             !e.IsDeleted);
            }

            // تحقق من عدم ترحيل المصروف مسبقاً قبل السماح بالتعديل
            if (oldExpense != null && (oldExpense.FinancialStatus == FinancialStatus.Posted || oldExpense.FinancialStatus == FinancialStatus.Archived))
            {
                throw new Exception("لا يمكن تعديل حركة السداد لأن المصروف المرتبط بها قد تم ترحيله مالياً بالفعل.");
            }

            // عكس الحركة النقدية القديمة للمصروف إن وجدت
            if (oldExpense != null)
            {
                var oldCashMovement = await _context.CashMovements.FindLiveForSourceAsync(SourceTypes.GeneralExpense, oldExpense.Id);
                if (oldCashMovement != null)
                {
                    await _cashLedgerService.ReverseMovementAsync(oldCashMovement.Id, "تعديل دفعة المورد");
                }
            }

            if (bankAccountId.HasValue)
            {
                // إذا تم تحديد حساب بنكي
                if (oldOwnerDebt != null)
                {
                    await _ownerDebtService.DeleteDebtAsync(oldOwnerDebt.Id);
                }

                var bankTxNotes = $"سداد للمورد: {supplier.Name}" + (string.IsNullOrEmpty(notes) ? "" : $" | {notes}");

                if (oldBankTx != null)
                {
                    if (oldBankTx.BankAccountId != bankAccountId.Value)
                    {
                        // إذا تغير الحساب البنكي، نقوم بحذف القديم وتسجيل حركة جديدة
                        await _bankService.DeleteTransactionBySourceAsync("SupplierTransaction", ledgerTx.Id);

                        var newBankTx = await _bankService.RecordTransactionAsync(
                            bankAccountId.Value,
                            BankTransactionType.SupplierPayment,
                            amount,
                            bankReferenceNumber ?? receiptNumber,
                            bankTxNotes,
                            "SupplierTransaction",
                            ledgerTx.Id,
                            paymentDate
                        );
                    }
                    else
                    {
                        // تحديث الحركة في نفس الحساب البنكي
                        oldBankTx.Amount = amount;
                        oldBankTx.ReferenceNumber = bankReferenceNumber ?? receiptNumber;
                        oldBankTx.Notes = bankTxNotes;
                        oldBankTx.TransactionDate = paymentDate;
                        oldBankTx.UpdatedAt = DateTime.UtcNow;

                        await _context.SaveChangesAsync();
                        await _bankService.RebuildAccountBalanceAsync(bankAccountId.Value);
                    }
                }
                else
                {
                    // تسجيل حركة بنكية جديدة
                    var newBankTx = await _bankService.RecordTransactionAsync(
                        bankAccountId.Value,
                        BankTransactionType.SupplierPayment,
                        amount,
                        bankReferenceNumber ?? receiptNumber,
                        bankTxNotes,
                        "SupplierTransaction",
                        ledgerTx.Id,
                        paymentDate
                    );
                }

                // إدارة المصروف العام المرتبط بالتحويل البنكي
                var expenseNotes = GetCleanExpenseNotes(supplier.Name, notes);
                if (oldExpense != null)
                {
                    oldExpense.ExpenseType = GeneralExpenseType.SupplierPayment;
                    oldExpense.Amount = amount;
                    oldExpense.PaymentDate = paymentDate;
                    oldExpense.PaymentMethod = PaymentMethodType.BankTransfer;
                    oldExpense.BankAccountId = bankAccountId.Value;
                    oldExpense.Description = expenseNotes;
                    oldExpense.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    var expense = new GeneralExpense
                    {
                        ExpenseType = GeneralExpenseType.SupplierPayment,
                        Amount = amount,
                        PaymentDate = paymentDate,
                        PaymentMethod = PaymentMethodType.BankTransfer,
                        BankAccountId = bankAccountId.Value,
                        Description = expenseNotes,
                        FinancialStatus = FinancialStatus.Draft,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.GeneralExpenses.Add(expense);
                    await _context.SaveChangesAsync();
                    ledgerTx.SourceId = expense.Id;
                }
            }
            else if (!string.IsNullOrEmpty(partnerName))
            {
                // إذا تم السداد شخصياً من قبل المالك
                if (oldBankTx != null)
                {
                    await _bankService.DeleteTransactionBySourceAsync("SupplierTransaction", ledgerTx.Id);
                }

                if (oldExpense != null)
                {
                    oldExpense.IsDeleted = true;
                    oldExpense.UpdatedAt = DateTime.UtcNow;
                }

                var debtNotes = $"سداد شخصي للمورد: {supplier.Name}" + (string.IsNullOrEmpty(notes) ? "" : $" | {notes}");

                if (oldOwnerDebt != null)
                {
                    oldOwnerDebt.PartnerName = partnerName;
                    oldOwnerDebt.Amount = amount;
                    oldOwnerDebt.Notes = debtNotes;
                    oldOwnerDebt.TransactionDate = paymentDate;
                    oldOwnerDebt.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    var newDebt = await _ownerDebtService.RecordDebtAsync(
                        partnerName,
                        amount,
                        "SupplierPayment",
                        debtNotes,
                        paymentDate,
                        "SupplierTransaction",
                        ledgerTx.Id
                    );
                    ledgerTx.SourceId = newDebt.Id;
                }
            }
            else
            {
                // سداد نقدي عادي، حذف أي ربط بنكي أو دين مالك سابق
                if (oldBankTx != null)
                {
                    await _bankService.DeleteTransactionBySourceAsync("SupplierTransaction", ledgerTx.Id);
                }
                if (oldOwnerDebt != null)
                {
                    await _ownerDebtService.DeleteDebtAsync(oldOwnerDebt.Id);
                }

                // معالجة المصروف العام
                if (ledgerTx.SourceType == TransactionSourceType.ExternalPayment)
                {
                    var expenseNotes = GetCleanExpenseNotes(supplier.Name, notes);
                    if (oldExpense != null)
                    {
                        oldExpense.ExpenseType = GeneralExpenseType.SupplierPayment;
                        oldExpense.Amount = amount;
                        oldExpense.PaymentDate = paymentDate;
                        oldExpense.PaymentMethod = PaymentMethodType.Cash;
                        oldExpense.BankAccountId = null;
                        oldExpense.Description = expenseNotes;
                        oldExpense.UpdatedAt = DateTime.UtcNow;

                        await _cashLedgerService.RecordMovementAsync(
                            CashMovementType.CashOut,
                            amount,
                            "GeneralExpense",
                            oldExpense.Id,
                            expenseNotes,
                            paymentDate
                        );
                    }
                    else
                    {
                        // إنشاء مصروف عام جديد إذا لم يكن موجوداً
                        var expense = new GeneralExpense
                        {
                            ExpenseType = GeneralExpenseType.SupplierPayment,
                            Amount = amount,
                            PaymentDate = paymentDate,
                            PaymentMethod = PaymentMethodType.Cash,
                            Description = expenseNotes,
                            FinancialStatus = FinancialStatus.Draft,
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.GeneralExpenses.Add(expense);
                        await _context.SaveChangesAsync();
                        ledgerTx.SourceId = expense.Id;

                        await _cashLedgerService.RecordMovementAsync(
                            CashMovementType.CashOut,
                            amount,
                            "GeneralExpense",
                            expense.Id,
                            expenseNotes,
                            paymentDate
                        );
                    }
                }
                else
                {
                    if (oldExpense != null)
                    {
                        oldExpense.IsDeleted = true;
                        oldExpense.UpdatedAt = DateTime.UtcNow;
                    }
                    ledgerTx.SourceId = 0;
                }
            }

            await _context.SaveChangesAsync();

            // إعادة حساب رصيد وحالة الفاتورة القديمة إن وُجدت
            if (oldInvoiceId.HasValue)
            {
                var invoice = await _context.SupplierInvoices.FindAsync(oldInvoiceId.Value);
                if (invoice != null)
                {
                    var invoicePayments = await _context.SupplierTransactions
                        .Where(t => t.SupplierInvoiceId == invoice.Id && t.Type == SupplierTransactionType.DecreaseDebt && !t.IsDeleted)
                        .ToListAsync();

                    invoice.PaidAmount = invoicePayments.Sum(p => p.Amount);
                    if (invoice.PaidAmount >= invoice.TotalAmount)
                        invoice.Status = InvoiceStatus.Paid;
                    else if (invoice.PaidAmount > 0)
                        invoice.Status = InvoiceStatus.PartiallyPaid;
                    else
                        invoice.Status = InvoiceStatus.Unpaid;
                }
            }

            // إعادة بناء أرصدة الحسابات للمورد
            await RebuildSupplierLedgerInternalAsync(ledgerTx.SupplierId);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }


    public async Task<List<SupplierTransaction>> GetStatementAsync(int supplierId, DateTime from, DateTime to)
    {
        return await _context.SupplierTransactions
            .Include(t => t.SupplierInvoice)
            .Where(t => t.SupplierId == supplierId && t.TransactionDate.Date >= from.Date && t.TransactionDate.Date <= to.Date)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
    }

    private string GetCleanExpenseNotes(string supplierName, string? notes)
    {
        var expenseNotes = $"اسم المستفيد: {supplierName}";
        if (string.IsNullOrEmpty(notes))
        {
            return expenseNotes;
        }

        var cleanNotes = notes;
        cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"\s*\|\s*تحويل بنكي:[^|]+", "");
        cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"\s*\|\s*سداد شخصي:[^|]+", "");
        cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"\s*\|\s*سداد نقدي", "");

        var trimmed = cleanNotes.Trim().TrimStart('|').Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            expenseNotes += $" | {trimmed}";
        }

        return expenseNotes;
    }
}
