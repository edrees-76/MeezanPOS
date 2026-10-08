using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services.Queries;

/// <summary>بيانات تعديل دفعة مورد: الحركة وما يرتبط بها (تحويل بنكي، دين شريك، فاتورة).</summary>
public sealed record SupplierPaymentDetails(
    SupplierTransaction Transaction,
    BankTransaction? BankTransaction,
    OwnerDebt? OwnerDebt,
    SupplierInvoice? Invoice);

/// <summary>استعلامات وحفظ بيانات الموردين لشاشتي القائمة والتفاصيل (كانت داخل الـ ViewModels).</summary>
public interface ISupplierQueryService
{
    Task<List<Supplier>> SearchAsync(string? text);
    Task SaveSupplierAsync(Supplier form);
    Task<Supplier?> GetSupplierAsync(int supplierId);
    Task<List<SupplierInvoice>> GetInvoicesAsync(int supplierId);
    Task<SupplierInvoice?> GetInvoiceWithItemsAsync(int invoiceId);
    Task<List<SupplierInvoice>> GetInvoicesWithItemsAsync(IReadOnlyCollection<int> invoiceIds);
    Task<SupplierPaymentDetails?> GetPaymentDetailsAsync(int transactionId);
    Task<DailyExpenseItem?> GetJournalExpenseItemAsync(int expenseItemId);
    /// <summary>متزامن عمداً: يُستدعى من أمر عرض متزامن.</summary>
    BankTransaction? GetBankTransferForPayment(int transactionId);
}

public sealed class SupplierQueryService : ISupplierQueryService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public SupplierQueryService(IDbContextFactory<AppDbContext>? factory = null)
        => _factory = factory ?? DefaultDbContextFactory.Instance;

    public async Task<List<Supplier>> SearchAsync(string? text)
    {
        await using var db = _factory.CreateDbContext();
        var query = db.Suppliers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(text))
            query = query.Where(s => s.Name.Contains(text) || (s.Phone != null && s.Phone.Contains(text)));
        return await query.OrderBy(s => s.Name).ToListAsync();
    }

    /// <summary>إضافة مورد أو تعديل بياناته الأساسية. الرصيدان الافتتاحي والحالي لا يُعدَّلان هنا منعاً للتلاعب.</summary>
    public async Task SaveSupplierAsync(Supplier form)
    {
        await using var db = _factory.CreateDbContext();
        if (form.Id == 0)
        {
            form.CurrentBalance = form.OpeningBalance; // الرصيد الافتتاحي هو الرصيد الحالي مبدئياً
            db.Suppliers.Add(form);
        }
        else
        {
            var existing = await db.Suppliers.FindAsync(form.Id);
            if (existing == null) throw new KeyNotFoundException("المورد غير موجود.");
            existing.Name = form.Name;
            existing.Phone = form.Phone;
            existing.CreditLimit = form.CreditLimit;
            existing.IsActive = form.IsActive;
        }
        await db.SaveChangesAsync();
    }

    public async Task<Supplier?> GetSupplierAsync(int supplierId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId);
    }

    public async Task<List<SupplierInvoice>> GetInvoicesAsync(int supplierId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.SupplierInvoices.AsNoTracking()
            .Where(i => i.SupplierId == supplierId && !i.IsDeleted)
            .OrderByDescending(i => i.InvoiceDate)
            .ThenByDescending(i => i.Id)
            .ToListAsync();
    }

    public async Task<SupplierInvoice?> GetInvoiceWithItemsAsync(int invoiceId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.SupplierInvoices.AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId);
    }

    public async Task<List<SupplierInvoice>> GetInvoicesWithItemsAsync(IReadOnlyCollection<int> invoiceIds)
    {
        if (invoiceIds.Count == 0) return new List<SupplierInvoice>();
        var ids = invoiceIds.ToList();
        await using var db = _factory.CreateDbContext();
        return await db.SupplierInvoices.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => ids.Contains(i.Id) && !i.IsDeleted)
            .OrderBy(i => i.InvoiceDate)
            .ThenBy(i => i.Id)
            .ToListAsync();
    }

    public async Task<SupplierPaymentDetails?> GetPaymentDetailsAsync(int transactionId)
    {
        await using var db = _factory.CreateDbContext();
        var trans = await db.SupplierTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId);
        if (trans == null) return null;

        BankTransaction? bankTx = null;
        OwnerDebt? debt = null;
        if (trans.SourceType != Domain.Enums.TransactionSourceType.DailyJournalPayment)
        {
            bankTx = await db.BankTransactions.AsNoTracking()
                .FirstOrDefaultAsync(t => t.SourceType == SourceTypes.SupplierTransaction && t.SourceId == trans.Id && !t.IsDeleted);
            debt = await db.OwnerDebts.AsNoTracking()
                .FirstOrDefaultAsync(d => d.SourceType == SourceTypes.SupplierTransaction && d.SourceId == trans.Id && !d.IsDeleted);
        }

        var invoice = trans.SupplierInvoiceId.HasValue
            ? await db.SupplierInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == trans.SupplierInvoiceId.Value)
            : null;
        return new SupplierPaymentDetails(trans, bankTx, debt, invoice);
    }

    public async Task<DailyExpenseItem?> GetJournalExpenseItemAsync(int expenseItemId)
    {
        await using var db = _factory.CreateDbContext();
        return await db.DailyExpenseItems.AsNoTracking().Include(e => e.DailyJournal).FirstOrDefaultAsync(e => e.Id == expenseItemId);
    }

    public BankTransaction? GetBankTransferForPayment(int transactionId)
    {
        using var db = _factory.CreateDbContext();
        return db.BankTransactions.AsNoTracking()
            .Include(t => t.BankAccount)
            .FirstOrDefault(t => t.SourceType == SourceTypes.SupplierTransaction && t.SourceId == transactionId && !t.IsDeleted);
    }
}
