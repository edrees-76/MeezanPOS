namespace MeezanPOS.Domain.Enums;

/// <summary>
/// ثوابت أنواع المصادر المستخدمة لربط الحركات المالية بمصادرها.
/// يمنع الأخطاء الإملائية ويوفر مرجعاً مركزياً واحداً.
/// </summary>
public static class SourceTypes
{
    public const string DailyJournal = "DailyJournal";
    public const string GeneralExpense = "GeneralExpense";
    public const string SupplierTransaction = "SupplierTransaction";
    public const string SupplierPayment = "SupplierPayment";
    public const string CardReconciliation = "CardReconciliation";
    public const string InternalTransfer = "InternalTransfer";
    public const string Manual = "Manual";
    public const string OwnerDebt = "OwnerDebt";
    public const string OwnerDebtTransfer = "OwnerDebt_Transfer";
    public const string OwnerDebtCash = "OwnerDebt_Cash";
    public const string OwnerDebtSettlement = "OwnerDebtSettlement";
    public const string OwnerWithdrawal = "OwnerWithdrawal";
    public const string Bank = "Bank";
    public const string Cash = "Cash";
    public const string ExternalPayment = "ExternalPayment";
}

/// <summary>
/// ثوابت حالات جلسات الترحيل المالي.
/// </summary>
public static class PostingStatuses
{
    public const string Posted = "Posted";
    public const string Unposted = "Unposted";
    public const string Unpost = "Unpost";
    public const string Settle = "Settle";
}
