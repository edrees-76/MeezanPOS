using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Interfaces;

public interface IOwnerDebtService
{
    // الديون
    Task<List<OwnerDebt>> GetDebtsAsync(string? partnerName = null, OwnerDebtStatus? status = null);
    Task<OwnerDebt> RecordDebtAsync(string partnerName, decimal amount, string? expenseCategory, string? notes, DateTime date, string? sourceType = null, int? sourceId = null);
    Task DeleteDebtAsync(int debtId);

    // التسويات
    Task<List<OwnerDebtSettlement>> GetSettlementsAsync(string? partnerName = null);
    Task<OwnerDebtSettlement> RecordSettlementAsync(int? debtId, string partnerName, decimal amount, OwnerDebtSettlementSource source, int? bankAccountId, string? notes, DateTime date);
    Task DeleteSettlementAsync(int settlementId);

    // لوحة تحكم مستحقات المالك
    Task<decimal> GetTotalOwnerDebtsAsync(string? partnerName = null);
    Task<decimal> GetTotalOwnerSettlementsAsync(string? partnerName = null);
    Task<decimal> GetNetOwnerBalanceAsync(string? partnerName = null);
    Task<List<string>> GetPartnerNamesAsync();
}

