using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Domain.Entities
{
    public class FinancialPeriod : BaseEntity
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public FinancialPeriodStatus Status { get; set; } = FinancialPeriodStatus.Open;
    }

    public class PostingSession : BaseEntity
    {
        public Guid SessionId { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(255)]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime PeriodStartDate { get; set; }
        public DateTime PeriodEndDate { get; set; }

        public int TotalAffectedRows { get; set; }

        public PostingSessionType SessionType { get; set; } = PostingSessionType.Posting;
        public PostingSessionStatus Status { get; set; } = PostingSessionStatus.Posted;

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [ConcurrencyCheck]
        public long RowVersion { get; set; }

        public ICollection<PostingSessionDetail> Details { get; set; } = new List<PostingSessionDetail>();
        public ICollection<PeriodUnlockHistory> UnlockHistories { get; set; } = new List<PeriodUnlockHistory>();
    }

    public class PostingSessionDetail : BaseEntity
    {
        public int PostingSessionId { get; set; }
        public PostingSession? PostingSession { get; set; }

        public PostingEntityType EntityType { get; set; } = PostingEntityType.DailyJournal;

        public int EntityId { get; set; }

        public PostingActionType ActionType { get; set; } = PostingActionType.Posted;

        public decimal TransactionAmount { get; set; }
        public int? CashMovementId { get; set; }
    }

    public class PeriodUnlockHistory : BaseEntity
    {
        public int PostingSessionId { get; set; }
        public PostingSession? PostingSession { get; set; }

        public int UnlockSequence { get; set; }

        [Required]
        [MaxLength(255)]
        public string UnlockedBy { get; set; } = string.Empty;

        public DateTime UnlockDate { get; set; }

        public PostingSessionStatus PreviousStatus { get; set; }

        [Required]
        [MaxLength(255)]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string DetailReason { get; set; } = string.Empty;

        public bool IsReLocked { get; set; }
        public DateTime? ReLockedDate { get; set; }
    }
}
