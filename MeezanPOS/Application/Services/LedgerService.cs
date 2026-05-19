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

    public LedgerService(AppDbContext context)
    {
        _context = context;
    }

    public async Task PostInvoiceAsync(SupplierInvoice invoice)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
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

    public async Task PostPaymentAsync(int supplierId, decimal amount, TransactionSourceType source, int sourceId, DateTime paymentDate, int? targetInvoiceId = null, string? receiptNumber = null, string? notes = null)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var supplier = await _context.Suppliers.FindAsync(supplierId);
            if (supplier == null) throw new Exception("المورد غير موجود في قاعدة البيانات.");

            // 1. تحديث الفاتورة إن وُجدت (تتبع الدفع الجزئي أو الكلي)
            if (targetInvoiceId.HasValue)
            {
                var invoice = await _context.SupplierInvoices.FindAsync(targetInvoiceId.Value);
                if (invoice != null)
                {
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

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// ملاحظة أمنية: يجب تقييد هذه الدالة لمدراء النظام فقط، حيث تقوم بإعادة بناء الدفتر وتصحيح الأرصدة.
    /// </summary>
    public async Task RebuildSupplierLedgerAsync(int supplierId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var supplier = await _context.Suppliers.FindAsync(supplierId);
            if (supplier == null) throw new Exception("المورد غير موجود.");

            var transactions = await _context.SupplierTransactions
                .Where(t => t.SupplierId == supplierId)
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
}
