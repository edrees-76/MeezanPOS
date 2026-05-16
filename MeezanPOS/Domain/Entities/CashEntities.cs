using MeezanPOS.Domain.Enums;
using System;
using System.Collections.Generic;

namespace MeezanPOS.Domain.Entities;

public class CashSession : BaseEntity
{
    public DateTime OpenTime { get; set; }
    public DateTime? CloseTime { get; set; }
    public ShiftStatus Status { get; set; }
    
    public decimal OpeningBalance { get; set; }
    public decimal ExpectedClosingBalance { get; set; }
    public decimal ActualClosingBalance { get; set; }
    public decimal Difference { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public ICollection<CashTransaction> Transactions { get; set; } = new List<CashTransaction>();
    public ICollection<SaleHeader> Sales { get; set; } = new List<SaleHeader>();
}

public class CashTransaction : BaseEntity
{
    public int CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }

    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReferenceType { get; set; } 
    public int? ReferenceId { get; set; }
}
