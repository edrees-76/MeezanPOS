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

        // DbContext — Scoped (مشاركة نفس الكائن لتجنب أخطاء الإقفال والتعارض المالي في SQLite)
        services.AddScoped<AppDbContext>();
        services.AddDbContextFactory<AppDbContext>();

        // Session — Singleton (مستخدم واحد طوال التطبيق)
        services.AddSingleton<ISessionService, SessionService>();

        // Authentication
        services.AddTransient<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<PeriodUnlockApproval>();

        // الخدمات المالية — Scoped لمنع تعارض المعاملات وقفل قاعدة البيانات (SQLite Deadlock)
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IBankService, BankService>();
        services.AddScoped<ICashLedgerService, CashLedgerService>();
        services.AddScoped<IPostingService, PostingService>();
        services.AddScoped<IOwnerDebtService, OwnerDebtService>();
        services.AddScoped<IWagesService, WagesService>();
        services.AddScoped<IFinancialReportingService, FinancialReportingService>();
        services.AddScoped<AuditService>();

        _provider = services.BuildServiceProvider();
    }

    /// <summary>
    /// اختصار لجلب خدمة. يُستخدم في ViewModels.
    /// </summary>
    /// <remarks>
    /// كل استدعاء ينشئ نطاقاً (Scope) جديداً: الخدمة وتبعياتها تتشارك AppDbContext واحداً خاصاً بها
    /// (لازم لمشاركة المعاملة بين PostingService و CashLedgerService مثلاً)، لكن لا يُشارك السياق
    /// مع بقية الشاشات. سابقاً كانت الخدمات تُجلب من الجذر فيعيش سياق واحد طوال عمر التطبيق،
    /// فتبقى الكيانات القديمة في الذاكرة وتُحفظ تعديلات معاملات مُلغاة، ويُستخدم السياق من خيطين معاً.
    /// الخدمات الـ Singleton (مثل الجلسة) تبقى واحدة لأن النطاق يعيدها من الجذر.
    /// </remarks>
    public static T Resolve<T>() where T : notnull
        => Provider.CreateScope().ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// خدمة لعملية واحدة: <c>using var lease = AppServiceProvider.Lease&lt;T&gt;();</c>
    /// يُغلق نطاقها وسياق قاعدة بياناتها فور انتهاء العملية بدل انتظار جامع القمامة.
    /// Resolve يبقى للخدمات التي تعيش بعمر الشاشة.
    /// </summary>
    public static ServiceLease<T> Lease<T>() where T : notnull
    {
        var scope = Provider.CreateScope();
        return new ServiceLease<T>(scope, scope.ServiceProvider.GetRequiredService<T>());
    }
}

/// <summary>خدمة مع نطاقها؛ التخلص منها يغلق النطاق وكل ما أنشأه (ومنه AppDbContext).</summary>
public sealed class ServiceLease<T> : IDisposable
{
    private readonly IServiceScope _scope;

    internal ServiceLease(IServiceScope scope, T service)
    {
        _scope = scope;
        Service = service;
    }

    public T Service { get; }

    public void Dispose() => _scope.Dispose();
}
