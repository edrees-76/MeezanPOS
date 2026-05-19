using System;

namespace MeezanPOS.Domain.Enums
{
    public enum FinancialStatus
    {
        Draft = 0,
        Reviewed = 1,
        Posted = 2,
        Archived = 3
    }

    public enum FinancialPeriodStatus
    {
        Open = 0,
        Closed = 1
    }
}
