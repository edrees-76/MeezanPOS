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

        public PostingService(AppDbContext context)
        {
            _context = context;
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
                postableEntity.PostedDate = DateTime.Now;
                postableEntity.PostedByUserId = postedByUserId;
                postableEntity.RowVersion++;

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    // التحديث الذاتي (Self-Healing) لتعارض إصدار الصفوف الناتج عن تحديث نوع الحقل في SQLite
                    var tableName = _context.Model.FindEntityType(typeof(T))?.GetTableName() ?? typeof(T).Name;
#pragma warning disable EF1002
                    var sql = "UPDATE " + tableName + " SET RowVersion = 1 WHERE Id = {0}";
                    await _context.Database.ExecuteSqlRawAsync(sql, entityId);
#pragma warning restore EF1002
                    
                    // إعادة المحاولة بعد تعيين القيمة الصحيحة في قاعدة البيانات
                    _context.Entry(entity).State = EntityState.Detached;
                    entity = await _context.Set<T>().FindAsync(entityId);
                    if (entity is IPostableEntity activeEntity)
                    {
                        activeEntity.FinancialStatus = FinancialStatus.Posted;
                        activeEntity.PostedDate = DateTime.Now;
                        activeEntity.PostedByUserId = postedByUserId;
                        activeEntity.RowVersion = 2; // تحديث النسخة
                        await _context.SaveChangesAsync();
                    }
                }

                if (entity is DailyJournal finalJournal)
                {
                    var cashLedgerService = new CashLedgerService(_context);
                    decimal cashAmount = finalJournal.ActualCash - finalJournal.CashFloat;
                    await cashLedgerService.RecordMovementAsync(
                        CashMovementType.CashIn,
                        cashAmount,
                        "DailyJournal",
                        finalJournal.Id,
                        $"إيراد وردية: {finalJournal.ShiftName} - الكاشير: {finalJournal.EmployeeName}",
                        finalJournal.JournalDate
                    );
                }

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
                    PostedUntilDate = DateTime.Now,
                    TotalAffectedRows = 1,
                    Status = "Unpost",
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
                    // التحديث الذاتي (Self-Healing) لتعارض إصدار الصفوف
                    var tableName = _context.Model.FindEntityType(typeof(T))?.GetTableName() ?? typeof(T).Name;
#pragma warning disable EF1002
                    var sql = "UPDATE " + tableName + " SET RowVersion = 1 WHERE Id = {0}";
                    await _context.Database.ExecuteSqlRawAsync(sql, entityId);
#pragma warning restore EF1002
                    
                    _context.Entry(entity).State = EntityState.Detached;
                    entity = await _context.Set<T>().FindAsync(entityId);
                    if (entity is IPostableEntity activeEntity)
                    {
                        activeEntity.FinancialStatus = FinancialStatus.Draft;
                        activeEntity.PostedDate = null;
                        activeEntity.PostingSessionId = null;
                        activeEntity.RowVersion = 2;
                        await _context.SaveChangesAsync();
                    }
                }

                if (entity is DailyJournal journal)
                {
                    var cashLedgerService = new CashLedgerService(_context);
                    var oldMovement = await _context.CashMovements
                        .FirstOrDefaultAsync(m => m.SourceType == "DailyJournal" && m.SourceId == journal.Id && !m.IsReversed);
                    if (oldMovement != null)
                    {
                        await cashLedgerService.ReverseMovementAsync(oldMovement.Id, $"فك ترحيل الوردية: {reason}");
                    }
                }

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

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
                DateTime maxJournalDate = journals.Any() ? journals.Max(j => j.JournalDate) : DateTime.Now;

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
                    journal.PostedDate = DateTime.Now;
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
                    // التحديث الذاتي (Self-Healing) لتعارض إصدار الصفوف في SQLite
                    foreach (var journal in journals)
                    {
                        await _context.Database.ExecuteSqlAsync($"UPDATE DailyJournals SET RowVersion = 1 WHERE Id = {journal.Id}");
                    }

                    // إعادة المحاولة بعد تعيين القيمة الصحيحة في قاعدة البيانات
                    foreach (var journal in journals)
                    {
                        _context.Entry(journal).State = EntityState.Detached;
                        var activeJournal = await _context.DailyJournals.FindAsync(journal.Id);
                        if (activeJournal != null)
                        {
                            activeJournal.FinancialStatus = FinancialStatus.Posted;
                            activeJournal.PostedDate = DateTime.Now;
                            activeJournal.PostedByUserId = postedByUserId;
                            activeJournal.PostingSessionId = session.Id;
                            activeJournal.RowVersion = 2; // نسخة جديدة بعد التصحيح
                        }
                    }
                    await _context.SaveChangesAsync();
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
                DateTime maxExpenseDate = expenses.Any() ? expenses.Max(e => e.PaymentDate) : DateTime.Now;

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
                    expense.PostedDate = DateTime.Now;
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
                    // التحديث الذاتي (Self-Healing) لتعارض إصدار الصفوف في SQLite
                    foreach (var expense in expenses)
                    {
                        await _context.Database.ExecuteSqlAsync($"UPDATE GeneralExpenses SET RowVersion = 1 WHERE Id = {expense.Id}");
                    }

                    // إعادة المحاولة بعد تعيين القيمة الصحيحة في قاعدة البيانات
                    foreach (var expense in expenses)
                    {
                        _context.Entry(expense).State = EntityState.Detached;
                        var activeExpense = await _context.GeneralExpenses.FindAsync(expense.Id);
                        if (activeExpense != null)
                        {
                            activeExpense.FinancialStatus = FinancialStatus.Posted;
                            activeExpense.PostedDate = DateTime.Now;
                            activeExpense.PostedByUserId = postedByUserId;
                            activeExpense.PostingSessionId = session.Id;
                            activeExpense.RowVersion = 2; // نسخة جديدة بعد التصحيح
                        }
                    }
                    await _context.SaveChangesAsync();
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
    }
}
