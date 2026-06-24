# خطة تنفيذ توصيات تحسين كود MeezanPOS (نسخة معدلة نهائية)
**تاريخ التحديث:** 5 يونيو 2026

تحتوي هذه النسخة على التحديثات الدقيقة للإجابة على تساؤلات Claude بخصوص الميثودات الثلاثة وتأكيد إضافة `AsNoTracking()`.

---

## أولاً: إجابة تساؤلات Claude
1. **نمط إنشاء WagesManagementViewModel:**
   - يتم إنشاء نموذج العرض **في كل فتح للشاشة (New Instance per View)** وليس كـ Singleton.
   - السطر المسبب: `DataContext = new WagesManagementViewModel();` في ملف `WagesManagementView.xaml.cs`.
   - **القرار:** نقل استدعاء البيانات إلى حدث `Loaded` آمن تماماً ولن يتسبب في تكرار تحميل البيانات.

2. **طبيعة الميثودات الثلاثة في WagesService.cs:**
   - **GetWorkerLedgerAsync:** تقرأ البيانات فقط (قراءة صرفة).
   - **GetWorkerSummariesAsync:** تقرأ البيانات فقط (قراءة صرفة).
   - **GetAttendanceForDateAsync:** تقرأ البيانات فقط من قاعدة البيانات وتملأ حقولاً غير مضافة لقاعدة البيانات `[NotMapped]` وهي `DeductionAmount` و `AdvanceDeducted` لعرضها بالواجهة، ولا تقوم بأي عملية حفظ أو تعديل مخزّن.
   - **القرار:** جميعها **قراءة فقط (Read-Only)**، وبالتالي **سيتم إضافة `.AsNoTracking()` لها جميعاً** لرفع الأداء.

---

## ثانياً: خطة العمل المعدلة مرتبة حسب الأولويات

### الأولوية 1: تحسين استعلامات قاعدة البيانات والـ Eager Loading والـ AsNoTracking
* **الخطوات:**
  1. إدخال `.AsNoTracking()` في الاستعلامات داخل الميثودات الثلاثة في `WagesService.cs`:
     - `GetWorkerLedgerAsync`
     - `GetWorkerSummariesAsync`
     - `GetAttendanceForDateAsync`
  2. استخدام `.Include()` لتضمين الكيانات المرتبطة مباشرة لتقليل عدد الاستعلامات الإضافية.

### الأولوية 2: تعزيز معالجة وتسجيل الأخطاء (Logging Framework)
* **الخطوات:**
  1. تضمين نظام سجلات النظام `AppLogger` في كتل الـ `catch` في ملف `WagesManagementViewModel.cs`.
  2. تبسيط التنبيهات المنبثقة للمستخدم.

### الأولوية 3: تحسين المهام غير المتزامنة (Async/Await)
* **الخطوات:**
  1. إزالة الاستدعاء `_ = LoadAllDataAsync();` من مشيد `WagesManagementViewModel.cs`.
  2. تنفيذ جلب البيانات عبر حدث `Loaded` الخاص بالواجهة.
