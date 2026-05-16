using System;

namespace MeezanPOS.Domain.Entities;

public class Setting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class AuditLog : BaseEntity
{
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty; 
    public string Changes { get; set; } = string.Empty; 
    public int UserId { get; set; }
}

public class UserActionLog : BaseEntity
{
    public int UserId { get; set; }
    public string Action { get; set; } = string.Empty; 
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? IpAddress { get; set; }
}

public class Expense : BaseEntity
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime ExpenseDate { get; set; } = DateTime.Today;
    public int? CashSessionId { get; set; }
    public CashSession? CashSession { get; set; }
}
