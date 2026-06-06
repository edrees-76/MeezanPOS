using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Application.Services;

public class SessionService : ISessionService
{
    public User? CurrentUser { get; private set; }
    public string CurrentUserId => CurrentUser?.Id.ToString() ?? "Unknown";
    public string CurrentUsername => CurrentUser?.Username ?? "Unknown";
    public bool MustChangePassword => CurrentUser?.MustChangePassword ?? false;

    public void SetUser(User user) => CurrentUser = user;
    public void ClearSession() => CurrentUser = null;

    public bool HasPermission(string operation)
    {
        if (CurrentUser?.Role == null) return false;
        return CurrentUser.Role.Type switch
        {
            RoleType.Admin => true,
            RoleType.Manager => operation is not "DeleteSystem" and not "UnpostFinancial",
            RoleType.Cashier => operation is "CreateJournal" or "ViewSales" or "ViewDashboard",
            _ => false
        };
    }
}
