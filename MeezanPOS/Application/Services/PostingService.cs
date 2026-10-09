using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Interfaces;

namespace MeezanPOS.Application.Services
{
    public class PostingService : IPostingService
    {
        private readonly AppDbContext _context;
        private readonly ICashLedgerService _cashLedgerService;
        private readonly AuditService _auditService;
        private readonly ISessionService? _session;

        /// <param name="session">
        /// للتحقق من الصلاحيات داخل الخدمة نفسها (وليس في الواجهة فقط). يُمرَّر دائماً من حاوية الخدمات؛
        /// يكون null فقط عند إنشاء الخدمة يدوياً في الاختبارات.
        /// </param>
        private readonly IAuthenticationService? _auth;

        public PostingService(AppDbContext context, ICashLedgerService cashLedgerService, AuditService auditService, ISessionService? session = null,
            IAuthenticationService? auth = null)
        {
            _auth = auth;
            _session = session;
            _context = context;
            _cashLedgerService = cashLedgerService;
            _auditService = auditService;
        }

        public async Task<bool> PostEntityAsync<T>(int entityId, string postedByUserId) where T : class
        {
            using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
            try
            {
                var entity = await _context.Set<T>().FindAsync(entityId);

                if (entity == null)
                    throw new Exception("الحركة غير موجودة.");

                if (entity is not IPostableEntity postableEntity)
                    throw new Exception("هذا الجدول لا يدعم نظام الترحيل المالي.");

                if (postableEntity.FinancialStatus == FinancialStatus.Posted || postableEntity.FinancialStatus == FinancialStatus.Archived)
                    throw new Exception("لا يمكن ترحيل حركة مرحّلة مسبقاً.");

                await EnsureEntityPeriodOpenAsync(entity);

                // تحديث الحالة المالية
                postableEntity.FinancialStatus = FinancialStatus.Posted;
                postableEntity.PostedDate = DateTime.UtcNow;
                postableEntity.PostedByUserId = postedByUserId;
                postableEntity.RowVersion++;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    await SelfHealEntityAsync(entityId, entity, activeEntity =>
                    {
                        activeEntity.FinancialStatus = FinancialStatus.Posted;
                        activeEntity.PostedDate = DateTime.UtcNow;
                        activeEntity.PostedByUserId = postedByUserId;
                    });
                }

                decimal transactionAmt = 0;
                if (entity is DailyJournal finalJournal)
                {
                    transactionAmt = finalJournal.ActualCash - finalJournal.CashFloat;
                    await RecordJournalCashAsync(finalJournal,
                        $"ترحيل وردية: {finalJournal.ShiftName} - الموظف: {finalJournal.EmployeeName}");

                    await CheckAndAutoReSettleSessionAsync(finalJournal.JournalDate);
                }
                else if (entity is GeneralExpense expense)
                {
                    transactionAmt = expense.Amount;
                    await CheckAndAutoReSettleSessionAsync(expense.PaymentDate);
                }

                await _auditService.LogAsync(postedByUserId, "Post", typeof(T).Name, entityId, "Draft", "Posted | Qty: " + transactionAmt);

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> UnpostEntityAsync<T>(int entityId, string reason, string unpostedByUserId) where T : class
        {
            _session?.RequirePermission(Permissions.UnpostFinancial);

            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 20)
                throw new Exception("يجب إدخال سبب معتمد وشرح لا يقل عن 20 حرفاً لفك الترحيل المالي.");

            using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
            try
            {
                var entity = await _context.Set<T>().FindAsync(entityId);

                if (entity == null)
                    throw new Exception("الحركة غير موجودة.");

                if (entity is not IPostableEntity postableEntity)
                    throw new Exception("هذا الجدول لا يدعم نظام الترحيل المالي.");

                if (postableEntity.FinancialStatus != FinancialStatus.Posted)
                    throw new Exception("لا يمكن فك ترحيل حركة غير مرحّلة.");

                // القفل حسب التاريخ يشمل أيضاً السجلات المرحلة فردياً داخل الفترة المسواة
                await EnsureEntityPeriodOpenAsync(entity);

                // تحقق إذا كانت الوردية تابعة لجلسة مغلقة ولم يلغى قفلها
                if (postableEntity.PostingSessionId.HasValue)
                {
                    var parentSession = await _context.PostingSessions.FindAsync(postableEntity.PostingSessionId.Value);
                    if (parentSession != null &&
                        (parentSession.Status == PostingSessionStatus.Settled || parentSession.Status == PostingSessionStatus.ReSettled))
                    {
                        throw new Exception("هذا السجل يقع ضمن فترة مقفلة ومسواة مالياً. يجب إلغاء قفل الفترة أولاً.");
                    }
                }

                // تسجيل الـ Audit لفك الترحيل في الجلسات كإجراء عكسي
                var auditSession = new PostingSession
                {
                    CreatedBy = unpostedByUserId,
                    PeriodStartDate = DateTime.Now,
                    PeriodEndDate = DateTime.Now,
                    TotalAffectedRows = 1,
                    SessionType = PostingSessionType.Unposting,
                    Status = PostingSessionStatus.Unlocked,
                    Notes = $"فك ترحيل بسبب: {reason}"
                };

                _context.PostingSessions.Add(auditSession);
                await _context.SaveChangesAsync(); // للحصول على الـ Id

                decimal originalAmt = 0;
                if (entity is DailyJournal journal)
                {
                    originalAmt = journal.ActualCash - journal.CashFloat;
                }
                else if (entity is GeneralExpense exp)
                {
                    originalAmt = exp.Amount;
                }

                var auditDetail = new PostingSessionDetail
                {
                    PostingSessionId = auditSession.Id,
                    EntityType = typeof(T).Name == "DailyJournal" ? PostingEntityType.DailyJournal : PostingEntityType.GeneralExpense,
                    EntityId = entityId,
                    ActionType = PostingActionType.Unposted,
                    TransactionAmount = originalAmt
                };

                _context.PostingSessionDetails.Add(auditDetail);

                // إرجاع الحالة
                postableEntity.FinancialStatus = FinancialStatus.Draft;
                postableEntity.PostedDate = null;
                postableEntity.PostingSessionId = null; // فصلها عن جلسة الترحيل القديمة
                postableEntity.RowVersion++;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    await SelfHealEntityAsync(entityId, entity, activeEntity =>
                    {
                        activeEntity.FinancialStatus = FinancialStatus.Draft;
                        activeEntity.PostedDate = null;
                        activeEntity.PostingSessionId = null;
                    });
                }

                // حركة خزينة الوردية تُسجَّل عند الترحيل، لذا تُعكس عند فك الترحيل.
                // أما المصروف العام فحركته النقدية تُسجَّل عند إنشائه (وليس عند ترحيله)،
                // لذا فك ترحيله لا يمس الخزينة؛ تُعكس حركته فقط عند تعديله أو حذفه.
                if (entity is DailyJournal journalObj)
                {
                    var oldMovement = await _context.CashMovements
                        .FindLiveForSourceAsync(SourceTypes.DailyJournal, journalObj.Id);
                    if (oldMovement != null)
                    {
                        await _cashLedgerService.ReverseMovementAsync(oldMovement.Id, $"إلغاء ترحيل الوردية: {reason}");
                        auditDetail.CashMovementId = oldMovement.Id;
                    }
                }

                await _auditService.LogAsync(unpostedByUserId, "Unpost", typeof(T).Name, entityId, "Posted | Qty: " + originalAmt, "Draft | Reason: " + reason);

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        [Obsolete("سيتم بناء الترحيل الجماعي (Batch) في المرحلة القادمة.")]
        public async Task<Guid> CreatePostingSessionAsync(DateTime untilDate, string postedByUserId, string notes)
        {
            // هذه دالة مستقبلية ستتولى تجميع كافة الحركات (مبيعات، مصاريف)
            // التي تمت قبل التاريخ المحدد وترحيلها دفعة واحدة داخل جلسة واحدة (PostingSession).
            // سيتم تنفيذ الـ Batch Update هنا لاحقاً باستخدام ExecuteUpdateAsync للأداء العالي.
            await Task.CompletedTask;
            throw new NotImplementedException("سيتم بناء الترحيل الجماعي (Batch) في المرحلة القادمة.");
        }

        public async Task<PostingBatchResult> PostDailyJournalsBatchAsync(List<int> journalIds, string postedByUserId, string notes)
        {
            var result = new PostingBatchResult
            {
                Success = false,
                CorrelationId = Guid.NewGuid(),
                Errors = new List<string>()
            };

            if (journalIds == null || journalIds.Count == 0)
            {
                result.Errors.Add("لم يتم تحديد أي يوميات للترجيل.");
                return result;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var transaction = await _context.Database.BeginOrJoinTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var ids = journalIds.ToHashSet();

                // تحميل الكيانات وتفاصيلها دفعة واحدة من قاعدة البيانات
                var journals = await _context.DailyJournals
                    .Include(j => j.ExpenseItems)
                    .Include(j => j.BankingItems)
                    .Include(j => j.Adjustments)
                    .Where(j => ids.Contains(j.Id))
                    .ToListAsync();

                // التحقق من وجود جميع اليوميات المطلوبة
                if (journals.Count != ids.Count)
                {
                    var foundIds = journals.Select(j => j.Id).ToHashSet();
                    var missingIds = ids.Where(id => !foundIds.Contains(id)).ToList();
                    throw new Exception($"بعض اليوميات غير موجودة في النظام: {string.Join(", ", missingIds)}");
                }

                // التحقق المحاسبي: يجب أن تكون جميع اليوميات في حالة Draft
                var nonDraftJournals = journals.Where(j => j.FinancialStatus != FinancialStatus.Draft).ToList();
                if (nonDraftJournals.Any())
                {
                    var nonDraftIds = nonDraftJournals.Select(j => j.Id).ToList();
                    throw new Exception($"لا يمكن ترحيل يوميات تمت معالجتها أو ترحيلها مسبقاً. معرفات اليوميات المخالفة: {string.Join(", ", nonDraftIds)}");
                }

                // حساب إجمالي الجلسة
                decimal totalSales = journals.Sum(j => j.TotalSales);
                decimal totalExpenses = journals.Sum(j => j.TotalExpenses);
                DateTime maxJournalDate = journals.Any() ? journals.Max(j => j.JournalDate) : DateTime.UtcNow;
                DateTime minJournalDate = journals.Any() ? journals.Min(j => j.JournalDate) : DateTime.UtcNow;

                // 1. إنشاء الجلسة وإدخالها للحصول على المعرف التلقائي
                var session = new PostingSession
                {
                    CreatedBy = postedByUserId,
                    PeriodStartDate = minJournalDate,
                    PeriodEndDate = maxJournalDate,
                    TotalAffectedRows = journals.Count,
                    SessionType = PostingSessionType.Posting,
                    Status = PostingSessionStatus.Posted,
                    Notes = notes
                };

                _context.PostingSessions.Add(session);
                await _context.SaveChangesAsync(); // حفظ الجلسة أولاً للحصول على Id

                // 2. تحديث الحالات وربط اليوميات وتوليد تفاصيل الجلسة في الذاكرة
                foreach (var journal in journals)
                {
                    journal.FinancialStatus = FinancialStatus.Posted;
                    journal.PostedDate = DateTime.UtcNow;
                    journal.PostedByUserId = postedByUserId;
                    journal.PostingSessionId = session.Id;
                    journal.RowVersion++;

                    var detail = new PostingSessionDetail
                    {
                        PostingSessionId = session.Id,
                        EntityType = PostingEntityType.DailyJournal,
                        EntityId = journal.Id,
                        ActionType = PostingActionType.Posted,
                        TransactionAmount = journal.ActualCash - journal.CashFloat
                    };
                    _context.PostingSessionDetails.Add(detail);
                }

                // 3. الحفظ الموحد للورديات وتفاصيل الجلسة
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    await SelfHealBatchAsync(journals, j => j.Id, activeJournal =>
                    {
                        activeJournal.FinancialStatus = FinancialStatus.Posted;
                        activeJournal.PostedDate = DateTime.UtcNow;
                        activeJournal.PostedByUserId = postedByUserId;
                        activeJournal.PostingSessionId = session.Id;
                    });
                }

                foreach (var journal in journals)
                {
                    await RecordJournalCashAsync(journal,
                        $"إيراد وردية: {journal.ShiftName} - الكاشير: {journal.EmployeeName}");

                    await CheckAndAutoReSettleSessionAsync(journal.JournalDate);
                }

                foreach (var journal in journals)
                {
                    await _auditService.LogAsync(postedByUserId, "PostBatch", "DailyJournal", journal.Id, "Draft", "Posted");
                }

                await transaction.CommitAsync();

                stopwatch.Stop();
                result.Success = true;
                result.SessionGuid = session.SessionId;
                result.PostedCount = journals.Count;
                result.TotalSales = totalSales;
                result.TotalExpenses = totalExpenses;
                result.Duration = stopwatch.Elapsed;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                result.Success = false;
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        public async Task<PostingBatchResult> PostGeneralExpensesBatchAsync(List<int> expenseIds, string postedByUserId, string notes)
        {
            var result = new PostingBatchResult
            {
                Success = false,
                CorrelationId = Guid.NewGuid(),
                Errors = new List<string>()
            };

            if (expenseIds == null || expenseIds.Count == 0)
            {
                result.Errors.Add("لم يتم تحديد أي مصاريف للترحيل.");
                return result;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var transaction = await _context.Database.BeginOrJoinTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var ids = expenseIds.ToHashSet();

                // تحميل الكيانات دفعة واحدة من قاعدة البيانات
                var expenses = await _context.GeneralExpenses
                    .Where(e => ids.Contains(e.Id))
                    .ToListAsync();

                // التحقق من وجود جميع المصاريف المطلوبة
                if (expenses.Count != ids.Count)
                {
                    var foundIds = expenses.Select(e => e.Id).ToHashSet();
                    var missingIds = ids.Where(id => !foundIds.Contains(id)).ToList();
                    throw new Exception($"بعض المصاريف غير موجودة في النظام: {string.Join(", ", missingIds)}");
                }

                // التحقق المحاسبي: يجب أن تكون جميع المصاريف في حالة Draft
                var nonDraftExpenses = expenses.Where(e => e.FinancialStatus != FinancialStatus.Draft).ToList();
                if (nonDraftExpenses.Any())
                {
                    var nonDraftIds = nonDraftExpenses.Select(e => e.Id).ToList();
                    throw new Exception($"لا يمكن ترحيل مصاريف تمت معالجتها أو ترحيلها مسبقاً. معرفات المصاريف المخالفة: {string.Join(", ", nonDraftIds)}");
                }

                // حساب إجمالي الجلسة
                decimal totalExpenses = expenses.Sum(e => e.Amount);
                DateTime maxExpenseDate = expenses.Any() ? expenses.Max(e => e.PaymentDate) : DateTime.UtcNow;
                DateTime minExpenseDate = expenses.Any() ? expenses.Min(e => e.PaymentDate) : DateTime.UtcNow;

                // 1. إنشاء الجلسة وإدخالها للحصول على المعرف التلقائي
                var session = new PostingSession
                {
                    CreatedBy = postedByUserId,
                    PeriodStartDate = minExpenseDate,
                    PeriodEndDate = maxExpenseDate,
                    TotalAffectedRows = expenses.Count,
                    SessionType = PostingSessionType.Posting,
                    Status = PostingSessionStatus.Posted,
                    Notes = notes
                };

                _context.PostingSessions.Add(session);
                await _context.SaveChangesAsync(); // حفظ الجلسة أولاً للحصول على Id

                // 2. تحديث الحالات وربط المصاريف وتوليد تفاصيل الجلسة في الذاكرة
                foreach (var expense in expenses)
                {
                    expense.FinancialStatus = FinancialStatus.Posted;
                    expense.PostedDate = DateTime.UtcNow;
                    expense.PostedByUserId = postedByUserId;
                    expense.PostingSessionId = session.Id;
                    expense.RowVersion++;

                    var detail = new PostingSessionDetail
                    {
                        PostingSessionId = session.Id,
                        EntityType = PostingEntityType.GeneralExpense,
                        EntityId = expense.Id,
                        ActionType = PostingActionType.Posted,
                        TransactionAmount = expense.Amount
                    };
                    _context.PostingSessionDetails.Add(detail);
                }

                // 3. الحفظ الموحد للمصاريف وتفاصيل الجلسة
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    await SelfHealBatchAsync(expenses, e => e.Id, activeExpense =>
                    {
                        activeExpense.FinancialStatus = FinancialStatus.Posted;
                        activeExpense.PostedDate = DateTime.UtcNow;
                        activeExpense.PostedByUserId = postedByUserId;
                        activeExpense.PostingSessionId = session.Id;
                    });
                }

                foreach (var expense in expenses)
                {
                    await _auditService.LogAsync(postedByUserId, "PostBatch", "GeneralExpense", expense.Id, "Draft", "Posted");
                    await CheckAndAutoReSettleSessionAsync(expense.PaymentDate);
                }

                await transaction.CommitAsync();

                stopwatch.Stop();
                result.Success = true;
                result.SessionGuid = session.SessionId;
                result.PostedCount = expenses.Count;
                result.TotalSales = 0;
                result.TotalExpenses = totalExpenses;
                result.Duration = stopwatch.Elapsed;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                result.Success = false;
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        public async Task<PostingBatchResult> SettleAndLockPeriodAsync(decimal payoutAmount, decimal keepAmount, string notes, string postedByUserId)
        {
            var result = new PostingBatchResult
            {
                Success = false,
                CorrelationId = Guid.NewGuid(),
                Errors = new List<string>()
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var transaction = await _context.Database.BeginOrJoinTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                // 1. ترحيل وتأكيد كافة اليوميات غير المرحلة حالياً لإدخال مبالغها في الخزينة
                var draftJournals = await _context.DailyJournals
                    .Include(j => j.ExpenseItems)
                    .Include(j => j.BankingItems)
                    .Include(j => j.Adjustments)
                    .Where(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate <= DateTime.Now)
                    .ToListAsync();

                var draftExpenses = await _context.GeneralExpenses
                    .Where(e => e.FinancialStatus == FinancialStatus.Draft && e.PaymentDate <= DateTime.Now)
                    .ToListAsync();

                var totalSalesVal = draftJournals.Sum(j => j.TotalSales);
                var totalExpensesVal = draftExpenses.Sum(e => e.Amount) + draftJournals.Sum(j => j.TotalExpenses);

                // حدود الفترة من تواريخ السجلات فقط: البدء بالوقت الحالي كان يمد الفترة المقفلة حتى اليوم
                // (فيُقفل اليوم الجاري) إذا لم توجد إلا مصروفات قديمة
                var periodDates = draftJournals.Select(j => j.JournalDate)
                    .Concat(draftExpenses.Select(e => e.PaymentDate))
                    .ToList();
                DateTime minDate = periodDates.Count > 0 ? periodDates.Min() : DateTime.Today;
                DateTime maxDate = periodDates.Count > 0 ? periodDates.Max() : DateTime.Today;

                // 2. إنشاء الجلسة والإقفال للتسوية
                var session = new PostingSession
                {
                    CreatedBy = postedByUserId,
                    PeriodStartDate = minDate,
                    PeriodEndDate = maxDate,
                    TotalAffectedRows = draftJournals.Count + draftExpenses.Count,
                    SessionType = PostingSessionType.Settlement,
                    Status = PostingSessionStatus.Settled,
                    Notes = $"تسوية نقدية وإقفال دوري | سحب المالك: {payoutAmount:N2} د.ل | الاحتفاظ: {keepAmount:N2} د.ل | البيان: {notes}"
                };

                _context.PostingSessions.Add(session);
                await _context.SaveChangesAsync();

                // 3. ترحيل اليوميات (مع تسجيل حركات الوارد النقدي التابع لها بالخزينة)
                foreach (var journal in draftJournals)
                {
                    journal.FinancialStatus = FinancialStatus.Posted;
                    journal.PostedDate = DateTime.UtcNow;
                    journal.PostedByUserId = postedByUserId;
                    journal.PostingSessionId = session.Id;
                    journal.RowVersion++;

                    var detail = new PostingSessionDetail
                    {
                        PostingSessionId = session.Id,
                        EntityType = PostingEntityType.DailyJournal,
                        EntityId = journal.Id,
                        ActionType = PostingActionType.Settled,
                        TransactionAmount = journal.ActualCash - journal.CashFloat
                    };
                    _context.PostingSessionDetails.Add(detail);

                    // تسجيل حركة الوارد النقدي بالخزينة للوردية
                    await RecordJournalCashAsync(journal,
                        $"إيراد وردية (إقفال وتسوية): {journal.ShiftName} - الكاشير: {journal.EmployeeName}");
                }

                // 4. ترحيل المصاريف العامة
                foreach (var expense in draftExpenses)
                {
                    expense.FinancialStatus = FinancialStatus.Posted;
                    expense.PostedDate = DateTime.UtcNow;
                    expense.PostedByUserId = postedByUserId;
                    expense.PostingSessionId = session.Id;
                    expense.RowVersion++;

                    var detail = new PostingSessionDetail
                    {
                        PostingSessionId = session.Id,
                        EntityType = PostingEntityType.GeneralExpense,
                        EntityId = expense.Id,
                        ActionType = PostingActionType.Settled,
                        TransactionAmount = expense.Amount
                    };
                    _context.PostingSessionDetails.Add(detail);
                }

                await _context.SaveChangesAsync();

                // 5. التحقق من السيولة النقدية المتوفرة بالخزينة بعد الترحيل
                decimal currentBalance = await _cashLedgerService.GetCurrentBalanceAsync();
                if (payoutAmount > currentBalance)
                {
                    throw new Exception($"المبلغ المطلوب تسليمه للمالك ({payoutAmount:N2} د.ل) يتجاوز السيولة النقدية المتوفرة حالياً بالخزينة ({currentBalance:N2} د.ل).");
                }

                // 6. تسجيل حركة مسحوبات المالك لتصفية أو خفض رصيد الصندوق
                if (payoutAmount > 0)
                {
                    await _cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashOut,
                        payoutAmount,
                        "OwnerWithdrawal",
                        session.Id,
                        $"سحب سيولة وتسليم كاش للمالك | البيان: {notes}",
                        DateTime.UtcNow
                    );
                }

                await _auditService.LogAsync(postedByUserId, "SettleAndLockPeriod", "FinancialPeriod", session.Id, "Active", "Settled");

                await transaction.CommitAsync();

                stopwatch.Stop();
                result.Success = true;
                result.SessionGuid = session.SessionId;
                result.PostedCount = draftJournals.Count + draftExpenses.Count;
                result.TotalSales = totalSalesVal;
                result.TotalExpenses = totalExpensesVal;
                result.Duration = stopwatch.Elapsed;
                result.BalanceBefore = currentBalance;
                result.PayoutAmount = payoutAmount;
                result.KeepAmount = keepAmount;

                // 7. توليد التقرير المالي للتسوية بصيغة PDF وتخزينه في المجلد المؤقت (خارج المعاملة لحماية سلامة البيانات)
                try
                {
                    string fileName = $"إيصال_تسوية_مالك_{session.SessionId.ToString().Substring(0, 8).ToUpper()}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                    string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

                    SettlementPdfReport.GeneratePdf(
                        tempPath,
                        payoutAmount,
                        keepAmount,
                        currentBalance,
                        notes,
                        postedByUserId,
                        draftJournals.Count + draftExpenses.Count,
                        totalSalesVal,
                        totalExpensesVal,
                        session.SessionId
                    );
                    result.PdfPath = tempPath;
                }
                catch (Exception pdfEx)
                {
                    result.Errors.Add($"تمت التسوية المالية بنجاح، ولكن فشل توليد ملف PDF: {pdfEx.Message}");
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                result.Success = false;
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        public async Task<List<SettlementHistoryItem>> GetSettlementHistoryAsync()
        {
            var sessions = await _context.PostingSessions
                .Where(s => s.SessionType == PostingSessionType.Settlement)
                .OrderByDescending(s => s.PeriodEndDate)
                .ToListAsync();

            var sessionIds = sessions.Select(s => s.Id).ToList();

            // 1. جلب حركات مسحوبات المالك المرتبطة بكل الجلسات دفعة واحدة لتفادي استعلامات N+1
            var withdrawals = await _context.CashMovements
                .Where(m => m.SourceType == "OwnerWithdrawal" && m.SourceId.HasValue && sessionIds.Contains(m.SourceId.Value))
                .ToListAsync();

            var withdrawalsDict = withdrawals.ToDictionary(m => m.SourceId.GetValueOrDefault());

            // 2. بالنسبة للجلسات التي ليس لها مسحوبات، سنقوم بجلب آخر حركة نقدية لها بكفاءة
            var sessionsWithoutWithdrawal = sessions.Where(s => !withdrawalsDict.ContainsKey(s.Id)).ToList();
            var keepBalancesDict = new Dictionary<int, decimal>();

            if (sessionsWithoutWithdrawal.Any())
            {
                foreach (var session in sessionsWithoutWithdrawal)
                {
                    var lastMovementBefore = await _context.CashMovements
                        .Where(m => m.TransactionDate <= session.PeriodEndDate)
                        .OrderByDescending(m => m.Id) // الرصيد التراكمي مرتب بـ Id (Sequence غير مخزن)
                        .FirstOrDefaultAsync();

                    keepBalancesDict[session.Id] = lastMovementBefore?.BalanceAfter ?? 0;
                }
            }

            var historyList = new List<SettlementHistoryItem>();

            foreach (var session in sessions)
            {
                withdrawalsDict.TryGetValue(session.Id, out var withdrawal);

                decimal payout = withdrawal != null ? Math.Abs(withdrawal.Amount) : 0;
                decimal keep = 0;

                if (withdrawal != null)
                {
                    keep = withdrawal.BalanceAfter;
                }
                else
                {
                    keepBalancesDict.TryGetValue(session.Id, out keep);
                }

                string userNotes = ExtractUserNotes(session.Notes);

                historyList.Add(new SettlementHistoryItem
                {
                    SessionId = session.Id,
                    SessionGuid = session.SessionId,
                    SettleDate = session.PeriodEndDate,
                    CreatedBy = session.CreatedBy,
                    PayoutAmount = payout,
                    KeepAmount = keep,
                    AffectedCount = session.TotalAffectedRows,
                    Notes = userNotes,
                    Status = session.Status
                });
            }

            return historyList;
        }

        public async Task<string> RegenerateSettlementPdfAsync(int sessionId)
        {
            var session = await _context.PostingSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null)
                throw new Exception("جلسة التسوية غير موجودة.");

            var withdrawal = await _context.CashMovements
                .FirstOrDefaultAsync(m => m.SourceType == "OwnerWithdrawal" && m.SourceId == sessionId);

            decimal payoutAmount = withdrawal != null ? Math.Abs(withdrawal.Amount) : 0;
            decimal keepAmount = withdrawal != null ? withdrawal.BalanceAfter : 0;

            if (withdrawal == null)
            {
                var lastMovementBefore = await _context.CashMovements
                    .Where(m => m.TransactionDate <= session.PeriodEndDate)
                    .OrderByDescending(m => m.Id) // الرصيد التراكمي مرتب بـ Id (Sequence غير مخزن)
                    .FirstOrDefaultAsync();

                keepAmount = lastMovementBefore?.BalanceAfter ?? 0;
            }

            decimal balanceBefore = payoutAmount + keepAmount;

            var journals = await _context.DailyJournals
                .Where(j => j.PostingSessionId == sessionId)
                .ToListAsync();

            var expenses = await _context.GeneralExpenses
                .Where(e => e.PostingSessionId == sessionId)
                .ToListAsync();

            decimal totalSales = journals.Sum(j => j.TotalSales);
            decimal totalExpenses = journals.Sum(j => j.TotalExpenses) + expenses.Sum(e => e.Amount);
            int affectedCount = journals.Count + expenses.Count;

            string userNotes = ExtractUserNotes(session.Notes);

            string fileName = $"إيصال_تسوية_مالك_{session.SessionId.ToString().Substring(0, 8).ToUpper()}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);

            SettlementPdfReport.GeneratePdf(
                tempPath,
                payoutAmount,
                keepAmount,
                balanceBefore,
                userNotes,
                session.CreatedBy,
                affectedCount,
                totalSales,
                totalExpenses,
                session.SessionId
            );

            return tempPath;
        }

        public async Task<bool> UnlockPeriodAsync(int sessionId, string reason, string detailReason, string unlockedByUserId,
            string approverUsername, string approverPassword)
        {
            _session?.RequirePermission(Permissions.UnlockPeriod);

            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(detailReason) || detailReason.Length < 20)
                throw new Exception("يجب تحديد سبب وإدخال تفاصيل شرح لا تقل عن 20 حرفاً لإلغاء القفل.");

            // الاعتماد كان يُتحقق منه في النافذة فقط، والخدمة تقبل أي استدعاء بلا معتمِد
            var auth = _auth ?? throw new InvalidOperationException("خدمة التحقق من المستخدمين غير متاحة لاعتماد فك القفل.");
            var approval = await new PeriodUnlockApproval(_context, auth)
                .VerifyAsync(approverUsername, approverPassword, _session?.CurrentUser?.Id ?? 0);
            if (!approval.IsApproved)
                throw new InvalidOperationException(approval.Error);
            detailReason = $"{detailReason} | اعتمده: {approval.Approver!.Username}";

            using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
            try
            {
                var session = await _context.PostingSessions
                    .Include(s => s.UnlockHistories)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                    throw new Exception("جلسة الإقفال غير موجودة.");

                if (session.Status != PostingSessionStatus.Settled && session.Status != PostingSessionStatus.ReSettled)
                    throw new Exception("لا يمكن إلغاء قفل فترة غير مقفلة أو مسواة.");

                var prevStatus = session.Status;
                session.Status = PostingSessionStatus.Unlocked;
                session.RowVersion++;

                int nextSeq = 1;
                if (session.UnlockHistories.Any())
                {
                    nextSeq = session.UnlockHistories.Max(h => h.UnlockSequence) + 1;
                }

                var history = new PeriodUnlockHistory
                {
                    PostingSessionId = sessionId,
                    UnlockSequence = nextSeq,
                    UnlockedBy = unlockedByUserId,
                    UnlockDate = DateTime.Now,
                    PreviousStatus = prevStatus,
                    Reason = reason,
                    DetailReason = detailReason,
                    IsReLocked = false
                };

                _context.PeriodUnlockHistories.Add(history);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(unlockedByUserId, "UnlockPeriod", "PostingSession", sessionId, prevStatus.ToString(), "Unlocked | Reason: " + reason);

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> UnpostPeriodAsync(int sessionId, string reason, string unpostedByUserId)
        {
            _session?.RequirePermission(Permissions.UnpostFinancial);

            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 20)
                throw new Exception("يجب إدخال سبب معتمد وشرح تفصيلي لا يقل عن 20 حرفاً لفك ترحيل الفترة.");

            using var transaction = await _context.Database.BeginOrJoinTransactionAsync();
            try
            {
                var session = await _context.PostingSessions
                    .Include(s => s.Details)
                    .FirstOrDefaultAsync(s => s.Id == sessionId);

                if (session == null)
                    throw new Exception("جلسة الترحيل غير موجودة.");

                if (session.Status != PostingSessionStatus.Unlocked)
                    throw new Exception("يجب إلغاء قفل الفترة أولاً قبل البدء بفك الترحيل المجمع.");

                var prevStatus = session.Status;

                // جلب جميع اليوميات والمصاريف العامة المرتبطة بهذه الجلسة
                var journals = await _context.DailyJournals
                    .Where(j => j.PostingSessionId == sessionId)
                    .ToListAsync();

                var expenses = await _context.GeneralExpenses
                    .Where(e => e.PostingSessionId == sessionId)
                    .ToListAsync();

                decimal totalReversedAmount = 0;
                var reversedMovementIds = new System.Collections.Generic.Dictionary<int, int>();

                // إلغاء ترحيل اليوميات وعكس حركة الخزينة الخاصة بكل وردية على حدة
                foreach (var journal in journals)
                {
                    journal.FinancialStatus = FinancialStatus.Draft;
                    journal.PostedDate = null;
                    journal.PostingSessionId = null;
                    journal.RowVersion++;
                    totalReversedAmount += (journal.ActualCash - journal.CashFloat);

                    var oldMovement = await _context.CashMovements
                        .FindLiveForSourceAsync(SourceTypes.DailyJournal, journal.Id);
                    if (oldMovement != null)
                    {
                        await _cashLedgerService.ReverseMovementAsync(oldMovement.Id, $"فك ترحيل الفترة: {reason}");
                        reversedMovementIds[journal.Id] = oldMovement.Id;
                    }
                }

                // إلغاء ترحيل المصاريف العامة: حركتها النقدية سُجلت عند إنشائها وليس عند الترحيل،
                // لذا لا تُعكس هنا (تُعكس فقط عند تعديل المصروف أو حذفه).
                foreach (var expense in expenses)
                {
                    expense.FinancialStatus = FinancialStatus.Draft;
                    expense.PostedDate = null;
                    expense.PostingSessionId = null;
                    expense.RowVersion++;
                }

                // تحديث تفاصيل الجلسة للتتبع
                foreach (var detail in session.Details)
                {
                    detail.ActionType = PostingActionType.Unposted;

                    // تحديث المبلغ وقت العملية للتسجيل الدقيق
                    if (detail.EntityType == PostingEntityType.DailyJournal)
                    {
                        var j = journals.FirstOrDefault(x => x.Id == detail.EntityId);
                        if (j != null) detail.TransactionAmount = j.ActualCash - j.CashFloat;
                        detail.CashMovementId = reversedMovementIds.TryGetValue(detail.EntityId, out var movId) ? movId : null;
                    }
                    else if (detail.EntityType == PostingEntityType.GeneralExpense)
                    {
                        var e = expenses.FirstOrDefault(x => x.Id == detail.EntityId);
                        if (e != null) detail.TransactionAmount = e.Amount;
                        detail.CashMovementId = null;
                    }
                }

                session.Status = PostingSessionStatus.Unlocked; // تظل مفتوحة للتعديل
                session.RowVersion++;

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(unpostedByUserId, "UnpostPeriod", "PostingSession", sessionId, prevStatus.ToString(), "Unposted | Reversed Amount: " + totalReversedAmount);

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task CheckAndAutoReSettleSessionAsync(DateTime entityDate)
        {
            var session = await _context.PostingSessions
                .FirstOrDefaultAsync(s => s.Status == PostingSessionStatus.Unlocked &&
                                         entityDate.Date >= s.PeriodStartDate.Date &&
                                         entityDate.Date <= s.PeriodEndDate.Date);

            if (session != null)
            {
                // تحقق من عدم وجود أي مسودات (Draft) أخرى في هذه الفترة
                var hasDraftJournals = await _context.DailyJournals
                    .AnyAsync(j => j.FinancialStatus == FinancialStatus.Draft &&
                                   j.JournalDate.Date >= session.PeriodStartDate.Date &&
                                   j.JournalDate.Date <= session.PeriodEndDate.Date &&
                                   !j.IsDeleted);

                var hasDraftExpenses = await _context.GeneralExpenses
                    .AnyAsync(e => e.FinancialStatus == FinancialStatus.Draft &&
                                   e.PaymentDate.Date >= session.PeriodStartDate.Date &&
                                   e.PaymentDate.Date <= session.PeriodEndDate.Date &&
                                   !e.IsDeleted);

                if (!hasDraftJournals && !hasDraftExpenses)
                {
                    session.Status = PostingSessionStatus.ReSettled;
                    session.RowVersion++;

                    // تحديث تاريخ إلغاء القفل إلى مغلق
                    var pendingHistory = await _context.PeriodUnlockHistories
                        .Where(h => h.PostingSessionId == session.Id && !h.IsReLocked)
                        .OrderByDescending(h => h.UnlockDate)
                        .FirstOrDefaultAsync();

                    if (pendingHistory != null)
                    {
                        pendingHistory.IsReLocked = true;
                        pendingHistory.ReLockedDate = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();
                }
            }
        }

        private async Task EnsureEntityPeriodOpenAsync(object entity)
        {
            DateTime? date = entity switch
            {
                DailyJournal j => j.JournalDate,
                GeneralExpense e => e.PaymentDate,
                _ => null
            };
            if (date.HasValue)
                await PeriodLock.EnsureDateOpenAsync(_context, date.Value);
        }

        /// <summary>
        /// تسجيل صافي نقدية الوردية (الفعلي - العهدة) في الخزينة.
        /// موجب = وارد، سالب = صادر (عجز عن العهدة)، صفر = لا حركة (مبيعات بالبطاقات بالكامل مثلاً).
        /// </summary>
        private async Task RecordJournalCashAsync(DailyJournal journal, string notes)
        {
            decimal net = journal.ActualCash - journal.CashFloat;
            if (net == 0) return;

            await _cashLedgerService.RecordMovementAsync(
                net > 0 ? CashMovementType.CashIn : CashMovementType.CashOut,
                Math.Abs(net),
                SourceTypes.DailyJournal,
                journal.Id,
                notes,
                journal.JournalDate);
        }

        private async Task SelfHealEntityAsync<T>(int entityId, T entity, Action<IPostableEntity> updateAction) where T : class
        {
            var entry = _context.Entry(entity);
            var databaseValues = await entry.GetDatabaseValuesAsync();
            if (databaseValues == null)
            {
                throw new Exception("تم حذف الحركة من قبل مستخدم آخر.");
            }

            entry.OriginalValues.SetValues(databaseValues);

            if (entity is IPostableEntity postable)
            {
                updateAction(postable);
                var currentDbRowVersion = databaseValues.GetValue<long>("RowVersion");
                postable.RowVersion = currentDbRowVersion + 1;
            }

            await _context.SaveChangesAsync();
        }

        private async Task SelfHealBatchAsync<T>(List<T> entities, Func<T, int> idSelector, Action<IPostableEntity> updateAction) where T : class
        {
            foreach (var entity in entities)
            {
                var entry = _context.Entry(entity);
                var databaseValues = await entry.GetDatabaseValuesAsync();
                if (databaseValues == null)
                {
                    throw new Exception("تم حذف أحد السجلات من قبل مستخدم آخر.");
                }

                entry.OriginalValues.SetValues(databaseValues);

                if (entity is IPostableEntity postable)
                {
                    updateAction(postable);
                    var currentDbRowVersion = databaseValues.GetValue<long>("RowVersion");
                    postable.RowVersion = currentDbRowVersion + 1;
                }
            }
            await _context.SaveChangesAsync();
        }

        private string ExtractUserNotes(string? rawNotes)
        {
            string userNotes = rawNotes ?? string.Empty;
            if (userNotes.Contains(" | البيان: "))
            {
                userNotes = userNotes.Substring(userNotes.IndexOf(" | البيان: ") + " | البيان: ".Length);
            }
            return userNotes;
        }
    }
}
