using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Domain.Interfaces;

namespace MeezanPOS.Application.Services
{
    public class PostingService : IPostingService
    {
        private readonly AppDbContext _context;
        private readonly CashLedgerService _cashLedgerService;

        public PostingService(AppDbContext context)
        {
            _context = context;
            _cashLedgerService = new CashLedgerService(context);
        }

        public async Task<bool> PostEntityAsync<T>(int entityId, string postedByUserId) where T : class
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var entity = await _context.Set<T>().FindAsync(entityId);
                
                if (entity == null)
                    throw new Exception("الحركة غير موجودة.");

                if (entity is not IPostableEntity postableEntity)
                    throw new Exception("هذا الجدول لا يدعم نظام الترحيل المالي.");

                if (postableEntity.FinancialStatus == FinancialStatus.Posted || postableEntity.FinancialStatus == FinancialStatus.Archived)
                    throw new Exception("لا يمكن ترحيل حركة مرحّلة مسبقاً.");

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

                if (entity is DailyJournal finalJournal)
                {
                    decimal cashAmount = finalJournal.ActualCash - finalJournal.CashFloat;
                    await _cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashIn,
                        cashAmount,
                        SourceTypes.DailyJournal,
                        finalJournal.Id,
                        $"ترحيل وردية: {finalJournal.ShiftName} - الموظف: {finalJournal.EmployeeName}",
                        finalJournal.JournalDate
                    );
                }

                var auditService = new AuditService(_context);
                await auditService.LogAsync(postedByUserId, "Post", typeof(T).Name, entityId, "Draft", "Posted");

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
            if (string.IsNullOrWhiteSpace(reason))
                throw new Exception("يجب إدخال سبب مقنع لفك الترحيل المالي.");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var entity = await _context.Set<T>().FindAsync(entityId);
                
                if (entity == null)
                    throw new Exception("الحركة غير موجودة.");

                if (entity is not IPostableEntity postableEntity)
                    throw new Exception("هذا الجدول لا يدعم نظام الترحيل المالي.");

                if (postableEntity.FinancialStatus != FinancialStatus.Posted)
                    throw new Exception("لا يمكن فك ترحيل حركة غير مرحّلة.");

                // تسجيل الـ Audit لفك الترحيل في الجلسات كإجراء عكسي
                var auditSession = new PostingSession
                {
                    CreatedBy = unpostedByUserId,
                    PostedUntilDate = DateTime.UtcNow,
                    TotalAffectedRows = 1,
                    Status = PostingStatuses.Unpost,
                    Notes = $"فك ترحيل بسبب: {reason}"
                };

                _context.PostingSessions.Add(auditSession);
                await _context.SaveChangesAsync(); // للحصول على الـ Id

                var auditDetail = new PostingSessionDetail
                {
                    PostingSessionId = auditSession.Id,
                    EntityType = typeof(T).Name,
                    EntityId = entityId,
                    ActionType = "Unposted"
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

                if (entity is DailyJournal journal)
                {
                    var oldMovement = await _context.CashMovements
                        .FirstOrDefaultAsync(m => m.SourceType == SourceTypes.DailyJournal && m.SourceId == journal.Id && !m.IsReversed);
                    if (oldMovement != null)
                    {
                        await _cashLedgerService.ReverseMovementAsync(oldMovement.Id, $"إلغاء ترحيل الوردية: {reason}");
                    }
                }

                var auditService = new AuditService(_context);
                await auditService.LogAsync(unpostedByUserId, "Unpost", typeof(T).Name, entityId, "Posted", "Draft");

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
            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

                // 1. إنشاء الجلسة وإدخالها للحصول على المعرف التلقائي
                var session = new PostingSession
                {
                    CreatedBy = postedByUserId,
                    PostedUntilDate = maxJournalDate,
                    TotalAffectedRows = journals.Count,
                    Status = "Posted",
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
                        EntityType = "DailyJournal",
                        EntityId = journal.Id,
                        ActionType = "Posted"
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

                var cashLedgerService = new CashLedgerService(_context);
                foreach (var journal in journals)
                {
                    decimal cashAmount = journal.ActualCash - journal.CashFloat;
                    await cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashIn,
                        cashAmount,
                        "DailyJournal",
                        journal.Id,
                        $"إيراد وردية: {journal.ShiftName} - الكاشير: {journal.EmployeeName}",
                        journal.JournalDate
                    );
                }

                var auditService = new AuditService(_context);
                foreach (var journal in journals)
                {
                    await auditService.LogAsync(postedByUserId, "PostBatch", "DailyJournal", journal.Id, "Draft", "Posted");
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
            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

                // 1. إنشاء الجلسة وإدخالها للحصول على المعرف التلقائي
                var session = new PostingSession
                {
                    CreatedBy = postedByUserId,
                    PostedUntilDate = maxExpenseDate,
                    TotalAffectedRows = expenses.Count,
                    Status = "Posted",
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
                        EntityType = "GeneralExpense",
                        EntityId = expense.Id,
                        ActionType = "Posted"
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

                var auditService = new AuditService(_context);
                foreach (var expense in expenses)
                {
                    await auditService.LogAsync(postedByUserId, "PostBatch", "GeneralExpense", expense.Id, "Draft", "Posted");
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
            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var cashLedgerService = new CashLedgerService(_context);
                
                // 1. ترحيل وتأكيد كافة اليوميات غير المرحلة حالياً لإدخال مبالغها في الخزينة
                var draftJournals = await _context.DailyJournals
                    .Include(j => j.ExpenseItems)
                    .Include(j => j.BankingItems)
                    .Include(j => j.Adjustments)
                    .Where(j => j.FinancialStatus == FinancialStatus.Draft && j.JournalDate <= DateTime.UtcNow)
                    .ToListAsync();

                var draftExpenses = await _context.GeneralExpenses
                    .Where(e => e.FinancialStatus == FinancialStatus.Draft && e.PaymentDate <= DateTime.UtcNow)
                    .ToListAsync();

                var totalSalesVal = draftJournals.Sum(j => j.TotalSales);
                var totalExpensesVal = draftExpenses.Sum(e => e.Amount) + draftJournals.Sum(j => j.TotalExpenses);

                // 2. إنشاء جلسة الترحيل والإقفال للتسوية
                var session = new PostingSession
                {
                    CreatedBy = postedByUserId,
                    PostedUntilDate = DateTime.UtcNow,
                    TotalAffectedRows = draftJournals.Count + draftExpenses.Count,
                    Status = "Settle",
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
                        EntityType = "DailyJournal",
                        EntityId = journal.Id,
                        ActionType = "Posted"
                    };
                    _context.PostingSessionDetails.Add(detail);

                    // تسجيل حركة الوارد النقدي بالخزينة للوردية
                    decimal cashAmount = journal.ActualCash - journal.CashFloat;
                    await cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashIn,
                        cashAmount,
                        "DailyJournal",
                        journal.Id,
                        $"إيراد وردية (إقفال وتسوية): {journal.ShiftName} - الكاشير: {journal.EmployeeName}",
                        journal.JournalDate
                    );
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
                        EntityType = "GeneralExpense",
                        EntityId = expense.Id,
                        ActionType = "Posted"
                    };
                    _context.PostingSessionDetails.Add(detail);
                }

                await _context.SaveChangesAsync();

                // 5. التحقق من السيولة النقدية المتوفرة بالخزينة بعد الترحيل
                decimal currentBalance = await cashLedgerService.GetCurrentBalanceAsync();
                if (payoutAmount > currentBalance)
                {
                    throw new Exception($"المبلغ المطلوب تسليمه للمالك ({payoutAmount:N2} د.ل) يتجاوز السيولة النقدية المتوفرة حالياً بالخزينة ({currentBalance:N2} د.ل).");
                }

                // 6. تسجيل حركة مسحوبات المالك لتصفية أو خفض رصيد الصندوق
                if (payoutAmount > 0)
                {
                    await cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashOut,
                        payoutAmount,
                        "OwnerWithdrawal",
                        session.Id,
                        $"سحب سيولة وتسليم كاش للمالك | البيان: {notes}",
                        DateTime.UtcNow
                    );
                }

                var auditService = new AuditService(_context);
                await auditService.LogAsync(postedByUserId, "SettleAndLockPeriod", "FinancialPeriod", session.Id, "Active", "Settled");

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
                .Where(s => s.Status == "Settle")
                .OrderByDescending(s => s.PostedUntilDate)
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
                        .Where(m => m.TransactionDate <= session.PostedUntilDate)
                        .OrderByDescending(m => m.Sequence)
                        .ThenByDescending(m => m.TransactionDate)
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
                    SettleDate = session.PostedUntilDate,
                    CreatedBy = session.CreatedBy,
                    PayoutAmount = payout,
                    KeepAmount = keep,
                    AffectedCount = session.TotalAffectedRows,
                    Notes = userNotes
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
                    .Where(m => m.TransactionDate <= session.PostedUntilDate)
                    .OrderByDescending(m => m.Sequence)
                    .ThenByDescending(m => m.TransactionDate)
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

        private async Task SelfHealEntityAsync<T>(int entityId, T entity, Action<IPostableEntity> updateAction) where T : class
        {
            var tableName = _context.Model.FindEntityType(typeof(T))?.GetTableName() ?? typeof(T).Name;
#pragma warning disable EF1002
            var sql = "UPDATE " + tableName + " SET RowVersion = 1 WHERE Id = {0}";
            await _context.Database.ExecuteSqlRawAsync(sql, entityId);
#pragma warning restore EF1002
            
            _context.Entry(entity).State = EntityState.Detached;
            var activeEntity = await _context.Set<T>().FindAsync(entityId);
            if (activeEntity is IPostableEntity postable)
            {
                updateAction(postable);
                postable.RowVersion = 2;
                await _context.SaveChangesAsync();
            }
        }

        private async Task SelfHealBatchAsync<T>(List<T> entities, Func<T, int> idSelector, Action<IPostableEntity> updateAction) where T : class
        {
            var tableName = _context.Model.FindEntityType(typeof(T))?.GetTableName() ?? typeof(T).Name;
            
            foreach (var entity in entities)
            {
                int id = idSelector(entity);
#pragma warning disable EF1002
                await _context.Database.ExecuteSqlRawAsync("UPDATE " + tableName + " SET RowVersion = 1 WHERE Id = {0}", id);
#pragma warning restore EF1002
            }

            foreach (var entity in entities)
            {
                int id = idSelector(entity);
                _context.Entry(entity).State = EntityState.Detached;
                var activeEntity = await _context.Set<T>().FindAsync(id);
                if (activeEntity is IPostableEntity postable)
                {
                    updateAction(postable);
                    postable.RowVersion = 2;
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
