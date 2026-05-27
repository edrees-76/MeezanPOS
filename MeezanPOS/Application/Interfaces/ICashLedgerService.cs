using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public interface ICashLedgerService
{
    // إضافة حركة نقدية جديدة (وارد/صادر) وحساب الرصيد التراكمي تلقائياً
    Task<CashMovement> RecordMovementAsync(CashMovementType type, decimal amount, string? sourceType, int? sourceId, string? notes, DateTime? date = null);
    
    // إبطال/عكس حركة سابقة (Reversal Event)
    Task<CashMovement> ReverseMovementAsync(int movementId, string? reason);
    
    // جرد وإعادة بناء الأرصدة التراكمية بالكامل في حال حدوث أي خلل (Rebuild Safety Tool)
    Task RebuildLedgerAsync();
    
    // الحصول على الرصيد اللحظي الحالي
    Task<decimal> GetCurrentBalanceAsync();

    // التحقق مما إذا كانت إعادة بناء الدفتر مطلوبة
    Task<bool> IsRebuildRequiredAsync();
}
