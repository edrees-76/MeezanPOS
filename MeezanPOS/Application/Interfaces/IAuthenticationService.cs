using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Application.Interfaces;

public interface IAuthenticationService
{
    Task<User?> AuthenticateAsync(string username, string password);
    string HashPassword(string password);
}
