using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public interface IAuthenticationService
{
    Task<User?> AuthenticateAsync(string username, string password);
    string HashPassword(string password);

    /// <summary>تسجيل دخول المستخدم أو خروجه في سجل النشاط.</summary>
    Task RecordSessionEventAsync(int userId, string action);
}
