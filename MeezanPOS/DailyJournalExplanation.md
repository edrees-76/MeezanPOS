# شرح آلية تسجيل الحركة اليومية ونظام الترحيل والأرشفة في المشروع

هذا المستند يشرح بالتفصيل بنية وعملية تسجيل الحركة اليومية (Daily Journal)، التمييز بين اليوميات المفتوحة والمرحلة، آلية الترحيل المالي وجلسات الترحيل (Posting Sessions)، وكيفية عرض الأرشيف التاريخي في مشروع **MeezanPOS**.

---

## 1. الجداول والكيانات المرتبطة باليومية (Daily Journal)

تتمثل الحركة اليومية في جدول رئيسي يرتبط بمجموعة من جداول التفاصيل (علاقة رأس بأطراف - One-to-Many):

1. **[DailyJournal](file:///d:/Meezan%20sys/MeezanPOS/Domain/Entities/DailyJournalEntities.cs#L12-L71)**: الكيان الرئيسي لتسجيل ملخص يوم عمل كامل أو وردية (Cash Float, Total Sales, Actual Cash, Notes, etc.).
2. **[OrderAdjustmentItem](file:///d:/Meezan%20sys/MeezanPOS/Domain/Entities/DailyJournalEntities.cs#L76-L85)**: جدول تفاصيل لتسجيل التعديلات على الطلبيات (الطلبات المجانية والمرتجعات).
3. **[DailyExpenseItem](file:///d:/Meezan%20sys/MeezanPOS/Domain/Entities/DailyJournalEntities.cs#L90-L110)**: جدول تفاصيل للمصروفات النثرية خلال اليوم أو الوردية.
4. **[BankingItem](file:///d:/Meezan%20sys/MeezanPOS/Domain/Entities/DailyJournalEntities.cs#L115-L130)**: جدول تفاصيل المعاملات المصرفية (بطاقة/شبكة).
5. **[DailyJournalBankSale](file:///d:/Meezan%20sys/MeezanPOS/Domain/Entities/DailyJournalEntities.cs#L135-L147)**: جدول تقسيم المبيعات المصرفية حسب المصرف.

---

## 2. تسجيل الطلبات المجانية والمرتجعات

يتم تسجيل الطلبات المجانية والمرتجعات على مستويين متكاملين (إجمالي وتفصيلي):

### أ. تسجيل الطلبات المجانية (Free Orders)
* **كإجمالي**: يحتوي جدول `DailyJournal` على الحقل `FreeOrdersTotal` لتخزين المجموع الإجمالي لقيم الطلبات المجانية للوردية.
* **كتفاصيل**: يتم حفظ تفاصيل كل طلبية مجانية كبند مستقل في جدول `OrderAdjustmentItem` بربطها باليومية وتحديد الحقل `IsFreeOrder = true`.

### ب. تسجيل المرتجعات (Returns)
* **كإجمالي**: يحتوي جدول `DailyJournal` على الحقل `ReturnsTotal` لتخزين المجموع الإجمالي للمرتجعات.
* **كتفاصيل**: يتم حفظ تفاصيل كل مرتجع كبند مستقل في جدول `OrderAdjustmentItem` بربطها باليومية وتحديد الحقل `IsFreeOrder = false`.

---

## 3. العلاقة بين `DailyJournal` و `OrderAdjustmentItem`

العلاقة هي **علاقة رأس بأطراف (One-to-Many)**:
* الـ `DailyJournal` يحتوي على مجموعة (Collection) من الـ `OrderAdjustmentItem` تحت الاسم `Adjustments`.
* كل `OrderAdjustmentItem` يرتبط بـ `DailyJournal` واحد عبر المعرف `DailyJournalId`.

```csharp
// في كلاس DailyJournal
public ICollection<OrderAdjustmentItem> Adjustments { get; set; } = new List<OrderAdjustmentItem>();

// في كلاس OrderAdjustmentItem
public int? DailyJournalId { get; set; }
public DailyJournal? DailyJournal { get; set; }
```

---

## 4. بنية الكيانات المرتبطة بالطلبات المجانية والمرتجعات بالحقول الكاملة

### أولاً: الكيان الرئيسي `DailyJournal` (الحقول المرتبطة بالتسويات والتعديلات)
```csharp
public class DailyJournal : BaseEntity, IPostableEntity
{
    // الحقول الأساسية والمعرفات الموروثة من BaseEntity:
    // public int Id { get; set; }
    // public DateTime CreatedAt { get; set; }
    // public DateTime? UpdatedAt { get; set; }
    // public bool IsDeleted { get; set; }

    public DateTime JournalDate { get; set; }
    public ShiftType ShiftType { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    // مبالغ اليومية الإجمالية
    public decimal CashFloat { get; set; }           // مبلغ الصرف (الفكة)
    public decimal TotalSales { get; set; }          // إجمالي المبيعات من منظومة الكاشير
    public decimal BankingTotal { get; set; }         // إجمالي الخدمات المصرفية
    
    // إجماليات المرتجعات والطلبات المجانية
    public decimal ReturnsTotal { get; set; }        // إجمالي المرتجعات
    public decimal FreeOrdersTotal { get; set; }     // إجمالي الطلبات المجانية

    [NotMapped]
    public decimal TotalAdjustments => ReturnsTotal + FreeOrdersTotal; // إجمالي التعديلات

    // النقد المتوقع والفعلي والفرق
    [NotMapped]
    public decimal ExpectedCash => CashFloat + (TotalSales - BankingTotal) - TotalExpenses - ReturnsTotal;
    public decimal ActualCash { get; set; }
    [NotMapped]
    public decimal Difference => ActualCash - ExpectedCash;

    // قائمة التفاصيل المرتبطة (الطلبات المجانية والمرتجعات)
    public ICollection<OrderAdjustmentItem> Adjustments { get; set; } = new List<OrderAdjustmentItem>();
    
    // حقول الترحيل المالي
    public FinancialStatus FinancialStatus { get; set; } = FinancialStatus.Draft;
    public DateTime? PostedDate { get; set; }
    public string? PostedByUserId { get; set; }
    public int? PostingSessionId { get; set; }
    public long RowVersion { get; set; }
}
```

### ثانياً: كيان بند التسوية `OrderAdjustmentItem` (الحقول الكاملة)
```csharp
public class OrderAdjustmentItem : BaseEntity
{
    // الحقول الموروثة من BaseEntity:
    // public int Id { get; set; }              // المعرف الفريد للبند
    // public DateTime CreatedAt { get; set; }  // تاريخ ووقت الإنشاء
    // public DateTime? UpdatedAt { get; set; }  // تاريخ ووقت التعديل
    // public bool IsDeleted { get; set; }      // حالة الحذف المنطقي

    // ربط العلاقة مع اليومية
    public int? DailyJournalId { get; set; }
    public DailyJournal? DailyJournal { get; set; }

    // الحقول الوظيفية للبند
    public bool IsFreeOrder { get; set; }       // نوع البند (true للطلب المجاني، false للمرتجع)
    public decimal Amount { get; set; }         // قيمة الطلبية أو المرتجع
    public string? InvoiceNumber { get; set; }   // رقم الفاتورة / رقم الطلبية
    public string? Notes { get; set; }           // ملاحظات أو سبب الإرجاع/المجانية
}
```

---

## 5. التمييز بين اليوميات الحالية والأرشيف (الورديات المفتوحة والمرحلة)

يتم التمييز بين اليوميات الحالية (المفتوحة للتعديل) واليوميات المغلقة والمرحلة عن طريق خاصية الحالة المالية: **`FinancialStatus`**.

* **اليوميات الحالية (مفتوحة)**:
  * تكون فيها `FinancialStatus == FinancialStatus.Draft` (القيمة الرقمية `0`).
  * تُعرض في تبويب العمل الحالي (الورديات المفتوحة).
  * في الكود الفعلي يتم تصفيتها كالتالي:
    ```csharp
    var resultList = commonFiltered.Where(j => j.FinancialStatus == FinancialStatus.Draft).ToList();
    ```

* **اليوميات المرحلة (الأرشيف)**:
  * تكون فيها `FinancialStatus == FinancialStatus.Posted` (القيمة الرقمية `2`) أو `FinancialStatus.Archived` (القيمة الرقمية `3`).
  * ترتبط بجلسة ترحيل عبر حقل المعرف `PostingSessionId`.
  * تُعرض في تبويب أرشيف الأشهر المرحلة.

---

## 6. جلسات الترحيل: `PostingSession` و `PostingSessionDetail`

عند ترحيل وردية أو فترة زمنية، يقوم النظام بإنشاء جلسة ترحيل رسمية للتدقيق والمتابعة.

### أ. الكيان `PostingSession` (جلسة الترحيل)
يمثل ترويسة جلسة الترحيل أو التسوية ويحتوي على البيانات التجميعية والوصفية للعملية:
```csharp
public class PostingSession : BaseEntity
{
    public Guid SessionId { get; set; } = Guid.NewGuid(); // معرف فريد خارجي
    [Required]
    [MaxLength(255)]
    public string CreatedBy { get; set; } = string.Empty; // معرف المستخدم المسؤول
    public DateTime PostedUntilDate { get; set; } = DateTime.MinValue; 
    public DateTime PeriodStartDate { get; set; } // بداية تاريخ الفترة المرحلة
    public DateTime PeriodEndDate { get; set; }   // نهاية تاريخ الفترة المرحلة
    public int TotalAffectedRows { get; set; }    // عدد القيود أو الحركات المتأثرة
    public PostingSessionType SessionType { get; set; } = PostingSessionType.Posting; // نوع الجلسة (ترحيل، تسوية، إلغاء ترحيل)
    public PostingSessionStatus Status { get; set; } = PostingSessionStatus.Posted;   // حالة الجلسة (Posted, Settled, Unlocked, ReSettled)
    [MaxLength(1000)]
    public string? Notes { get; set; }
    public long RowVersion { get; set; }
    
    public ICollection<PostingSessionDetail> Details { get; set; } = new List<PostingSessionDetail>();
    public ICollection<PeriodUnlockHistory> UnlockHistories { get; set; } = new List<PeriodUnlockHistory>();
}
```

### ب. الكيان `PostingSessionDetail` (تفاصيل الجلسة)
يربط كل سجل متأثر (مثل وردية يومية أو مصروف عام) بجلسة الترحيل:
```csharp
public class PostingSessionDetail : BaseEntity
{
    public int PostingSessionId { get; set; }
    public PostingSession? PostingSession { get; set; }

    public PostingEntityType EntityType { get; set; } = PostingEntityType.DailyJournal; // نوع الكيان (DailyJournal = 1 أو GeneralExpense = 2)
    public int EntityId { get; set; } // معرف السجل المتأثر (مثل DailyJournal.Id)

    public PostingActionType ActionType { get; set; } = PostingActionType.Posted; // نوع الإجراء المالي
    public decimal TransactionAmount { get; set; } // القيمة المالية المتأثرة
    public int? CashMovementId { get; set; } // معرف حركة الصندوق المرتبطة بالترحيل
}
```

### ج. العلاقة باليومية `DailyJournal`
* يرتبط السجل `DailyJournal` بالجلسة من خلال حقل المفتاح الأجنبي `PostingSessionId` (علاقة اختيارية صفر أو واحد لجلسة الترحيل).
* يتم حفظ المعرف التابع للجلسة في السجل لتسريع عمليات الفلترة والتدقيق وفك الترحيل.
* يتم تسجيل التفاصيل التفصيلية أيضاً داخل `PostingSessionDetail` بربط `EntityId` بـ `DailyJournal.Id` و `EntityType` بـ `PostingEntityType.DailyJournal`.

---

## 7. ماذا يحدث للبيانات عند ترحيل فترة زمنية؟

* **لا تُنقل البيانات** إلى جداول أرشيفية منفصلة بل **تتغير حالتها فقط** داخل نفس الجدول مع فرض قيود وحماية برمجية وقاعدة بيانات لمنع التعديل.
* **الخطوات الفعلية للترحيل**:
  1. تتغير الحالة `FinancialStatus` في `DailyJournal` من `Draft` (0) إلى `Posted` (2).
  2. يتم ملء الحقول `PostedDate` و `PostedByUserId` و `PostingSessionId`.
  3. يتم تسجيل حركة وارد مالي (CashIn) في الخزينة الرئيسية (`CashMovement`) بقيمة `ActualCash - CashFloat` لزيادة السيولة الفعلية بالخزينة.
  4. تقوم مشغلات الحماية بقاعدة البيانات (`SQLite Triggers`) بتجميد السجل. أي عملية `UPDATE` أو `DELETE` على الجدول يُرجع استثناء يمنع التعديل طالما كانت الحالة `Posted` (2) أو `Archived` (3).

* **فك الترحيل**:
  * يتم بإجراء عكسي، حيث ترجع الحالة إلى `Draft` (0)، وتُلغى حركات الخزينة السابقة (IsReversed = true)، ويُفرغ حقل `PostingSessionId`.

---

## 8. طريقة عرض الأرشيف في واجهة المستخدم

في تبويب الأرشيف (`SelectedTab == 1`)، يتم تقسيم وعرض البيانات بطريقة هرمية مريحة تعتمد على مستويين عبر الخاصية `ArchiveLevel`:

1. **مستوى الأشهر (`ArchiveLevel == 0`)**:
   * تُعرض بطاقات مجمعة للمبيعات والربح والمصاريف حسب الأشهر (`MonthSummaryCard`).
   * يتم التجميع في الكود الفعلي باستخدام استعلام Linq:
     ```csharp
     var grouped = _allJournals
         .GroupBy(j => new { j.JournalDate.Year, j.JournalDate.Month })
         .Select(g => new MonthSummaryCard {
             Year = g.Key.Year,
             Month = g.Key.Month,
             MonthName = $"{GetArabicMonthName(g.Key.Month)} {g.Key.Year}",
             TotalSales = g.Sum(j => j.TotalSales),
             TotalCashSales = g.Sum(j => j.CashSales),
             TotalBankingSales = g.Sum(j => j.BankingTotal),
             TotalExpenses = g.Sum(j => j.TotalExpenses),
             DaysCount = g.Select(j => j.JournalDate.Date).Distinct().Count(),
             IsPosted = g.Count(j => j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived) == g.Count()
         });
     ```

2. **مستوى الأيام داخل الشهر المحدد (`ArchiveLevel == 1`)**:
   * عند قيام المستخدم باختيار بطاقة شهر معينة، يتم نقله لعرض الورديات التابعة لذلك الشهر فقط بشكل تفصيلي.
   * يتم تصفية قائمة الورديات وتعبئتها في `ArchivedJournals`:
     ```csharp
     var resultList = commonFiltered.Where(j => j.JournalDate.Year == SelectedArchivedMonth.Year && 
                                                j.JournalDate.Month == SelectedArchivedMonth.Month).ToList();
     ```

---

## 9. عرض تفاصيل الورديات المرحلة القديمة

* **نعم، يمكن للمستخدم عرض تفاصيل الورديات القديمة المرحلة** في أي وقت، ولا يتم إخفاؤها.
* عند النقر على خيار "عرض التفاصيل" لوردية مؤرشفة، يتم تشغيل دالة `LoadJournalForViewing(journal)` في `DailyJournalViewModel.cs` التي تُهيئ النموذج في **وضع العرض فقط**:
  ```csharp
  public void LoadJournalForViewing(MeezanPOS.Domain.Entities.DailyJournal journal)
  {
      IsViewingMode = true; // وضع العرض فقط
      editingJournalId = null;
      editingJournalShift = null;
      LoadJournalData(journal);
      StatusMessage = "وضع العرض - لا يمكن تعديل حركة سابقة";
  }
  ```
* في وضع العرض، يتم تفعيل القفل على واجهة المستخدم (تصبح حقول الإدخال وأزرار الحفظ معطلة بربط خصائص `IsEnabled` بنفي `IsViewingMode` عبر `IsNotViewingMode`)، مما يتيح تصفح البيانات الكاملة ومراجعة المصروفات والمجاني والمرتجع بشكل آمن دون إمكانية التعديل.

---

## 10. آلية ترحيل الورديات المفتوحة بالتفصيل الكامل

### أ. واجهة المستخدم وطرق اختيار الورديات
يوفر النظام 4 طرق مرنة للترحيل لتناسب الاحتياجات المختلفة للمحاسبين:
1. **الترحيل الفردي**: بالضغط على زر ترحيل بجانب وردية معينة في قائمة الورديات المفتوحة (يستدعي `PostJournalCommand`).
2. **الترحيل بالانتخاب (الجماعي)**: تفعيل خانات الاختيار (Checkboxes) بجانب اليوميات المراد ترحيلها والضغط على "ترحيل الورديات المحددة" (يستدعي `PostSelectedJournalsCommand`).
3. **ترحيل فترة زمنية**: الضغط على زر "ترحيل فترة" وتحديد تاريخ البداية والنهاية عبر نافذة منبثقة (يستدعي `PostPeriodCommand`).
4. **الترحيل التلقائي عبر التسوية الإجمالية**: عند النقر على "تسوية كاش المالك" وقبول التسوية (يستدعي `ConfirmSettleOwnerCashCommand` الذي يستدعي `SettleAndLockPeriodAsync` في الخدمة) ليتم ترحيل كافة اليوميات والمصاريف المفتوحة قبل لحظة التسوية تلقائياً.

### ب. شروط الفلترة والقبول للترحيل
* **حالة الوردية**: يجب أن تكون اليومية في حالة مسودة `FinancialStatus == FinancialStatus.Draft`. اليوميات المرحلة سابقاً يتم استثناؤها فوراً لمنع تكرار العملية.
* **التصفية الزمنية**:
  * في حال ترحيل الفترة: يتم جلب اليوميات التي يقع تاريخها ضمن الفترة المحددة:
    `j.JournalDate.Date >= startDate && j.JournalDate.Date <= endDate`
  * في حال التسوية الشاملة للمالك: يتم جلب جميع اليوميات المفتوحة التي تاريخها يسبق أو يساوي التوقيت الحالي:
    `j.JournalDate <= DateTime.UtcNow`

### ج. الخطوات التقنية لعملية الترحيل خطوة بخطوة

#### 1. عند ضغط المستخدم على زر "ترحيل الورديات المحددة" في `SalesViewModel.cs`:
```csharp
[RelayCommand]
public async Task PostSelectedJournalsAsync()
{
    var selectedIds = Journals.Where(j => j.IsSelected).Select(j => j.Journal.Id).ToList();
    if (!selectedIds.Any()) return;

    // إظهار تأكيد ورسالة تشتمل على الإجماليات المحددة
    var confirmResult = System.Windows.MessageBox.Show(...);
    if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

    try
    {
        IsLoading = true;
        var postingService = AppServiceProvider.Resolve<IPostingService>();
        // استدعاء خدمة الترحيل الجماعي
        var batchResult = await postingService.PostDailyJournalsBatchAsync(selectedIds, "Admin", "ترحيل جماعي لليوميات المحددة من الواجهة");

        if (batchResult.Success)
        {
            System.Windows.MessageBox.Show("تمت عملية الترحيل الجماعي للمؤسسات بنجاح! ...");
            await LoadDataAsync(); // تحديث القوائم والتبويبات
        }
    }
    ...
}
```

#### 2. معالجة العملية داخل الخدمة `PostingService.cs`:
```csharp
public async Task<PostingBatchResult> PostDailyJournalsBatchAsync(List<int> journalIds, string postedByUserId, string notes)
{
    // ... التحقق من صحة المعطيات وفتح معاملة قاعدة بيانات مع عزل من النوع Serializable
    using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    try
    {
        // 1. تحميل الكيانات وتفاصيلها دفعة واحدة من قاعدة البيانات لتفادي N+1
        var journals = await _context.DailyJournals
            .Include(j => j.ExpenseItems)
            .Include(j => j.BankingItems)
            .Include(j => j.Adjustments)
            .Where(j => ids.Contains(j.Id))
            .ToListAsync();

        // 2. التحقق من أن جميع اليوميات المطلوبة في حالة Draft وموجودة
        // ...

        // 3. حساب حدود تواريخ الجلسة
        DateTime maxJournalDate = journals.Max(j => j.JournalDate);
        DateTime minJournalDate = journals.Min(j => j.JournalDate);

        // 4. إنشاء جلسة الترحيل PostingSession وحفظها للحصول على معرف تلقائي
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
        await _context.SaveChangesAsync();

        // 5. تحديث السجلات وتجهيز تفاصيل الجلسة PostingSessionDetail
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

        await _context.SaveChangesAsync(); // حفظ التعديلات وحماية النسخ المتزامن

        // 6. تسجيل قيود الصندوق لكل وردية وتمريرها للخزينة
        foreach (var journal in journals)
        {
            decimal cashAmount = journal.ActualCash - journal.CashFloat;
            await _cashLedgerService.RecordMovementAsync(
                CashMovementType.CashIn,
                cashAmount,
                "DailyJournal",
                journal.Id,
                $"إيراد وردية: {journal.ShiftName} - الكاشير: {journal.EmployeeName}",
                journal.JournalDate
            );
        }

        await transaction.CommitAsync(); // اعتماد الترحيل نهائياً في قاعدة البيانات
        // ...
    }
    ...
}
```

### د. آلية اختفاء السجلات وظهورها في الأرشيف
بعد نجاح الترحيل، يتم استدعاء دالة `LoadDataAsync()` في الـ ViewModel لإعادة جلب السجلات من قاعدة البيانات وتحديث قائمة `_allJournals` بالقيم الجديدة التابعة لخاصية `FinancialStatus`.

عند الانتقال بين التبويبات أو بعد تحديث البيانات، يتم استدعاء دالة `ApplyFilters()` التي تتبع المنطق التالي:
* **تبويب العمل الحالي (`SelectedTab == 0`)**:
  يقوم بتصفية وعرض السجلات التي ما زالت في حالة مسودة فقط:
  `var resultList = commonFiltered.Where(j => j.FinancialStatus == FinancialStatus.Draft).ToList();`
  بما أن الورديات المرحلة أصبحت حالتها `Posted` (2)، فإنها تُستبعد تلقائياً وتختفي من هذه القائمة.
* **تبويب الأرشيف (`SelectedTab == 1`)**:
  يتم عرض بطاقات الأشهر بناءً على تجميع اليوميات المرحلة والمؤرشفة، وعند النقر على الشهر يتم جلبها من خلال:
  `var resultList = commonFiltered.Where(j => j.JournalDate.Year == SelectedArchivedMonth.Year && j.JournalDate.Month == SelectedArchivedMonth.Month).ToList();`
  والتي تحتوي على اليوميات التي تحمل الحالة `Posted` أو `Archived` للمراجعة والعرض فقط.

### هـ. الحالات الخاصة (الورديات المسودة القديمة)
* **في الترحيل الجماعي والفردي**: لا يمنع النظام ترحيل الورديات الحديثة بمفردها وترك الورديات المسودة القديمة معلقة كـ Draft.
* **في التسوية الإجمالية للمالك**:
  عند قيام المحاسب بعملية "تسوية كاش المالك" يتم استدعاء `SettleAndLockPeriodAsync` والتي تقوم بفحص قاعدة البيانات بشكل كامل وجلب **كافة الورديات المفتوحة والمصاريف المعلقة التي تسبق التاريخ الحالي**:
  `j.FinancialStatus == FinancialStatus.Draft && j.JournalDate <= DateTime.UtcNow`
  بناءً على ذلك، يتم سحب وترحيل كافة المسودات المعلقة تاريخياً وإدخالها في الصندوق بالتسوية للتأكد من مطابقة الخزينة الفعلية وحماية الحسابات من أي ثغرات نقدية معلقة.

---

## 11. تصميم وهيكل التبويبات (Segmented Control Tabs) في واجهة المستخدم XAML

لا يستخدم المشروع الـ `TabControl` الافتراضي القياسي الخاص بـ WPF، وإنما يعتمد على أسلوب تصميم حديث يُحاكي عناصر التحكم المقسمة (Segmented Controls) في واجهات الويب وأنظمة التشغيل الحديثة (مثل iOS) من خلال استخدام أزرار عادية داخل حاوية مخصصة.

### أ. كود XAML لهيكل التبويبات (مثال من SalesView.xaml)
```xml
<!-- Gorgeous Segmented Control for Navigation -->
<Border Grid.Column="1" Background="#e2e8f0" CornerRadius="22" Padding="4" VerticalAlignment="Center">
    <StackPanel Orientation="Horizontal">
        
        <!-- Tab 0: Active Work -->
        <Button Command="{Binding SetTabCommand}" CommandParameter="0">
            <Button.Style>
                <Style TargetType="Button" BasedOn="{StaticResource SleekTabButtonStyle}">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding SelectedTab}" Value="0">
                            <Setter Property="Background" Value="White"/>
                            <Setter Property="Foreground" Value="#0f172a"/>
                            <Setter Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect BlurRadius="6" ShadowDepth="1" Color="#cbd5e1" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Button.Style>
            <Button.Resources>
                <Style TargetType="Border">
                    <Setter Property="CornerRadius" Value="18"/>
                </Style>
            </Button.Resources>
            <StackPanel Orientation="Horizontal">
                <materialDesign:PackIcon Kind="CashRegister" Width="18" Height="18" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <TextBlock Text="العمل الحالي (الورديات المفتوحة)" VerticalAlignment="Center"/>
            </StackPanel>
        </Button>

        <!-- Tab 1: Archive -->
        <Button Command="{Binding SetTabCommand}" CommandParameter="1">
            <Button.Style>
                <Style TargetType="Button" BasedOn="{StaticResource SleekTabButtonStyle}">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding SelectedTab}" Value="1">
                            <Setter Property="Background" Value="White"/>
                            <Setter Property="Foreground" Value="#0f172a"/>
                            <Setter Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect BlurRadius="6" ShadowDepth="1" Color="#cbd5e1" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Button.Style>
            <Button.Resources>
                <Style TargetType="Border">
                    <Setter Property="CornerRadius" Value="18"/>
                </Style>
            </Button.Resources>
            <StackPanel Orientation="Horizontal">
                <materialDesign:PackIcon Kind="ArchiveOutline" Width="18" Height="18" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <TextBlock Text="أرشيف الأشهر المرحّلة" VerticalAlignment="Center"/>
            </StackPanel>
        </Button>

        <!-- Tab 2: Current Cash -->
        <Button Command="{Binding SetTabCommand}" CommandParameter="2">
            <Button.Style>
                <Style TargetType="Button" BasedOn="{StaticResource SleekTabButtonStyle}">
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding SelectedTab}" Value="2">
                            <Setter Property="Background" Value="White"/>
                            <Setter Property="Foreground" Value="#0f172a"/>
                            <Setter Property="Effect">
                                <Setter.Value>
                                    <DropShadowEffect BlurRadius="6" ShadowDepth="1" Color="#cbd5e1" Opacity="0.5"/>
                                </Setter.Value>
                            </Setter>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </Button.Style>
            <Button.Resources>
                <Style TargetType="Border">
                    <Setter Property="CornerRadius" Value="18"/>
                </Style>
            </Button.Resources>
            <StackPanel Orientation="Horizontal">
                <materialDesign:PackIcon Kind="CashMultiple" Width="18" Height="18" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <TextBlock Text="الكاش الحالي" VerticalAlignment="Center"/>
            </StackPanel>
        </Button>

    </StackPanel>
</Border>
```

### ب. كود XAML لـ SleekTabButtonStyle المستعمل للتبويبات
تم إدراج هذا الستايل في الموارد المحلية للشاشة:
```xml
<!-- Tab Button Style -->
<Style x:Key="SleekTabButtonStyle" TargetType="Button" BasedOn="{StaticResource MaterialDesignFlatButton}">
    <Setter Property="Height" Value="40"/>
    <Setter Property="Padding" Value="24,0"/>
    <Setter Property="FontSize" Value="14"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="#64748b"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Style.Resources>
        <!-- Overriding material design borders -->
        <Style TargetType="Border">
            <Setter Property="CornerRadius" Value="20"/>
        </Style>
    </Style.Resources>
</Style>
```

### ج. تفاصيل الألوان والأحجام والخطوط المستخدمة
* **خلفية شريط التبويبات الكلي**: `#e2e8f0` (أزرق رمادي فاتح / Slate Grey).
* **حواف شريط التبويبات**: `CornerRadius="22"`.
* **خلفية التبويب النشط**: `White` (أبيض).
* **لون خط التبويب النشط**: `#0f172a` (أزرق رمادي غامق جداً / Dark Slate).
* **خلفية التبويب غير النشط**: `Transparent` (شفاف).
* **لون خط التبويب غير النشط**: `#64748b` (رمادي متوسط / Muted Slate).
* **تأثير الظل للتبويب النشط**: ظل ناعم `BlurRadius="6"` بلون `#cbd5e1` وبدرجة شفافية `Opacity="0.5"`.
* **ارتفاع زر التبويب**: `40`.
* **الهامش الداخلي لزر التبويب**: `24` يمين ويسار.
* **حجم الخط**: `14`.
* **وزن الخط**: `SemiBold`.
* **نوع الخط**: يعتمد على الخط الافتراضي الأنيق المعتمد للمشروع (مثل `Inter` أو `Arial` مع التوجيه من اليمين إلى اليسار).

---

## 12. آلية طباعة وتصدير تقارير الحركة اليومية (PDF)

تحتوي منظومة MeezanPOS على ميزة تصدير وطباعة الحركة اليومية كاملةً مع تفاصيل المصروفات والخدمات المصرفية والتسويات (المرتجعات والطلبات المجانية) إلى ملف PDF منسق وأنيق.

### أ. ملف التقرير ومكانه
* **اسم الملف**: `DailyJournalPdfReport.cs`
* **المسار الفعلي**: `d:\Meezan sys\MeezanPOS\Application\Services\DailyJournalPdfReport.cs`
* يستخدم التقرير مكتبة **QuestPDF** لتوليد ملف مستند متقدم يدعم اللغة العربية بشكل كامل (`ContentFromRightToLeft`).

### ب. عرض بنود الطلبات المجانية والمرتجعات (OrderAdjustmentItem)
نعم، يعرض التقرير تفاصيل بنود الطلبات المجانية والمرتجعات كاملةً في جدولين مستقلين جنباً إلى جنب في الـ PDF:
* **الطلبات المجانية (Free Orders)**: يعرض جدولاً يضم مبالغ كل طلب مع الملاحظات أو رقم الفاتورة الخاص به، بالإضافة إلى المجموع النهائي.
* **المرتجعات (Returns)**: يعرض جدولاً مماثلاً يضم مبالغ كل مرتجع مع الملاحظات ورقم الفاتورة المرتبط والمجموع الإجمالي.

الكود الفعلي لرسم جزء التسويات والمجاني في التقرير:
```csharp
row.RelativeItem().Element(c => DrawCard(c, "الطلبات المجانية", content =>
{
    content.Column(innerCol => 
    {
        if (_vm.FreeOrders.Any())
        {
            innerCol.Item().Table(t =>
            {
                t.ColumnsDefinition(cols => { cols.RelativeColumn(); cols.RelativeColumn(2); });
                t.Header(h => { 
                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fefff5").Padding(4).Text("المبلغ").SemiBold(); 
                    h.Cell().Border(1).BorderColor(Colors.Black).Background("#fefff5").Padding(4).Text("ملاحظات/فاتورة").SemiBold(); 
                });
                foreach (var item in _vm.FreeOrders) { 
                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text($"{item.Amount:N2}"); 
                    t.Cell().Border(1).BorderColor(Colors.Black).Padding(4).Text(item.InvoiceNumber ?? item.Notes); 
                }
            });
        }
        else { innerCol.Item().Text("لا يوجد طلبات مجانية"); }
        innerCol.Item().PaddingTop(10).Text($"الإجمالي: {_vm.FreeOrdersAmount:N2}").SemiBold();
    });
}));
```

### ج. زر تصدير/طباعة تقرير الـ PDF في واجهة المستخدم
نعم، يشتمل ملف `DailyJournalView.xaml` على زر طباعة PDF متاح دائماً للمستخدم (في نهاية الشاشة) ويرتبط بـ `PrintPdfCommand` في الـ ViewModel:
```xml
<!-- زر الطباعة (متاح دائماً) -->
<Button Command="{Binding PrintPdfCommand}" Margin="16,0,0,0"
        Style="{StaticResource MaterialDesignRaisedButton}"
        materialDesign:ButtonAssist.CornerRadius="10"
        Background="#3b82f6" BorderBrush="#3b82f6"
        Height="50" Padding="24,0" FontSize="16" FontWeight="Bold">
    <StackPanel Orientation="Horizontal">
        <materialDesign:PackIcon Kind="Printer" Margin="0,0,10,0" VerticalAlignment="Center"/>
        <TextBlock Text="PDF" VerticalAlignment="Center"/>
    </StackPanel>
</Button>
```

عند النقر عليه، يستدعي الدالة التالية في `DailyJournalViewModel.cs` لتوليد التقرير وفتحه تلقائياً:
```csharp
[RelayCommand]
private void PrintPdf()
{
    try
    {
        var report = new MeezanPOS.Application.Services.DailyJournalPdfReport(this);
        string shiftName = GetShiftDisplayName(SelectedShiftType);
        string fileName = $"حركة يومية - {JournalDate:yyyy-MM-dd} - الوردية {shiftName}.pdf";
        var filePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
        
        // توليد الـ PDF
        report.GeneratePdf(filePath);
        
        // فتح الـ PDF تلقائياً
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
    }
    catch (System.Exception ex)
    {
        StatusMessage = "خطأ في الطباعة: " + ex.Message;
    }
}
```
