using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Domain.Entities;

public class BankAccount : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string FriendlyName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? LegalOwnerName { get; set; }

        [NotMapped]
        public string DisplayName => FriendlyName;

    public BankAccountType AccountType { get; set; } = BankAccountType.Commercial;

    [MaxLength(100)]
    public string? BankName { get; set; }

    [MaxLength(100)]
    public string? AccountNumber { get; set; }

    public decimal OpeningBalance { get; set; }
    
    public decimal CurrentBalance { get; set; }

    public bool IsActive { get; set; } = true;
}

public class BankTransaction : BaseEntity
{
    public int BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public BankTransactionType Type { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(50)]
    public string? ReferenceNumber { get; set; }

    public decimal BalanceAfter { get; set; }

    public DateTime TransactionDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [NotMapped]
    public string TransactionTypeDisplayName
    {
        get
        {
            if (Type == BankTransactionType.Deposit && SourceType == "OwnerDebt_Transfer")
                return "تحويل مصرفي";
            if (Type == BankTransactionType.Deposit && SourceType == "OwnerDebt_Cash")
                return "إيداع نقدي";
            // للتوافق مع البيانات القديمة
            if (Type == BankTransactionType.Deposit && SourceType == "OwnerDebt")
            {
                if (!string.IsNullOrWhiteSpace(ReferenceNumber) && ReferenceNumber != "تمويل شريك")
                    return "تحويل مصرفي";
                return "إيداع نقدي";
            }

            return Type switch
            {
                BankTransactionType.CardSalesDeposit => "خدمات مصرفية",
                BankTransactionType.Deposit => "إيداع نقدي",
                BankTransactionType.Withdrawal => "سحب نقدي",
                BankTransactionType.InternalTransfer => "تحويل داخلي",
                BankTransactionType.SupplierPayment => "سداد مورد",
                BankTransactionType.ExpensePayment => "مصروف عام",
                BankTransactionType.OwnerDebtSettlement => "تسوية مالك",
                BankTransactionType.ExchangeDifference => "فروقات",
                _ => "أخرى"
            };
        }
    }

    [NotMapped]
    public int SequenceNumber { get; set; }

    [NotMapped]
    public string DisplayNotes
    {
        get
        {
            if (Type == BankTransactionType.SupplierPayment && !string.IsNullOrEmpty(Notes))
            {
                string prefix = "سداد للمورد:";
                if (Notes.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string supplierPart = Notes.Substring(prefix.Length);
                    int pipeIndex = supplierPart.IndexOf('|');
                    if (pipeIndex >= 0)
                    {
                        supplierPart = supplierPart.Substring(0, pipeIndex);
                    }
                    return supplierPart.Trim();
                }
            }
            return Notes ?? string.Empty;
        }
    }

    [MaxLength(100)]
    public string? SourceType { get; set; } // e.g., "DailyJournal", "SupplierTransaction", "GeneralExpense"

    public int? SourceId { get; set; }
}

public class CardPaymentReconciliation : BaseEntity
{
    public int DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? BankName { get; set; }

    [MaxLength(50)]
    public string? ReferenceNumber { get; set; }

    public CardPaymentStatus Status { get; set; } = CardPaymentStatus.Pending;

    public DateTime? ClearedDate { get; set; }

    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public int? BankTransactionId { get; set; }
    public BankTransaction? BankTransaction { get; set; }
}

public class OwnerDebt : BaseEntity
{
    [Required]
    [MaxLength(255)]
    public string PartnerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? ExpenseCategory { get; set; } // e.g., "SupplierPayment", "Rent"

    [MaxLength(500)]
    public string? Notes { get; set; }

    public OwnerDebtStatus Status { get; set; } = OwnerDebtStatus.Unpaid;

    public DateTime TransactionDate { get; set; }

    [MaxLength(100)]
    public string? SourceType { get; set; }

    public int? SourceId { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; } // "Cash", "Transfer"

    [MaxLength(50)]
    public string? TransferReference { get; set; }
}

public class OwnerDebtSettlement : BaseEntity
{
    public int? OwnerDebtId { get; set; }
    public OwnerDebt? OwnerDebt { get; set; }

    [Required]
    [MaxLength(255)]
    public string PartnerName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public OwnerDebtSettlementSource SettlementSource { get; set; } = OwnerDebtSettlementSource.CashRegister;

    public DateTime SettlementDate { get; set; }

    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
