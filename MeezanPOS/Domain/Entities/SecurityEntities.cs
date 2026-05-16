using MeezanPOS.Domain.Enums;
using System.Collections.Generic;

namespace MeezanPOS.Domain.Entities;

public class Role : BaseEntity
{
    public RoleType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class User : BaseEntity
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public Role? Role { get; set; }
}
