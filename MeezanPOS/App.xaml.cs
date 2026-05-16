using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.EntityFrameworkCore;
namespace MeezanPOS;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // التأكد من تطبيق كل التحديثات على قاعدة البيانات عند بدء التشغيل
        using (var context = new MeezanPOS.Infrastructure.Data.AppDbContext())
        {
            context.Database.Migrate();
        }
    }
}

