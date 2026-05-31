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

    public enum PostingSessionType
    {
        Posting,
        Settlement,
        Unposting
    }

    public enum PostingSessionStatus
    {
        Draft = 0,
        Posted = 1,
        Settled = 2,
        Unlocked = 3,
        ReSettled = 4
    }

    public enum PostingEntityType
    {
        DailyJournal = 1,
        GeneralExpense = 2
    }

    public enum PostingActionType
    {
        Posted = 1,
        Unposted = 2,
        Reposted = 3,
        Settled = 4
    }
}
