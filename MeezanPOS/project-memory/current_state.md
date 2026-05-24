# حالة المشروع الحالية (Project Current State)

يوفر هذا الملف ملخصاً فورياً لحالة التطوير الحالية، وآخر الملفات المعدلة، والمهام النشطة، والخطوات التالية مباشرة في **منظومة ميزان للمطاعم (MeezanPOS)**.

---

## 1. حالة التنفيذ النشطة (Active Implementation Status)

نحن الآن في مرحلة تحسين وتطوير **وحدة الخدمات المصرفية والبنكية (Banking Module)**.

### المنجزات الأخيرة:
1. **✅ دعم الدفع الشخصي (الشريك) في المصاريف العامة**: إضافة طريقة دفع جديدة `"شخصي (شريك)"` إلى المصاريف العامة. تسجيل الدفع تلقائياً كدين مستحق لصالح الشريك الممول عبر `OwnerDebtService` لتوفير إمكانية السداد وتوسيط حسابات الشركاء بسهولة.
2. **✅ نافذة تفاصيل العمليات المربوطة (مصروف عام / سداد مورد)**: تم إنشاء نافذة حوارية جديدة `TransactionDetailsViewWindow` وتفعيلها بالنقرة المزدوجة على معاملات "مصروف عام" أو "سداد مورد" في كشف حساب المصرف، حيث تعرض تفاصيل الفاتورة والأصناف وقيمها بالتفصيل.
3. **تحديث مسمى المعاملات**: تم تغيير المسمى البرمجي لعرض معاملات `CardSalesDeposit` إلى **"خدمات مصرفية"**.
4. **التجميع اليومي (Daily Aggregation)**: تم تطبيق تجميع تلقائي يومي لمعاملات مبيعات الكروت.
5. **تعريب أنواع العمليات بالواجهة**: محول `TransactionTypeToNameConverter`.
6. **نافذة كشف الحساب الكبيرة**: `BankStatementReportWindow` ملء الشاشة.
7. **✅ نافذة تفاصيل مبيعات الخدمات المصرفية اليومية**: تم إنشاء نافذة حوارية `BankStatementDetailsDialog` تنبثق عند النقر المزدوج على أي بند "خدمات مصرفية" في جدول كشف الحساب، وتعرض تفاصيل كل وردية.
8. **✅ حل مشكلة الاستعلامات المتكررة (Redundant Queries)**: تم تعديل المنسدلات وخيار المعاملات المختلطة في الـ ViewModel لتصفير القائمة والملخصات فورياً.
9. **✅ تصحيح الحسابات الرياضية للملخصات والأرصدة التراكمية عند الفلترة**: فصل التصفية البصرية لعرض الحركات عن الحساب المالي الرياضي للمنظومة.

---

## 2. آخر الملفات المعدلة (Latest Modified Files)

* **[MODIFY]** `Domain\Enums\GeneralExpenseEnums.cs`
* **[MODIFY]** `Application\ViewModels\GeneralExpensesViewModel.cs`
* **[MODIFY]** `Presentation\Views\AddGeneralExpenseDialog.xaml`
* **[MODIFY]** `Application\Services\WagesService.cs`
* **[MODIFY]** `Application\ViewModels\BankingServicesViewModel.cs`
* **[NEW]** `Application\ViewModels\BankingItemDetailDto.cs`
* **[NEW]** `Presentation\Views\BankStatementDetailsDialog.xaml` / `.xaml.cs`
* **[NEW]** `Presentation\Views\TransactionDetailsViewWindow.xaml` / `.xaml.cs`
* **[MODIFY]** `Presentation\Views\BankStatementReportWindow.xaml`
* **[MODIFY]** `Presentation\Views\BankStatementReportWindow.xaml.cs`

---

## 3. المهمة النشطة (Active Task)

* **الهدف الحالي**: التحقق من فحص الحركات وتفاصيل المصروف وسداد المورد في كشف الحساب بنجاح.

---

## 4. العقبات البرمجية الحالية (Blockers)

* **بيئة الطرفية**: أمر `dotnet build` واجه مشكلة في تشغيله عبر بيئة الأداة بسبب المسافة في اسم المجلد `D:\Meezan sys\MeezanPOS`. يُنصح بتشغيله يدوياً من بيئة Visual Studio أو الطرفية المحلية.

---

## 5. الخطوات التالية المباشرة (Immediate Next Steps)

1. بناء المشروع يدوياً `dotnet build` من الطرفية أو Visual Studio للتأكد من نجاح البناء بنسبة 100%.
2. اختبار عملية اختيار الحساب البنكي والتحقق من عدم وجود أي بطء أو استدعاء متكرر بالخلفية قبل الضغط على زر تحميل كشف الحساب.
3. الانتقال إلى دراسة ملف وتفاصيل محاذاة وتحسين مدفوعات الموردين (Supplier Payments Alignment).
