using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.ViewModels
{
    public class WorkerWageInputModel : ObservableObject
    {
        private bool _isAttended;
        private bool _isAbsent;
        private decimal _dailyWage;
        private decimal _actualWage;
        private decimal _advance;
        private decimal _deduction;
        private decimal _amountPaid;
        private string _notes = string.Empty;

        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;

        public bool IsAttended
        {
            get => _isAttended;
            set
            {
                if (SetProperty(ref _isAttended, value))
                {
                    if (value)
                    {
                        IsAbsent = false;
                        if (ActualWage == 0)
                        {
                            ActualWage = DailyWage;
                        }
                        if (AmountPaid == 0)
                        {
                            AmountPaid = DailyWage;
                        }
                    }
                    StatusChanged?.Invoke();
                }
            }
        }

        public bool IsAbsent
        {
            get => _isAbsent;
            set
            {
                if (SetProperty(ref _isAbsent, value))
                {
                    if (value)
                    {
                        IsAttended = false;
                        ActualWage = 0;
                        AmountPaid = 0;
                        Advance = 0;
                        Deduction = 0;
                    }
                    StatusChanged?.Invoke();
                }
            }
        }

        public decimal DailyWage
        {
            get => _dailyWage;
            set => SetProperty(ref _dailyWage, value);
        }

        public decimal ActualWage
        {
            get => _actualWage;
            set
            {
                if (SetProperty(ref _actualWage, value))
                {
                    if (IsAttended && AmountPaid == 0)
                    {
                        AmountPaid = value;
                    }
                    AmountsChanged?.Invoke();
                }
            }
        }

        public decimal Advance
        {
            get => _advance;
            set
            {
                if (SetProperty(ref _advance, value))
                {
                    AmountsChanged?.Invoke();
                }
            }
        }

        public decimal Deduction
        {
            get => _deduction;
            set
            {
                if (SetProperty(ref _deduction, value))
                {
                    AmountsChanged?.Invoke();
                }
            }
        }

        public decimal AmountPaid
        {
            get => _amountPaid;
            set
            {
                if (SetProperty(ref _amountPaid, value))
                {
                    AmountsChanged?.Invoke();
                }
            }
        }

        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        public event Action? AmountsChanged;
        public event Action? StatusChanged;
    }

    public partial class WorkerWagesDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _totalActiveWorkers;

        [ObservableProperty]
        private int _totalAttended;

        [ObservableProperty]
        private int _totalAbsent;

        [ObservableProperty]
        private decimal _totalDefaultWages;

        [ObservableProperty]
        private decimal _totalAttendedDefaultWages;

        [ObservableProperty]
        private decimal _totalActualWages;

        [ObservableProperty]
        private decimal _totalAdvances;

        [ObservableProperty]
        private decimal _totalDeductions;

        [ObservableProperty]
        private decimal _totalAmountPaid;

        public ObservableCollection<WorkerWageInputModel> WorkersList { get; } = new();

        public bool? DialogResult { get; private set; }

        public List<WorkerTransactionDetailDto> ResultDetails { get; private set; } = new();

        public WorkerWagesDialogViewModel()
        {
        }

        public async Task LoadWorkersAsync(List<WorkerTransactionDetailDto>? existingDetails = null)
        {
            WorkersList.Clear();

            using var db = new AppDbContext();
            var activeWorkers = await db.Workers
                .Where(w => w.IsActive && !w.IsDeleted)
                .OrderBy(w => w.WorkerName)
                .ToListAsync();

            TotalActiveWorkers = activeWorkers.Count;

            foreach (var worker in activeWorkers)
            {
                var inputModel = new WorkerWageInputModel
                {
                    WorkerId = worker.Id,
                    WorkerName = worker.WorkerName,
                    DailyWage = worker.DailyWage
                };

                // إذا كانت هناك بيانات محفوظة سابقاً (تعديل)
                if (existingDetails != null)
                {
                    var detail = existingDetails.FirstOrDefault(d => d.WorkerId == worker.Id);
                    if (detail != null)
                    {
                        inputModel.IsAttended = detail.IsAttended;
                        inputModel.IsAbsent = !detail.IsAttended;
                        inputModel.ActualWage = detail.ActualWage;
                        inputModel.Advance = detail.Advance;
                        inputModel.Deduction = detail.Deduction;
                        inputModel.AmountPaid = detail.AmountPaid;
                        inputModel.Notes = detail.Notes ?? string.Empty;
                    }
                }

                inputModel.AmountsChanged += RecalculateTotals;
                inputModel.StatusChanged += OnWorkerStatusChanged;

                WorkersList.Add(inputModel);
            }

            RecalculateTotals();
        }

        private void OnWorkerStatusChanged()
        {
            TotalAttended = WorkersList.Count(w => w.IsAttended);
            TotalAbsent = WorkersList.Count(w => w.IsAbsent);
            RecalculateTotals();
        }

        private void RecalculateTotals()
        {
            TotalDefaultWages = WorkersList.Sum(w => w.DailyWage);
            TotalAttendedDefaultWages = WorkersList.Where(w => w.IsAttended).Sum(w => w.DailyWage);
            TotalActualWages = WorkersList.Sum(w => w.ActualWage);
            TotalAdvances = WorkersList.Sum(w => w.Advance);
            TotalDeductions = WorkersList.Sum(w => w.Deduction);
            TotalAmountPaid = WorkersList.Sum(w => w.AmountPaid);
        }

        [RelayCommand]
        private void Save(Window window)
        {
            // التحقق من تحديد حالة كل عامل نشط (حضور أو غياب)
            var unselectedWorkers = WorkersList.Where(w => !w.IsAttended && !w.IsAbsent).ToList();
            if (unselectedWorkers.Any())
            {
                var names = string.Join("، ", unselectedWorkers.Select(w => w.WorkerName));
                Dialogs.Show($"يرجى تحديد حالة الحضور أو الغياب للعمال التاليين أولاً:\n{names}", 
                                "تنبيه التحقق من الحضور", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Warning);
                return;
            }

            // تجميع النتائج
            ResultDetails = WorkersList.Select(w => new WorkerTransactionDetailDto
            {
                WorkerId = w.WorkerId,
                WorkerName = w.WorkerName,
                IsAttended = w.IsAttended,
                ActualWage = w.ActualWage,
                Advance = w.Advance,
                Deduction = w.Deduction,
                AmountPaid = w.AmountPaid,
                Notes = w.Notes
            }).ToList();

            DialogResult = true;
            if (window != null)
            {
                window.DialogResult = true;
                window.Close();
            }
        }

        [RelayCommand]
        private void Cancel(Window window)
        {
            DialogResult = false;
            if (window != null)
            {
                window.DialogResult = false;
                window.Close();
            }
        }
    }

    public class WorkerTransactionDetailDto
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;
        public bool IsAttended { get; set; }
        public decimal ActualWage { get; set; }
        public decimal Advance { get; set; }
        public decimal Deduction { get; set; }
        public decimal AmountPaid { get; set; }
        public string? Notes { get; set; }
    }
}
