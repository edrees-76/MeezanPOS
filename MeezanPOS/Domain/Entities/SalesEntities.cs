using MeezanPOS.Domain.Enums;
using MeezanPOS.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MeezanPOS.Domain.Entities;

public class SaleHeader : BaseEntity, IPostableEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal TotalAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? BankName { get; set; }
    public string? BankOperationNumber { get; set; }
    
    public int UserId { get; set; }
    public User? User { get; set; }

    public int CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }

    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();

    // IPostableEntity Implementation
    public FinancialStatus FinancialStatus { get; set; } = FinancialStatus.Draft;
    public DateTime? PostedDate { get; set; }
    public string? PostedByUserId { get; set; }
    public int? PostingSessionId { get; set; }
    
    [ConcurrencyCheck]
    public long RowVersion { get; set; }
}

public class SaleItem : BaseEntity
{
    public int SaleHeaderId { get; set; }
    public SaleHeader? SaleHeader { get; set; }

    public int? ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice { get; set; }
}
