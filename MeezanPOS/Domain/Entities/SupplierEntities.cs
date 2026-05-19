using System;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Domain.Entities;

public class Supplier : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CreditLimit { get; set; }
    
    // يُحدث حصراً عبر LedgerService
    public decimal CurrentBalance { get; set; } 
    public bool IsActive { get; set; } = true;
    
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int Sequence { get; set; }
}

public class SupplierInvoice : BaseEntity
{
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal TotalAmount { get; set; }
    
    // يُحدث حصراً عبر LedgerService لضمان التطابق
    public decimal PaidAmount { get; set; } 
    public InvoiceStatus Status { get; set; } 
    public DateTime InvoiceDate { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Notes { get; set; }
}

public class SupplierTransaction : BaseEntity
{
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    
    public SupplierTransactionType Type { get; set; } 
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    
    public TransactionSourceType SourceType { get; set; } 
    public int SourceId { get; set; } 
    
    public int? SupplierInvoiceId { get; set; } 
    public SupplierInvoice? SupplierInvoice { get; set; }

    public DateTime TransactionDate { get; set; } 
    public string? ReceiptNumber { get; set; }
    public string? Notes { get; set; }
}
