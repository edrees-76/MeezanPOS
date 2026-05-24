# محاذاة تبويب "مسحوبات وديون الشركاء" (Partner Debts Alignment)

## الغرض العام
مركز إدارة الذمم المالية بين المطعم والشركاء/المالك. يتتبع كل مبلغ سحبه شريك من صندوق المطعم أو حساباته البنكية، وكل مبلغ أعاده أو ضخه كرأس مال.

**المعادلة الأساسية:**
`صافي الذمة = إجمالي الديون والمسحوبات − إجمالي التسويات والضخ`

---

## 🏗️ البنية المعمارية (3 طبقات)

### 1. طبقة البيانات (Domain)
- **`OwnerDebt`**: تسجيل دين/مسحوب (PartnerName, Amount, Status, SourceType).
- **`OwnerDebtSettlement`**: تسجيل تسوية/سداد (Amount, SettlementSource, BankAccountId).
- **تعدادات الحالات**: (Unpaid, Paid) و (CashRegister, Bank, PettyCash).

### 2. طبقة الخدمات (Application - `OwnerDebtService.cs`)
- `RecordDebtAsync()`: تسجيل دين جديد.
- `RecordSettlementAsync()`: تسجيل تسوية + حركة بنكية تلقائية.
- `DeleteSettlementAsync()`: عكس التسوية وإعادة الدين للحالة القائمة.
- منطق حسابات المجاميع والصافي.

### 3. طبقة العرض (Presentation)
- **ViewModel**: `BankingServicesViewModel.cs` يدير الربط (Binding) للمجاميع والجداول.
- **View**: `BankingServicesView.xaml` يعرض الكروت الملونة والجدول المتجاور.

---

## 🔄 سيناريوهات التشغيل المحاذية
1. **سداد مورد/مصروف شخصي**: يتم استدعاء `RecordDebtAsync` تلقائياً من `LedgerService` أو `GeneralExpensesViewModel`.
2. **سداد المطعم للشريك**: يتم عبر نافذة تسوية تربط بالبنك أو الخزينة.

---

## ⚙️ قواعد الحماية والأمان (Safety Rules)
1. ممنوع حذف دين له تسويات مرتبطة.
2. استخدام (Database Transactions) لضمان نجاح التسوية والربط البنكي معاً.
3. اعتماد الحذف الناعم (Soft Delete).
4. الربط التلقائي: حذف المصروف يحذف الدين التابع له فوراً.
