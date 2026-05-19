using System;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Domain.Interfaces
{
    public interface IPostableEntity
    {
        FinancialStatus FinancialStatus { get; set; }
        DateTime? PostedDate { get; set; }
        string? PostedByUserId { get; set; }
        int? PostingSessionId { get; set; }
        long RowVersion { get; set; }
    }
}
