using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Application.Services;
using MeezanPOS.Application.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace MeezanPOS.Application.ViewModels;

/// <summary>ترحيل المصروفات المحددة أو فترة كاملة.</summary>
public partial class GeneralExpensesViewModel
{
    [RelayCommand]
    private async System.Threading.Tasks.Task PostSelectedExpensesAsync()
    {
        var selectedIds = Expenses.Where(e => e.IsSelected && e.IsDraft).Select(e => e.Id).ToList();
        if (!selectedIds.Any())
        {
            Dialogs.Show("يرجى تحديد مصروف واحد على الأقل للترحيل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirmResult = Dialogs.Show(
            $"هل أنت متأكد من ترحيل وإقفال عدد ({selectedIds.Count}) مصاريف محددة مالياً؟\n" +
            $"إجمالي المصاريف المحددة: {RunningSelectedExpensesTotal:N2} د.ل\n" +
            $"بعد الترحيل، سيتم قفل هذه العمليات نهائياً ولن تتمكن من تعديلها أو حذفها.",
            "تأكيد الترحيل الجماعي للمصاريف",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmResult != MessageBoxResult.Yes) return;

        try
        {
            var postingService = _postingService;
            var currentUserId = _sessionService.CurrentUserId;
            var batchResult = await postingService.PostGeneralExpensesBatchAsync(selectedIds, currentUserId, "ترحيل جماعي للمصاريف المحددة من الواجهة");

            if (batchResult.Success)
            {
                Dialogs.Show(
                    $"تمت عملية الترحيل الجماعي للمصاريف بنجاح!\n\n" +
                    $"🔹 عدد المصاريف المرحلة: {batchResult.PostedCount}\n" +
                    $"🔹 إجمالي المصاريف المرحلة: {batchResult.TotalExpenses:N2} د.ل\n" +
                    $"🔹 زمن التنفيذ الفعلي: {batchResult.Duration.TotalMilliseconds:N0} مللي ثانية\n" +
                    $"🔹 معرف جلسة الترحيل (Session Guid):\n{batchResult.SessionGuid}\n" +
                    $"🔹 معرف التتبع (Correlation Id):\n{batchResult.CorrelationId}",
                    "نجاح الترحيل الجماعي للمصاريف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadExpenses();
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                Dialogs.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي: {ex.Message}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task PostPeriodAsync()
    {
        var range = AppWindows.Current.AskDateRange(DateFrom, DateTo);
        if (range == null) return;
        DateTime startDate = range.Value.Start;
        DateTime endDate = range.Value.End;

        try
        {
            var expensesInPeriod = await _expenses.GetDraftIdsInRangeAsync(startDate, endDate);

            if (!expensesInPeriod.Any())
            {
                Dialogs.Show(
                    $"لا توجد أي مصاريف مفتوحة (غير مرحلة) في الفترة المحددة:\nمن: {startDate:dd-MM-yyyy} إلى: {endDate:dd-MM-yyyy}",
                    "لا توجد بيانات للترحيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmResult = Dialogs.Show(
                $"هل أنت متأكد من ترحيل وإقفال جميع المصاريف المفتوحة في الفترة المحددة؟\n\n" +
                $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                $"📦 عدد المصاريف المفتوحة المكتشفة: {expensesInPeriod.Count} مصروف\n\n" +
                $"بعد الترحيل، سيتم قفل هذه العمليات محاسبياً نهائياً ولن تتمكن من تعديلها أو حذفها.",
                "تأكيد ترحيل وإقفال فترة زمنية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes) return;

            var postingService = _postingService;
            var currentUserId = _sessionService.CurrentUserId;
            var batchResult = await postingService.PostGeneralExpensesBatchAsync(expensesInPeriod, currentUserId, $"ترحيل جماعي للمصاريف للفترة من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}");

            if (batchResult.Success)
            {
                Dialogs.Show(
                    $"تمت عملية الترحيل الجماعي للمصاريف بنجاح!\n\n" +
                    $"الفترة: من {startDate:dd-MM-yyyy} إلى {endDate:dd-MM-yyyy}\n" +
                    $"🔹 عدد المصاريف المرحلة: {batchResult.PostedCount}\n" +
                    $"🔹 إجمالي المصاريف المرحلة: {batchResult.TotalExpenses:N2} د.ل\n" +
                    $"🔹 زمن التنفيذ الفعلي: {batchResult.Duration.TotalMilliseconds:N0} مللي ثانية\n" +
                    $"🔹 معرف الجلسة (Session Guid):\n{batchResult.SessionGuid}\n" +
                    $"🔹 معرف التتبع (Correlation Id):\n{batchResult.CorrelationId}",
                    "نجاح الترحيل الجماعي للفترة الزمنية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadExpenses();
            }
            else
            {
                var errors = string.Join("\n", batchResult.Errors);
                Dialogs.Show($"فشلت عملية الترحيل الجماعي:\n{errors}", "خطأ في الترحيل", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show($"حدث خطأ غير متوقع أثناء الترحيل الجماعي للفترة: {ex.Message}", "خطأ قاتل", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
