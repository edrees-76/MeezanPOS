using System.Linq;
using System.Threading.Tasks;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.Services;

/// <summary>
/// اعتماد فك قفل فترة مالية مسوّاة بكلمة مرور.
/// </summary>
/// <remarks>
/// إذا وُجد في المنظومة مستخدم آخر يملك صلاحية فك القفل، يجب أن يعتمد العملية شخص غير المستخدم الحالي
/// (مبدأ الشخصين). إذا كان المدير العام هو الوحيد المخوَّل، يكفي أن يعيد إدخال كلمة مروره.
/// </remarks>
public sealed class PeriodUnlockApproval
{
    private readonly AppDbContext _context;
    private readonly IAuthenticationService _auth;

    public PeriodUnlockApproval(AppDbContext context, IAuthenticationService auth)
    {
        _context = context;
        _auth = auth;
    }

    public sealed record Result(User? Approver, string? Error)
    {
        public bool IsApproved => Approver != null;
    }

    public async Task<Result> VerifyAsync(string username, string password, int currentUserId)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new Result(null, "أدخل اسم المستخدم وكلمة المرور للمعتمِد.");

        var approver = await _auth.AuthenticateAsync(username.Trim(), password);
        if (approver == null)
            return new Result(null, "بيانات المعتمِد غير صحيحة أو الحساب مقفل.");

        var role = approver.Role?.Type
            ?? await _context.Roles.Where(r => r.Id == approver.RoleId).Select(r => r.Type).FirstAsync();
        if (!Permissions.RoleAllows(role, Permissions.UnlockPeriod))
            return new Result(null, "المعتمِد لا يملك صلاحية فك قفل الفترات.");

        if (approver.Id == currentUserId)
        {
            var authorizedRoles = await _context.Roles.Select(r => new { r.Id, r.Type }).ToListAsync();
            var authorizedRoleIds = authorizedRoles
                .Where(r => Permissions.RoleAllows(r.Type, Permissions.UnlockPeriod))
                .Select(r => r.Id)
                .ToList();
            var otherApprovers = await _context.Users
                .CountAsync(u => u.Id != currentUserId && u.IsActive && authorizedRoleIds.Contains(u.RoleId));
            if (otherApprovers > 0)
                return new Result(null, "يجب أن يعتمد فك القفل مستخدم مخوَّل آخر غير المستخدم الحالي.");
        }

        return new Result(approver, null);
    }
}
