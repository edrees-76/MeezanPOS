using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MeezanPOS.Application.Services
{
    public class SettlementHistoryItem
    {
        public int SessionId { get; set; }
        public Guid SessionGuid { get; set; }
        public string ShortSessionGuid => SessionGuid.ToString().Substring(0, 8).ToUpper();
        public DateTime SettleDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public decimal PayoutAmount { get; set; }
        public decimal KeepAmount { get; set; }
        public decimal BalanceBefore => PayoutAmount + KeepAmount;
        public int AffectedCount { get; set; }
        public string Notes { get; set; } = string.Empty;
        public string? PdfPath { get; set; }
        public Domain.Enums.PostingSessionStatus Status { get; set; }
        public string StatusText => Status switch
        {
            Domain.Enums.PostingSessionStatus.Settled => "مغلق ومسوى",
            Domain.Enums.PostingSessionStatus.Unlocked => "ملغى القفل للمراجعة",
            Domain.Enums.PostingSessionStatus.ReSettled => "معاد تسويته",
            _ => Status.ToString()
        };
    }

    public interface IPostingService
    {
        /// <summary>
        /// ترحيل حركة مفردة
        /// </summary>
        Task<bool> PostEntityAsync<T>(int entityId, string postedByUserId) where T : class;

        /// <summary>
        /// فك ترحيل حركة (عملية حرجة تتطلب سبباً)
        /// </summary>
        Task<bool> UnpostEntityAsync<T>(int entityId, string reason, string unpostedByUserId) where T : class;

        /// <summary>
        /// ترحيل مجموعة من الحركات دفعة واحدة ضمن فترة زمنية (جلسة ترحيل)
        /// </summary>
        Task<Guid> CreatePostingSessionAsync(DateTime untilDate, string postedByUserId, string notes);

        /// <summary>
        /// ترحيل جماعي مخصص لورديات العمل اليومية مع التحقق المحاسبي المتقدم وتوليد استجابة ترحيل متكاملة
        /// </summary>
        Task<PostingBatchResult> PostDailyJournalsBatchAsync(List<int> journalIds, string postedByUserId, string notes);

        /// <summary>
        /// ترحيل جماعي مخصص للمصاريف العامة مع التحقق المحاسبي وتوليد استجابة ترحيل متكاملة
        /// </summary>
        Task<PostingBatchResult> PostGeneralExpensesBatchAsync(List<int> expenseIds, string postedByUserId, string notes);

        /// <summary>
        /// تسوية نقدية وإقفال دوري متدحرج عند الطلب وتوليد استجابة تسوية مجمعة
        /// </summary>
        Task<PostingBatchResult> SettleAndLockPeriodAsync(decimal payoutAmount, decimal keepAmount, string notes, string postedByUserId);

        /// <summary>
        /// جلب سجل تسويات كاش المالك السابقة
        /// </summary>
        Task<List<SettlementHistoryItem>> GetSettlementHistoryAsync();

        /// <summary>
        /// إعادة توليد ملف PDF لإيصال تسوية سابق
        /// </summary>
        Task<string> RegenerateSettlementPdfAsync(int sessionId);

        /// <summary>
        /// إلغاء قفل فترة مالية مسواة للسماح بتعديلها
        /// </summary>
        Task<bool> UnlockPeriodAsync(int sessionId, string reason, string detailReason, string unlockedByUserId);

        /// <summary>
        /// فك ترحيل فترة بالكامل كحزمة واحدة وعكس حركاتها النقدية مجمعة
        /// </summary>
        Task<bool> UnpostPeriodAsync(int sessionId, string reason, string unpostedByUserId);
    }
}
