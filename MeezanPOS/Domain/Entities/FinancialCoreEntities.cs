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

        public DateTime PostedUntilDate { get; set; }

        public int TotalAffectedRows { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Posted"; // "Posted", "Unposted"

        [MaxLength(1000)]
        public string? Notes { get; set; }

        public ICollection<PostingSessionDetail> Details { get; set; } = new List<PostingSessionDetail>();
    }

    public class PostingSessionDetail : BaseEntity
    {
        public int PostingSessionId { get; set; }
        public PostingSession? PostingSession { get; set; }

        [Required]
        [MaxLength(255)]
        public string EntityType { get; set; } = string.Empty; // e.g., "SaleHeader", "GeneralExpense"

        public int EntityId { get; set; }

        [MaxLength(50)]
        public string ActionType { get; set; } = "Posted"; // "Posted", "Unposted"
    }
}
