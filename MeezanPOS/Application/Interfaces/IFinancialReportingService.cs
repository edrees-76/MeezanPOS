using System;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public interface IFinancialReportingService
{
    Task<ClosingAccountSummary> GenerateSummaryAsync(DateTime start, DateTime end);
}
