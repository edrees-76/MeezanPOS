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

    public void SetUser(User user)
    {
        CurrentUser = user;
        MeezanPOS.Infrastructure.Data.AuditContext.CurrentUserId = user.Id;
    }

    public void ClearSession()
    {
        CurrentUser = null;
        MeezanPOS.Infrastructure.Data.AuditContext.CurrentUserId = null;
    }

    public bool HasPermission(string operation)
    {
        if (CurrentUser?.Role == null) return false;
        return Permissions.RoleAllows(CurrentUser.Role.Type, operation);
    }

    public void RequirePermission(string operation)
    {
        if (!HasPermission(operation))
            throw new PermissionDeniedException();
    }
}
