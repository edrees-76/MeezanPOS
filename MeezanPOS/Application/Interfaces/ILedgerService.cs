using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Interfaces;

public interface ILedgerService
{
    Task PostInvoiceAsync(SupplierInvoice invoice);
    Task PostPaymentAsync(int supplierId, decimal amount, TransactionSourceType source, int sourceId, DateTime paymentDate, int? targetInvoiceId = null, string? receiptNumber = null, string? notes = null);
    Task RebuildSupplierLedgerAsync(int supplierId);
    Task<List<SupplierTransaction>> GetStatementAsync(int supplierId, DateTime from, DateTime to);
}
