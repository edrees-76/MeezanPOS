using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

public class FinancialReportingService : IFinancialReportingService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public FinancialReportingService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    public async Task<ClosingAccountSummary> GenerateSummaryAsync(DateTime start, DateTime end)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();

        var summary = new ClosingAccountSummary
        {
            StartDate = start,
            EndDate = end,
            PeriodText = $"الفترة من {start:yyyy-MM-dd} إلى {end:yyyy-MM-dd}"
        };

        // 1. Income Statement (قائمة الدخل)
        // Load posted journals and general expenses
        var journals = await context.DailyJournals
            .AsNoTracking()
            .Where(j => j.JournalDate >= start && j.JournalDate <= end &&
                        (j.FinancialStatus == FinancialStatus.Posted || j.FinancialStatus == FinancialStatus.Archived))
            .ToListAsync();

        var genExpenses = await context.GeneralExpenses
            .AsNoTracking()
            .Where(e => e.PaymentDate >= start && e.PaymentDate <= end &&
                        (e.FinancialStatus == FinancialStatus.Posted || e.FinancialStatus == FinancialStatus.Archived))
            .ToListAsync();

        var totalSales = journals.Sum(j => j.TotalSales);
        var cardSales = journals.Sum(j => j.BankingTotal);
        var cashSales = totalSales - cardSales;

        var dailyExpenses = journals.Sum(j => j.TotalExpenses);
        var generalExpensesSum = genExpenses.Sum(e => e.Amount);

        summary.IncomeStatement = new IncomeStatementReport
        {
            TotalSales = totalSales,
            CashSales = cashSales,
            CardSales = cardSales,
            DailyExpenses = dailyExpenses,
            GeneralExpenses = generalExpensesSum
        };

        // 2. Expense Category Breakdown (تحليل المصاريف)
        var dailyExpenseItems = await context.DailyExpenseItems
            .AsNoTracking()
            .Include(e => e.DailyJournal)
            .Where(e => e.DailyJournal!.JournalDate >= start && e.DailyJournal.JournalDate <= end &&
                        (e.DailyJournal.FinancialStatus == FinancialStatus.Posted || e.DailyJournal.FinancialStatus == FinancialStatus.Archived))
            .Select(e => new { Category = e.CategoryName ?? e.Category ?? "مصاريف نثرية أخرى", e.Amount })
            .ToListAsync();

        var mappedGeneralExpenses = genExpenses.Select(e => new
        {
            Category = GetGeneralExpenseTypeName(e.ExpenseType, e.CustomExpenseName),
            e.Amount
        }).ToList();

        var dailyGrouped = dailyExpenseItems
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseCategoryReportItem
            {
                CategoryName = g.Key,
                Amount = g.Sum(e => e.Amount),
                SourceType = "يومي"
            })
            .ToList();

        var generalGrouped = mappedGeneralExpenses
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseCategoryReportItem
            {
                CategoryName = g.Key,
                Amount = g.Sum(e => e.Amount),
                SourceType = "عام"
            })
            .ToList();

        var allGrouped = dailyGrouped.Concat(generalGrouped)
            .GroupBy(x => x.CategoryName)
            .Select(g => new ExpenseCategoryReportItem
            {
                CategoryName = g.Key,
                Amount = g.Sum(x => x.Amount),
                SourceType = string.Join(" + ", g.Select(x => x.SourceType).Distinct())
            })
            .ToList();

        var totalExpAmount = allGrouped.Sum(e => e.Amount);
        foreach (var item in allGrouped)
        {
            item.Percentage = totalExpAmount > 0 ? (double)(item.Amount / totalExpAmount * 100) : 0;
        }

        summary.ExpensesByCategory = allGrouped.OrderByDescending(c => c.Amount).ToList();

        // 3. Supplier Report (تقرير الموردين)
        var suppliers = await context.Suppliers
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .ToListAsync();

        var supplierReportList = new List<SupplierReportItem>();

        // Batch-load ALL supplier transactions to avoid N+1 queries
        var supplierIds = suppliers.Select(s => s.Id).ToList();

        var allPriorSupplierTx = await context.SupplierTransactions
            .AsNoTracking()
            .Where(t => supplierIds.Contains(t.SupplierId) && t.TransactionDate < start && !t.IsDeleted)
            .ToListAsync();

        var allPeriodSupplierTx = await context.SupplierTransactions
            .AsNoTracking()
            .Where(t => supplierIds.Contains(t.SupplierId) && t.TransactionDate >= start && t.TransactionDate <= end && !t.IsDeleted)
            .ToListAsync();

        var priorTxBySupplierId = allPriorSupplierTx
            .GroupBy(t => t.SupplierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var periodTxBySupplierId = allPeriodSupplierTx
            .GroupBy(t => t.SupplierId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var supplier in suppliers)
        {
            // Opening balance before start date
            var opBalTx = priorTxBySupplierId.TryGetValue(supplier.Id, out var priorList)
                ? priorList.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id).FirstOrDefault()
                : null;

            decimal openingBalance = opBalTx?.BalanceAfter ?? 0m;

            // Activity during period
            var periodTx = periodTxBySupplierId.TryGetValue(supplier.Id, out var periodList)
                ? periodList
                : new List<SupplierTransaction>();

            decimal purchases = periodTx.Where(t => t.Type == SupplierTransactionType.IncreaseDebt).Sum(t => t.Amount);
            decimal payments = periodTx.Where(t => t.Type == SupplierTransactionType.DecreaseDebt).Sum(t => t.Amount);

            // Closing balance
            var clBalTx = periodTx.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id).FirstOrDefault();
            decimal closingBalance = clBalTx != null ? clBalTx.BalanceAfter : openingBalance;

            supplierReportList.Add(new SupplierReportItem
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,
                OpeningBalance = openingBalance,
                TotalPurchases = purchases,
                TotalPayments = payments,
                ClosingBalance = closingBalance
            });
        }
        summary.Suppliers = supplierReportList.OrderBy(s => s.SupplierName).ToList();

        // 4. Cash Ledger (تقرير حركة الخزينة)
        var lastOpMovement = await context.CashMovements
            .AsNoTracking()
            .Where(m => m.TransactionDate < start && !m.IsDeleted && !m.IsReversed)
            .OrderByDescending(m => m.TransactionDate)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync();

        decimal cashOpening = lastOpMovement?.BalanceAfter ?? 0m;

        var periodMovements = await context.CashMovements
            .AsNoTracking()
            .Where(m => m.TransactionDate >= start && m.TransactionDate <= end && !m.IsDeleted && !m.IsReversed)
            .ToListAsync();

        decimal cashIn = periodMovements.Where(m => m.Type == CashMovementType.CashIn).Sum(m => m.Amount);
        decimal cashOut = periodMovements.Where(m => m.Type == CashMovementType.CashOut).Sum(m => m.Amount);

        var lastPeriodMovement = periodMovements.OrderByDescending(m => m.TransactionDate).ThenByDescending(m => m.Id).FirstOrDefault();
        decimal cashClosing = lastPeriodMovement != null ? lastPeriodMovement.BalanceAfter : cashOpening;

        summary.CashLedger = new CashLedgerReport
        {
            OpeningBalance = cashOpening,
            TotalCashIn = cashIn,
            TotalCashOut = cashOut,
            ClosingBalance = cashClosing
        };

        // 5. Liabilities Summary (الالتزامات والمستحقات)
        var liabilities = new LiabilitiesSummary
        {
            TotalSupplierDebt = supplierReportList.Sum(s => s.ClosingBalance)
        };

        // Worker Advances & Wages Unpaid (calculated per-worker balance at endDate)
        var workerTransactions = await context.WorkerTransactions
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.TransactionDate <= end)
            .ToListAsync();

        var workerGroups = workerTransactions
            .GroupBy(t => t.WorkerId)
            .ToList();

        var workers = await context.Workers.AsNoTracking().Where(w => !w.IsDeleted).ToListAsync();
        var workersDict = workers.ToDictionary(w => w.Id);

        foreach (var group in workerGroups)
        {
            if (!workersDict.TryGetValue(group.Key, out var worker))
                continue;

            decimal totalAccrued = group.Sum(t => t.CreditAmount);
            decimal totalPaid = group.Sum(t => t.DebitAmount);
            DateTime lastActivity = group.Max(t => t.TransactionDate);

            if (totalPaid > totalAccrued)
            {
                // Worker owes restaurant (Outstanding Advance)
                liabilities.WorkerAdvanceDetails.Add(new WorkerAdvanceDetailItem
                {
                    WorkerName = worker.WorkerName,
                    AdvanceAmount = totalPaid - totalAccrued,
                    LastTransactionDate = lastActivity
                });
            }
            else if (totalAccrued > totalPaid)
            {
                // Restaurant owes worker (Unpaid wages)
                liabilities.WorkerUnpaidWageDetails.Add(new WorkerUnpaidWageDetailItem
                {
                    WorkerName = worker.WorkerName,
                    UnpaidAmount = totalAccrued - totalPaid,
                    LastAccrualDate = lastActivity
                });
            }
        }

        liabilities.TotalWorkerAdvancesOutstanding = liabilities.WorkerAdvanceDetails.Sum(w => w.AdvanceAmount);
        liabilities.TotalWorkerWagesUnpaid = liabilities.WorkerUnpaidWageDetails.Sum(w => w.UnpaidAmount);

        // Owner Receivables & Obligations
        var ownerDebts = await context.OwnerDebts
            .AsNoTracking()
            .Where(d => !d.IsDeleted && d.TransactionDate <= end)
            .ToListAsync();

        var ownerSettlements = await context.OwnerDebtSettlements
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.SettlementDate <= end)
            .ToListAsync();

        var ownerDebtGroups = ownerDebts.GroupBy(d => d.PartnerName).ToDictionary(g => g.Key, g => g.ToList());
        var ownerSettlementGroups = ownerSettlements.GroupBy(s => s.PartnerName).ToDictionary(g => g.Key, g => g.ToList());

        var allPartnerNames = ownerDebtGroups.Keys.Union(ownerSettlementGroups.Keys).OrderBy(n => n).ToList();

        foreach (var partner in allPartnerNames)
        {
            decimal totalDebts = ownerDebtGroups.TryGetValue(partner, out var dl) ? dl.Sum(d => d.Amount) : 0m;
            decimal totalSettlements = ownerSettlementGroups.TryGetValue(partner, out var sl) ? sl.Sum(s => s.Amount) : 0m;

            DateTime lastDebtDate = ownerDebtGroups.TryGetValue(partner, out var dlList) && dlList.Any() ? dlList.Max(d => d.TransactionDate) : DateTime.MinValue;
            DateTime lastSettleDate = ownerSettlementGroups.TryGetValue(partner, out var slList) && slList.Any() ? slList.Max(s => s.SettlementDate) : DateTime.MinValue;
            DateTime lastActivityDate = lastDebtDate > lastSettleDate ? lastDebtDate : lastSettleDate;
            if (lastActivityDate == DateTime.MinValue) lastActivityDate = DateTime.Now;

            decimal netBalance = totalDebts - totalSettlements;

            if (netBalance > 0)
            {
                // Restaurant owes owner (Owner Receivables / "ما له")
                liabilities.TotalOwnerReceivables += netBalance;
                liabilities.OwnerDebtDetails.Add(new OwnerDebtDetailItem
                {
                    PartnerName = partner,
                    Amount = netBalance,
                    Type = "مستحق له",
                    TransactionDate = lastActivityDate
                });
            }
            else if (netBalance < 0)
            {
                // Owner owes restaurant (Owner Obligations / "عليه")
                decimal netDebtVal = Math.Abs(netBalance);
                liabilities.TotalOwnerObligations += netDebtVal;
                liabilities.OwnerDebtDetails.Add(new OwnerDebtDetailItem
                {
                    PartnerName = partner,
                    Amount = netDebtVal,
                    Type = "عليه",
                    TransactionDate = lastActivityDate
                });
            }
        }

        liabilities.WorkerAdvanceDetails = liabilities.WorkerAdvanceDetails.OrderBy(d => d.WorkerName).ToList();
        liabilities.WorkerUnpaidWageDetails = liabilities.WorkerUnpaidWageDetails.OrderBy(d => d.WorkerName).ToList();
        liabilities.OwnerDebtDetails = liabilities.OwnerDebtDetails.OrderBy(d => d.PartnerName).ToList();

        summary.Liabilities = liabilities;

        // 6. Bank Accounts Report (تقرير الحسابات المصرفية)
        var bankAccounts = await context.BankAccounts
            .AsNoTracking()
            .Where(b => !b.IsDeleted)
            .ToListAsync();

        var bankReportList = new List<BankAccountReportItem>();

        // Batch-load ALL bank transactions to avoid N+1 queries
        var accountIds = bankAccounts.Select(a => a.Id).ToList();

        var allPriorBankTxns = await context.BankTransactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.BankAccountId) && !t.IsDeleted && t.TransactionDate < start)
            .ToListAsync();

        var allPeriodBankTxns = await context.BankTransactions
            .AsNoTracking()
            .Where(t => accountIds.Contains(t.BankAccountId) && !t.IsDeleted && t.TransactionDate >= start && t.TransactionDate <= end)
            .ToListAsync();

        var priorTxnsByAccountId = allPriorBankTxns
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.TransactionDate).ThenBy(t => t.CreatedAt).ToList());

        var periodTxnsByAccountId = allPeriodBankTxns
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var account in bankAccounts)
        {
            var priorTransactions = priorTxnsByAccountId.TryGetValue(account.Id, out var priorList)
                ? priorList
                : new List<BankTransaction>();

            decimal openingBalance = account.OpeningBalance;
            foreach (var tx in priorTransactions)
            {
                openingBalance += CalculateBankBalanceChange(tx.Type, tx.Amount, tx.Notes);
            }

            var periodTransactions = periodTxnsByAccountId.TryGetValue(account.Id, out var periodList)
                ? periodList
                : new List<BankTransaction>();

            decimal totalDeposits = 0;
            decimal totalWithdrawals = 0;

            foreach (var tx in periodTransactions)
            {
                decimal change = CalculateBankBalanceChange(tx.Type, tx.Amount, tx.Notes);
                if (change > 0)
                {
                    totalDeposits += change;
                }
                else if (change < 0)
                {
                    totalWithdrawals += Math.Abs(change);
                }
            }

            decimal closingBalance = openingBalance + totalDeposits - totalWithdrawals;

            bankReportList.Add(new BankAccountReportItem
            {
                BankAccountId = account.Id,
                AccountName = account.FriendlyName,
                BankName = account.BankName ?? string.Empty,
                AccountNumber = account.AccountNumber ?? string.Empty,
                OpeningBalance = openingBalance,
                TotalDeposits = totalDeposits,
                TotalWithdrawals = totalWithdrawals,
                ClosingBalance = closingBalance
            });
        }
        summary.BankAccounts = bankReportList.OrderBy(b => b.AccountName).ToList();

        return summary;
    }

    private string GetGeneralExpenseTypeName(GeneralExpenseType type, string? customName)
    {
        if (type == GeneralExpenseType.Other && !string.IsNullOrWhiteSpace(customName))
            return customName;

        return type switch
        {
            GeneralExpenseType.Rent => "إيجار",
            GeneralExpenseType.Electricity => "كهرباء",
            GeneralExpenseType.Water => "ماء",
            GeneralExpenseType.Salaries => "رواتب/أجور عمال",
            GeneralExpenseType.Internet => "إنترنت",
            GeneralExpenseType.Maintenance => "صيانة",
            GeneralExpenseType.Insurance => "تأمين",
            GeneralExpenseType.Taxes => "ضرائب/رسوم",
            GeneralExpenseType.SupplierPayment => "تسديد موردين",
            _ => "أخرى"
        };
    }

    private decimal CalculateBankBalanceChange(BankTransactionType type, decimal amount, string? notes = null)
    {
        return type switch
        {
            BankTransactionType.Deposit => amount,
            BankTransactionType.CardSalesDeposit => amount,
            BankTransactionType.Withdrawal => -amount,
            BankTransactionType.SupplierPayment => -amount,
            BankTransactionType.ExpensePayment => -amount,
            BankTransactionType.OwnerDebtSettlement => -amount,
            BankTransactionType.InternalTransferOut => -amount,
            BankTransactionType.InternalTransferIn => amount,
            BankTransactionType.InternalTransfer => (notes != null && (notes.StartsWith("تحويل من") || notes.Contains("من "))) ? amount : -amount,
            BankTransactionType.ExchangeDifference => amount,
            _ => 0
        };
    }
}
