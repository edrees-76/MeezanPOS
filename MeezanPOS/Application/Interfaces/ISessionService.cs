using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public interface ISessionService
{
    User? CurrentUser { get; }
    string CurrentUserId { get; }
    string CurrentUsername { get; }
    bool MustChangePassword { get; }
    void SetUser(User user);
    void ClearSession();
    bool HasPermission(string operation);

    /// <summary>يرمي PermissionDeniedException إذا لم يملك المستخدم الصلاحية.</summary>
    void RequirePermission(string operation);
}
