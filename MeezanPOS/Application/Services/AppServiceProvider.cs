using Microsoft.Extensions.DependencyInjection;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.Services;

/// <summary>
/// حاوية حقن التبعيات المركزية للتطبيق
/// </summary>
public static class AppServiceProvider
{
    private static IServiceProvider? _provider;
    public static IServiceProvider Provider => _provider
        ?? throw new InvalidOperationException("Services not initialized. Call Initialize() first.");

    public static void Initialize()
    {
        var services = new ServiceCollection();

        // DbContext — Transient (كل طلب context جديد)
        services.AddTransient<AppDbContext>();

        // Session — Singleton (مستخدم واحد طوال التطبيق)
        services.AddSingleton<ISessionService, SessionService>();

        // Authentication
        services.AddTransient<IAuthenticationService, AuthenticationService>();

        // الخدمات المالية — Transient
        services.AddTransient<ILedgerService, LedgerService>();
        services.AddTransient<IBankService, BankService>();
        services.AddTransient<ICashLedgerService, CashLedgerService>();
        services.AddTransient<IPostingService, PostingService>();
        services.AddTransient<IOwnerDebtService, OwnerDebtService>();
        services.AddTransient<IWagesService, WagesService>();
        services.AddTransient<AuditService>();

        _provider = services.BuildServiceProvider();
    }

    /// <summary>
    /// اختصار لجلب خدمة. يُستخدم في ViewModels.
    /// </summary>
    public static T Resolve<T>() where T : notnull
        => Provider.GetRequiredService<T>();
}
