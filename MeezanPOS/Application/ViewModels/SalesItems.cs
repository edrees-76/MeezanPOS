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

public partial class SelectableDailyJournal : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private int sequence;

    public DailyJournal Journal { get; }

    public SelectableDailyJournal(DailyJournal journal)
    {
        Journal = journal;
    }
}

public partial class MonthSummaryCard : ObservableObject
{
    [ObservableProperty]
    private int sequence;

    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetProfit))]
    private decimal totalSales;

    [ObservableProperty]
    private decimal totalCashSales;

    [ObservableProperty]
    private decimal totalBankingSales;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetProfit))]
    private decimal totalExpenses;

    public decimal NetProfit => TotalSales - TotalExpenses;

    [ObservableProperty]
    private int daysCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CardBackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    private bool isPosted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CardBackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    private bool isPartiallyPosted;

    public string StatusText => IsPosted ? "مرحّل بالكامل" : (IsPartiallyPosted ? "مرحّل جزئياً" : "مفتوح");
    public string StatusColor => IsPosted ? "#10b981" : (IsPartiallyPosted ? "#f59e0b" : "#3b82f6");
    public string CardBackground => IsPosted ? "#f0fdf4" : (IsPartiallyPosted ? "#fffbeb" : "#f8faff");
    public string CardBorderBrush => IsPosted ? "#dcfce7" : (IsPartiallyPosted ? "#fef3c7" : "#e5eeff");
}
