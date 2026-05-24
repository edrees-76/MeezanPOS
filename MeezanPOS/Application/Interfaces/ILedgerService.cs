using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Interfaces;

public interface ILedgerService
{
    Task PostInvoiceAsync(SupplierInvoice invoice);
    Task PostPaymentAsync(int supplierId, decimal amount, TransactionSourceType source, int sourceId, DateTime paymentDate, int? targetInvoiceId = null, string? receiptNumber = null, string? notes = null, int? bankAccountId = null, string? partnerName = null, string? bankReferenceNumber = null);
    Task RebuildSupplierLedgerAsync(int supplierId);
    Task<List<SupplierTransaction>> GetStatementAsync(int supplierId, DateTime from, DateTime to);
    Task UpdateInvoiceAsync(SupplierInvoice invoice, List<SupplierInvoiceItem> newItems);
    Task UpdatePaymentAsync(int transactionId, decimal amount, DateTime paymentDate, string? receiptNumber, string? notes, int? bankAccountId = null, string? partnerName = null, string? bankReferenceNumber = null);
}

