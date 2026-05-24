using System;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.ViewModels;

public class PartnerSummaryDto
{
    public string PartnerName { get; set; } = string.Empty;
    public decimal DebtsTotal { get; set; }
    public decimal SettlementsTotal { get; set; }
    public decimal NetBalance { get; set; }
    public PartnerBalanceDirection BalanceDirection { get; set; }
    public string BalanceDirectionText { get; set; } = string.Empty;
}

public class PartnerStatementEntryDto
{
    public int SequenceNumber { get; set; }
    public DateTime TransactionDate { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public PartnerBalanceDirection BalanceDirection { get; set; }
    public string BalanceDirectionText { get; set; } = string.Empty;
    public bool IsSettlement { get; set; }
    public string? SourceType { get; set; }
    public int? SourceId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string? PaymentMethod { get; set; }
    public string? TransferReference { get; set; }
    public bool CanDelete { get; set; }
}
