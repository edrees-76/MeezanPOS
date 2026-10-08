using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using System.Threading.Tasks;
using System.Windows.Input;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;

namespace MeezanPOS.Application.ViewModels;

/// <summary>دفتر النقدية، تسوية نقدية المالك، وأرشيف التسويات وفكها.</summary>
public partial class SalesViewModel
{
    [RelayCommand]
    public async Task LoadCashMovementsAsync()
    {
        IsCashLoading = true;
        try
        {
            var movements = await _queries.GetRecentCashMovementsAsync(150);

            // Add sequence numbers
            for (int i = 0; i < movements.Count; i++)
            {
                movements[i].Sequence = i + 1;
            }

            CashMovements = new ObservableCollection<CashMovement>(movements);

            var cashLedgerService = _cashLedgerService;
            CurrentCashBalance = await cashLedgerService.GetCurrentBalanceAsync();
            IsRebuildRequired = await cashLedgerService.IsRebuildRequiredAsync();
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"خطأ في تحميل حركات النقدية: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsCashLoading = false;
        }
    }

    [RelayCommand]
    public async Task RebuildCashLedgerAsync()
    {
        var result = Dialogs.Show(
            "هل أنت متأكد من إعادة بناء دفتر النقدية؟\nسيقوم هذا الإجراء بإعادة حساب الأرصدة التراكمية بناءً على الترتيب التاريخي للحركات.",
            "تأكيد إعادة البناء",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsCashLoading = true;
        try
        {
            var cashLedgerService = _cashLedgerService;
            await cashLedgerService.RebuildLedgerAsync();
            await LoadCashMovementsAsync();
            Dialogs.Show("تم إعادة بناء دفتر النقدية بنجاح!", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"خطأ أثناء إعادة بناء الدفتر: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsCashLoading = false;
        }
    }

    [RelayCommand]
    public void ViewCashMovementDetails(CashMovement movement)
    {
        if (movement == null) return;

        AppWindows.Current.ShowTransactionDetails(movement);
    }

    [RelayCommand]
    public async Task OpenSettleOwnerCashDialogAsync()
    {
        IsCashLoading = true;
        try
        {
            var cashLedgerService = _cashLedgerService;
            CurrentCashBalance = await cashLedgerService.GetCurrentBalanceAsync();
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"خطأ في تحديث رصيد الخزينة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsCashLoading = false;
        }

        SettlePayoutInput = 0;
        SettleNotesInput = $"تسوية نقدية للمالك - فترة {System.DateTime.Now:yyyy/MM/dd}";
        IsSettleOwnerCashDialogOpen = true;
    }

    [RelayCommand]
    public void CloseSettleOwnerCashDialog()
    {
        IsSettleOwnerCashDialogOpen = false;
    }

    [RelayCommand]
    public async Task ConfirmSettleOwnerCashAsync()
    {
        if (SettlePayoutInput < 0)
        {
            Dialogs.Show("يجب إدخال مبلغ صحيح وموجب للتسوية.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (SettlePayoutInput > CurrentCashBalance)
        {
            Dialogs.Show("المبلغ المطلوب أكبر من الرصيد المتاح في الخزينة.", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var confirm = Dialogs.Show(
            $"هل أنت متأكد من تأكيد تسوية الخزينة وسحب مبلغ للمالك؟\n\n" +
            $"💰 السيولة النقدية المتوفرة: {CurrentCashBalance:N2} د.ل\n" +
            $"💸 المبلغ المسحوب للمالك: {SettlePayoutInput:N2} د.ل\n" +
            $"⚙️ السيولة المتبقية بالصندوق: {SettleKeepCalculation:N2} د.ل\n\n" +
            $"سيتم ترحيل وتأكيد كافة اليوميات والمصاريف اليومية غير المرحلة وتجميدها نهائياً.",
            "تأكيد تسوية الخزينة وإقفال الفترة",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var postingService = _postingService;

            var batchResult = await postingService.SettleAndLockPeriodAsync(
                payoutAmount: SettlePayoutInput,
                keepAmount: SettleKeepCalculation,
                notes: SettleNotesInput,
                postedByUserId: CurrentUserId
            );

            if (batchResult.Success)
            {
                IsSettleOwnerCashDialogOpen = false;

                // تحديث البيانات
                await LoadCashMovementsAsync();
                await LoadDataAsync();

                var successMsg = $"تمت عملية التسوية بنجاح وتجميد الحركات السابقة!\n\n" +
                                 $"🔹 الرصيد السابق: {batchResult.BalanceBefore:N2} د.ل\n" +
                                 $"🔹 المسلم للمالك: {batchResult.PayoutAmount:N2} د.ل\n" +
                                 $"🔹 المتبقي بالخزينة: {batchResult.KeepAmount:N2} د.ل\n" +
                                 $"🔹 عدد اليوميات والمصاريف المقفلة: {batchResult.PostedCount}\n" +
                                 $"🔹 معرف الجلسة: {batchResult.SessionGuid.ToString().Substring(0,8).ToUpper()}";

                Dialogs.Show(successMsg, "نجاح تسوية الخزينة", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                // فتح ملف الـ PDF تلقائياً للمستند المولد
                if (!string.IsNullOrEmpty(batchResult.PdfPath) && System.IO.File.Exists(batchResult.PdfPath))
                {
                    using var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo(batchResult.PdfPath) { UseShellExecute = true }
                    };
                    process.Start();
                }
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                Dialogs.Show($"فشلت عملية تسوية الخزينة:\n{errors}", "خطأ في التسوية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء تسوية الخزينة: {ex.Message}", "خطأ قاتل", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenSettlementArchiveAsync()
    {
        IsLoading = true;
        try
        {
            var postingService = _postingService;
            var history = await postingService.GetSettlementHistoryAsync();

            SettlementHistory.Clear();
            foreach (var item in history)
            {
                SettlementHistory.Add(item);
            }
            IsSettlementArchiveDialogOpen = true;
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"فشل تحميل أرشيف التسويات: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CloseSettlementArchive()
    {
        IsSettlementArchiveDialogOpen = false;
    }

    [RelayCommand]
    public async Task UnlockPeriodAsync(SettlementHistoryItem item)
    {
        if (item == null) return;
        if (!_sessionService.HasPermission("UnpostFinancial"))
        {
            Dialogs.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        var unlock = AppWindows.Current.AskPeriodUnlockReason();
        if (unlock == null) return;
        string reason = unlock.Reason;
        string detailReason = unlock.Detail;

        IsLoading = true;
        try
        {
            var postingService = _postingService;
            var success = await postingService.UnlockPeriodAsync(item.SessionId, reason, detailReason, CurrentUserId);

            if (success)
            {
                Dialogs.Show("تم إلغاء قفل الفترة بنجاح للمراجعة والتدقيق.", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                // تحديث قائمة أرشيف التسويات
                var history = await postingService.GetSettlementHistoryAsync();
                SettlementHistory.Clear();
                foreach (var h in history)
                {
                    SettlementHistory.Add(h);
                }
                await LoadDataAsync();
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"فشل إلغاء قفل الفترة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UnpostPeriodAsync(SettlementHistoryItem item)
    {
        if (item == null) return;
        if (!_sessionService.HasPermission("UnpostFinancial"))
        {
            Dialogs.Show("عذراً، هذا الإجراء متاح فقط للمدير العام.", "صلاحية غير كافية", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        if (item.Status != PostingSessionStatus.Unlocked)
        {
            Dialogs.Show("يجب إلغاء قفل الفترة أولاً قبل البدء بفك الترحيل المجمع.", "تنبيه", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        int journalsCount = 0;
        int expensesCount = 0;
        decimal totalReversedAmount = 0;

        try
        {
            var impact = await _queries.GetSettlementImpactAsync(item.SessionId);
            journalsCount = impact.JournalsCount;
            expensesCount = impact.ExpensesCount;
            totalReversedAmount = impact.TotalAmount;
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في جلب بيانات الفترة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        decimal currentBalance = CurrentCashBalance;
        decimal expectedBalance = currentBalance - totalReversedAmount;

        var confirmMsg = $"تنبيه: أنت على وشك فك ترحيل الفترة بالكامل كحزمة واحدة.\n\n" +
                         $"📦 تفاصيل العملية العكسية:\n" +
                         $"🔹 عدد اليوميات المتأثرة: {journalsCount} يومية\n" +
                         $"🔹 عدد المصاريف المتأثرة: {expensesCount} مصروف\n" +
                         $"🔹 إجمالي القيمة المسترجعة (عكس حركة الخزينة): {totalReversedAmount:N2} د.ل\n\n" +
                         $"💰 الرصيد الحالي للخزينة: {currentBalance:N2} د.ل\n" +
                         $"📉 الرصيد المتوقع بعد العكس: {expectedBalance:N2} د.ل\n\n" +
                         $"هل تريد فك ترحيل الفترة وعكس حركات النقدية آلياً؟";

        var confirmResult = Dialogs.Show(confirmMsg, "تأكيد العمليات العكسية وفك ترحيل الفترة", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirmResult != System.Windows.MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var postingService = _postingService;
            var reason = $"إجراء فك ترحيل الفترة للجلسة {item.ShortSessionGuid} بواسطة المدير العام";
            var success = await postingService.UnpostPeriodAsync(item.SessionId, reason, CurrentUserId);

            if (success)
            {
                Dialogs.Show("تم فك ترحيل الفترة بالكامل وإلغاء وعكس حركات النقدية بنجاح.", "نجاح", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                // تحديث قائمة أرشيف التسويات
                var history = await postingService.GetSettlementHistoryAsync();
                SettlementHistory.Clear();
                foreach (var h in history)
                {
                    SettlementHistory.Add(h);
                }
                await LoadDataAsync();
                await LoadCashMovementsAsync();
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"فشل فك ترحيل الفترة: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RegenerateSettlementPdfAsync(SettlementHistoryItem item)
    {
        if (item == null) return;

        IsLoading = true;
        try
        {
            var postingService = _postingService;
            string pdfPath = await postingService.RegenerateSettlementPdfAsync(item.SessionId);

            if (!string.IsNullOrEmpty(pdfPath) && System.IO.File.Exists(pdfPath))
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(pdfPath) { UseShellExecute = true }
                };
                process.Start();
            }
        }
        catch (System.Exception ex)
        {
            Dialogs.Show($"فشل إعادة توليد ملف PDF: {ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
