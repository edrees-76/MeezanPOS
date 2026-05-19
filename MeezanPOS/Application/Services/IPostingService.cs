using System;
using System.Threading.Tasks;

namespace MeezanPOS.Application.Services
{
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
    }
}
