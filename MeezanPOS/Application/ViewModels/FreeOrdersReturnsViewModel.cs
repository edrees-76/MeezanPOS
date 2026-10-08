using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.ViewModels;

public class DailyRowItem
{
    public int Sequence { get; set; }
    public DateTime JournalDate { get; set; }
    public string DateDisplay => JournalDate.ToString("yyyy/MM/dd");
    public decimal FreeOrdersTotal { get; set; }
    public decimal ReturnsTotal { get; set; }
    public List<DailyJournal> Journals { get; set; } = new();
}

public class MonthAdjustmentCard
{
    public int Sequence { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public decimal FreeOrdersTotal { get; set; }
    public decimal ReturnsTotal { get; set; }
    public int DaysCount { get; set; }
}

public class ShiftAdjustmentRow
{
    public string ShiftName { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public decimal FreeOrdersTotal { get; set; }
    public decimal ReturnsTotal { get; set; }
    public int JournalId { get; set; }
}

public class AdjustmentDetailRow
{
    public int SequenceNumber { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class TopRecipientItem
{
    public string PersonName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

public partial class FreeOrdersReturnsViewModel : ObservableObject
{
    private List<DailyJournal> _allJournals = new();

    [ObservableProperty] private int selectedTab = 0;
    [ObservableProperty] private int archiveLevel = 0;
    [ObservableProperty] private bool isLoading = false;

    [ObservableProperty]
    private ObservableCollection<DailyRowItem> currentJournals = new();

    [ObservableProperty]
    private ObservableCollection<MonthAdjustmentCard> archiveMonths = new();

    [ObservableProperty]
    private ObservableCollection<DailyRowItem> archivedJournals = new();

    [ObservableProperty]
    private MonthAdjustmentCard? selectedArchiveMonth;

    [ObservableProperty]
    private ObservableCollection<ShiftAdjustmentRow> selectedDayShifts = new();
    [ObservableProperty] private bool isShiftPopupOpen = false;
    [ObservableProperty] private string selectedDayTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<AdjustmentDetailRow> selectedShiftDetails = new();
    [ObservableProperty] private bool isDetailPopupOpen = false;
    [ObservableProperty] private string selectedShiftTitle = string.Empty;
    [ObservableProperty] private bool isDetailFreeOrder = true;

    [ObservableProperty] private decimal monthlyFreeOrdersTotal;
    [ObservableProperty] private decimal monthlyReturnsTotal;
    [ObservableProperty] private decimal dailyAvgFreeOrders;
    [ObservableProperty] private decimal dailyAvgReturns;
    [ObservableProperty] private string topFreeOrderDay = string.Empty;
    [ObservableProperty] private string topReturnsDay = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TopRecipientItem> topFreeOrderRecipients = new();

    public async Task LoadDataAsync()
    {
        IsLoading = true;
        _allJournals = await new MeezanPOS.Application.Services.Queries.JournalExpenseQueryService().GetJournalsWithAdjustmentsAsync();
        ApplyFilters();
        IsLoading = false;
    }

    private void ApplyFilters()
    {
        if (SelectedTab == 0) LoadCurrentTab();
        else LoadArchiveTab();
    }

    private void LoadCurrentTab()
    {
        var rows = _allJournals
            .Where(j => j.FinancialStatus == FinancialStatus.Draft)
            .GroupBy(j => j.JournalDate.Date)
            .Select(g => new DailyRowItem
            {
                JournalDate = g.Key,
                FreeOrdersTotal = g.Sum(j => j.FreeOrdersTotal),
                ReturnsTotal = g.Sum(j => j.ReturnsTotal),
                Journals = g.ToList()
            })
            .OrderByDescending(d => d.JournalDate)
            .Select((d, idx) => {
                d.Sequence = idx + 1;
                return d;
            })
            .ToList();
        CurrentJournals = new ObservableCollection<DailyRowItem>(rows);
        CalculateMonthlyStats(rows);
    }

    private void LoadArchiveTab()
    {
        var posted = _allJournals
            .Where(j => j.FinancialStatus == FinancialStatus.Posted
                     || j.FinancialStatus == FinancialStatus.Archived);

        if (ArchiveLevel == 0)
        {
            var months = posted
                .GroupBy(j => new { j.JournalDate.Year, j.JournalDate.Month })
                .Select(g => new MonthAdjustmentCard
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    MonthName = $"{GetArabicMonthName(g.Key.Month)} {g.Key.Year}",
                    FreeOrdersTotal = g.Sum(j => j.FreeOrdersTotal),
                    ReturnsTotal = g.Sum(j => j.ReturnsTotal),
                    DaysCount = g.Select(j => j.JournalDate.Date).Distinct().Count()
                })
                .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
                .Select((m, idx) => {
                    m.Sequence = idx + 1;
                    return m;
                })
                .ToList();
            ArchiveMonths = new ObservableCollection<MonthAdjustmentCard>(months);

            var days = posted
                .GroupBy(j => j.JournalDate.Date)
                .Select(g => new DailyRowItem
                {
                    JournalDate = g.Key,
                    FreeOrdersTotal = g.Sum(j => j.FreeOrdersTotal),
                    ReturnsTotal = g.Sum(j => j.ReturnsTotal),
                    Journals = g.ToList()
                })
                .ToList();
            CalculateMonthlyStats(days);
        }
        else
        {
            if (SelectedArchiveMonth == null) return;
            var days = posted
                .Where(j => j.JournalDate.Year == SelectedArchiveMonth.Year
                         && j.JournalDate.Month == SelectedArchiveMonth.Month)
                .GroupBy(j => j.JournalDate.Date)
                .Select(g => new DailyRowItem
                {
                    JournalDate = g.Key,
                    FreeOrdersTotal = g.Sum(j => j.FreeOrdersTotal),
                    ReturnsTotal = g.Sum(j => j.ReturnsTotal),
                    Journals = g.ToList()
                })
                .OrderByDescending(d => d.JournalDate)
                .Select((d, idx) => {
                    d.Sequence = idx + 1;
                    return d;
                })
                .ToList();
            ArchivedJournals = new ObservableCollection<DailyRowItem>(days);
            CalculateMonthlyStats(days);
        }
    }

    private void CalculateMonthlyStats(List<DailyRowItem> days)
    {
        MonthlyFreeOrdersTotal = days.Sum(d => d.FreeOrdersTotal);
        MonthlyReturnsTotal = days.Sum(d => d.ReturnsTotal);
        DailyAvgFreeOrders = days.Count > 0
            ? Math.Round(MonthlyFreeOrdersTotal / days.Count, 2) : 0;
        DailyAvgReturns = days.Count > 0
            ? Math.Round(MonthlyReturnsTotal / days.Count, 2) : 0;

        var topFree = days.OrderByDescending(d => d.FreeOrdersTotal).FirstOrDefault();
        var topRet = days.OrderByDescending(d => d.ReturnsTotal).FirstOrDefault();
        TopFreeOrderDay = topFree != null
            ? $"{topFree.DateDisplay} — {topFree.FreeOrdersTotal:N2} د.ل" : "—";
        TopReturnsDay = topRet != null
            ? $"{topRet.DateDisplay} — {topRet.ReturnsTotal:N2} د.ل" : "—";

        // حساب أعلى المستلمين للطلبات المجانية (توب 5)
        var topRecipients = days
            .SelectMany(d => d.Journals)
            .SelectMany(j => j.Adjustments)
            .Where(a => a.IsFreeOrder && !string.IsNullOrWhiteSpace(a.PersonName) && a.PersonName != "—")
            .GroupBy(a => a.PersonName!.Trim())
            .Select(g => new TopRecipientItem
            {
                PersonName = g.Key,
                TotalAmount = g.Sum(a => a.Amount)
            })
            .OrderByDescending(x => x.TotalAmount)
            .Take(5)
            .ToList();

        TopFreeOrderRecipients = new ObservableCollection<TopRecipientItem>(topRecipients);
    }

    [RelayCommand]
    public void SetTab(string param)
    {
        SelectedTab = int.Parse(param);
        ArchiveLevel = 0;
        ApplyFilters();
    }

    [RelayCommand]
    public void SelectMonth(MonthAdjustmentCard month)
    {
        SelectedArchiveMonth = month;
        ArchiveLevel = 1;
        ApplyFilters();
    }

    [RelayCommand]
    public void GoBackToMonths()
    {
        ArchiveLevel = 0;
        ApplyFilters();
    }

    [RelayCommand]
    public void ShowDayDetails(DailyRowItem day)
    {
        SelectedDayTitle = $"ورديات يوم {day.DateDisplay}";
        SelectedDayShifts = new ObservableCollection<ShiftAdjustmentRow>(
            day.Journals.Select(j => new ShiftAdjustmentRow
            {
                ShiftName = GetShiftName(j.ShiftType),
                EmployeeName = j.EmployeeName,
                FreeOrdersTotal = j.FreeOrdersTotal,
                ReturnsTotal = j.ReturnsTotal,
                JournalId = j.Id
            }));
        IsShiftPopupOpen = true;
    }

    [RelayCommand]
    public void ShowDayFreeDetails(DailyRowItem day) => ShowDayDetailsInternal(day, true);

    [RelayCommand]
    public void ShowDayReturnDetails(DailyRowItem day) => ShowDayDetailsInternal(day, false);

    private void ShowDayDetailsInternal(DailyRowItem day, bool isFreeOrder)
    {
        if (day == null) return;
        IsDetailFreeOrder = isFreeOrder;
        SelectedShiftTitle = isFreeOrder
            ? $"تفاصيل الطلبات المجانية — يوم {day.DateDisplay}"
            : $"تفاصيل المرتجعات — يوم {day.DateDisplay}";

        var details = new List<AdjustmentDetailRow>();
        int seq = 1;
        foreach (var journal in day.Journals)
        {
            var adjustments = journal.Adjustments
                .Where(a => a.IsFreeOrder == isFreeOrder && !a.IsDeleted)
                .Select(a => new AdjustmentDetailRow
                {
                    SequenceNumber = seq++,
                    InvoiceNumber = a.InvoiceNumber ?? "—",
                    Amount = a.Amount,
                    PersonName = a.PersonName ?? "—",
                    Notes = a.Notes ?? "—"
                });
            details.AddRange(adjustments);
        }

        SelectedShiftDetails = new ObservableCollection<AdjustmentDetailRow>(details);
        IsDetailPopupOpen = true;
    }

    [RelayCommand]
    public void ShowShiftFreeDetails(ShiftAdjustmentRow shift) => ShowShiftDetailsInternal(shift, true);

    [RelayCommand]
    public void ShowShiftReturnDetails(ShiftAdjustmentRow shift) => ShowShiftDetailsInternal(shift, false);

    private void ShowShiftDetailsInternal(ShiftAdjustmentRow shift, bool isFreeOrder)
    {
        if (shift == null) return;
        IsDetailFreeOrder = isFreeOrder;
        SelectedShiftTitle = isFreeOrder
            ? $"الطلبات المجانية — {shift.ShiftName} — {shift.EmployeeName}"
            : $"المرتجعات — {shift.ShiftName} — {shift.EmployeeName}";

        var journal = _allJournals.FirstOrDefault(j => j.Id == shift.JournalId);
        if (journal == null) return;

        int seq = 1;
        SelectedShiftDetails = new ObservableCollection<AdjustmentDetailRow>(
            journal.Adjustments
                .Where(a => a.IsFreeOrder == isFreeOrder && !a.IsDeleted)
                .Select(a => new AdjustmentDetailRow
                {
                    SequenceNumber = seq++,
                    InvoiceNumber = a.InvoiceNumber ?? "—",
                    Amount = a.Amount,
                    PersonName = a.PersonName ?? "—",
                    Notes = a.Notes ?? "—"
                }));
        IsDetailPopupOpen = true;
    }

    [RelayCommand]
    public void CloseShiftPopup() => IsShiftPopupOpen = false;

    [RelayCommand]
    public void CloseDetailPopup() => IsDetailPopupOpen = false;

    [RelayCommand]
    public async Task LoadedAsync() => await LoadDataAsync();

    private static string GetShiftName(ShiftType type) => type switch
    {
        ShiftType.FirstShift => "وردية أولى",
        ShiftType.SecondShift => "وردية ثانية",
        ShiftType.FullDay => "يوم كامل",
        _ => "وردية"
    };

    private static string GetArabicMonthName(int month) => month switch
    {
        1 => "يناير", 2 => "فبراير", 3 => "مارس",
        4 => "أبريل", 5 => "مايو", 6 => "يونيو",
        7 => "يوليو", 8 => "أغسطس", 9 => "سبتمبر",
        10 => "أكتوبر", 11 => "نوفمبر", 12 => "ديسمبر",
        _ => string.Empty
    };
}
