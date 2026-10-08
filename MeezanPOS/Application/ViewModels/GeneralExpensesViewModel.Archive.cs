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

/// <summary>أرشيف المصروفات المرحّلة حسب الشهر.</summary>
public partial class GeneralExpensesViewModel
{
    [RelayCommand]
    public void LoadArchivedMonths()
    {
        try
        {
            var expenses = _expenses.GetPostedInRange(DateFrom, DateTo);

            var grouped = expenses
                .GroupBy(e => new { e.PaymentDate.Year, e.PaymentDate.Month })
                .Select(g => {
                    var year = g.Key.Year;
                    var month = g.Key.Month;
                    var postedCount = g.Count(e => e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived);
                    var totalCount = g.Count();
                    var isPosted = postedCount == totalCount;
                    var isPartiallyPosted = postedCount > 0 && postedCount < totalCount;
                    return new GeneralExpenseMonthCard
                    {
                        Year = year,
                        Month = month,
                        MonthName = $"{ExpenseManagementViewModel.GetArabicMonthName(month)} {year}",
                        TotalExpenses = g.Sum(e => e.Amount),
                        OperationsCount = totalCount,
                        IsPosted = isPosted,
                        IsPartiallyPosted = isPartiallyPosted
                    };
                })
                .OrderByDescending(m => m.Year)
                .ThenByDescending(m => m.Month)
                .Select((m, idx) => {
                    m.Sequence = idx + 1;
                    return m;
                })
                .ToList();

            ArchivedMonths.Clear();
            foreach (var m in grouped)
            {
                ArchivedMonths.Add(m);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show($"خطأ في تحميل كروت أشهر الأرشيف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SelectMonth(GeneralExpenseMonthCard month)
    {
        SelectedArchivedMonth = month;
        ArchiveLevel = 1;
        LoadExpenses();
    }

    [RelayCommand]
    private void GoBackToMonths()
    {
        ArchiveLevel = 0;
        SelectedArchivedMonth = null;
        LoadArchivedMonths();
    }
}
