using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using System.Windows;

namespace MeezanPOS.Application.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "ميزان للمالية - لوحة التحكم";

    [ObservableProperty]
    private ObservableObject? currentViewModel;

    [ObservableProperty]
    private bool isSidebarVisible = true;

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarVisible = !IsSidebarVisible;
    }

    private readonly MeezanPOS.Application.Interfaces.ISessionService? _session;

    public MainViewModel()
    {
        try
        {
            _session = MeezanPOS.Application.Services.AppServiceProvider
                .Resolve<MeezanPOS.Application.Interfaces.ISessionService>();
            var user = _session.CurrentUser;
            if (user != null)
            {
                CurrentUserName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
                CurrentUserRole = user.Role != null
                    ? MeezanPOS.Application.Services.UserManagementService.RoleDisplayName(user.Role.Type)
                    : string.Empty;
            }
        }
        catch (InvalidOperationException)
        {
            // الخدمات غير مهيأة (وضع التصميم) — تبقى بيانات المستخدم فارغة
        }

        NavVisible = NavOrder.Select(IsAllowed).ToArray();
        CurrentViewModel = new DashboardViewModel();
    }

    // الصلاحية المطلوبة لكل شاشة رئيسية في القائمة الجانبية
    private static readonly System.Collections.Generic.Dictionary<string, string> NavPermissions = new()
    {
        ["Dashboard"] = MeezanPOS.Application.Services.Permissions.ViewDashboard,
        ["AddJournal"] = MeezanPOS.Application.Services.Permissions.CreateJournal,
        ["Sales"] = MeezanPOS.Application.Services.Permissions.ViewSales,
        ["Suppliers"] = MeezanPOS.Application.Services.Permissions.ManageSuppliers,
        ["Expenses"] = MeezanPOS.Application.Services.Permissions.ManageExpenses,
        ["Wages"] = MeezanPOS.Application.Services.Permissions.ManageWages,
        ["Banking"] = MeezanPOS.Application.Services.Permissions.ManageBanking,
        ["FreeOrdersReturns"] = MeezanPOS.Application.Services.Permissions.ManageReturns,
        ["ClosingAccount"] = MeezanPOS.Application.Services.Permissions.ClosingAccount,
        ["Settings"] = MeezanPOS.Application.Services.Permissions.ViewSettings,
        ["Users"] = MeezanPOS.Application.Services.Permissions.ManageUsers,
    };

    private bool IsAllowed(string viewName)
        => _session == null
           || !NavPermissions.TryGetValue(viewName, out var permission)
           || _session.HasPermission(permission);

    /// <summary>ظهور عناصر القائمة الجانبية بنفس ترتيب NavOrder حسب صلاحيات المستخدم.</summary>
    public bool[] NavVisible { get; }

    [RelayCommand]
    private void ChangePassword()
    {
        AppWindows.Current.ChangePassword(forced: false);
    }

    // ترتيب عناصر القائمة الجانبية (يطابق MainView.xaml واختصارات Ctrl+1..Ctrl+0)
    private static readonly string[] NavOrder =
    {
        "Dashboard", "AddJournal", "Sales", "Suppliers", "Expenses",
        "Wages", "Banking", "FreeOrdersReturns", "ClosingAccount", "Settings", "Users"
    };

    private int selectedNavIndex = 0;
    private bool _isNavigating;
    public int SelectedNavIndex
    {
        get => selectedNavIndex;
        set
        {
            if (SetProperty(ref selectedNavIndex, value) && !_isNavigating && value >= 0 && value < NavOrder.Length)
                Navigate(NavOrder[value]);
        }
    }

    /// <summary>عنوان الصفحة المعروض في الشريط العلوي (بدون بادئة اسم المنظومة)</summary>
    public string PageTitle => Title.Replace("ميزان للمالية - ", string.Empty);

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>اسم المطعم المفتوح (فارغ إن لم يُختر مطعم، كما في الاختبارات).</summary>
    public string RestaurantName => MeezanPOS.Infrastructure.Data.RestaurantContext.DisplayName;
    public bool HasRestaurantName => !string.IsNullOrEmpty(RestaurantName);

    /// <summary>عنوان النافذة وشريط المهام: الصفحة ثم اسم المطعم حتى لا يُخلط بين المطاعم.</summary>
    public string WindowTitle => HasRestaurantName ? $"{Title} | {RestaurantName}" : Title;

    public string CurrentUserName { get; } = string.Empty;
    public string CurrentUserRole { get; } = string.Empty;
    public string CurrentUserInitial => string.IsNullOrWhiteSpace(CurrentUserName) ? "؟" : CurrentUserName.Trim()[0].ToString();
    public string TodayText { get; } = DateTime.Now.ToString("dd/MM/yyyy");

    [RelayCommand]
    private void Navigate(string viewName)
    {
        if (!IsAllowed(viewName))
        {
            Dialogs.Show(MeezanPOS.Application.Services.Permissions.DeniedMessage, "صلاحية غير كافية",
                MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK,
                MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            return;
        }

        // تنبيه قبل مغادرة يومية فيها بيانات غير محفوظة
        if (CurrentViewModel is DailyJournalViewModel journalVm && journalVm.HasUnsavedChanges)
        {
            var answer = Dialogs.Show(
                "توجد بيانات في اليومية الحالية لم تُحفظ بعد وستضيع عند المغادرة.\n\nهل تريد المغادرة دون حفظ؟",
                "بيانات غير محفوظة", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No,
                MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            if (answer != MessageBoxResult.Yes)
            {
                // إعادة تحديد عنصر القائمة الحالي
                OnPropertyChanged(nameof(SelectedNavIndex));
                return;
            }
        }

        // يمنع إعادة الدخول: تغيير SelectedNavIndex داخل التنقل لا يعيد إنشاء الشاشة
        _isNavigating = true;
        try
        {
            NavigateCore(viewName);
        }
        finally
        {
            _isNavigating = false;
        }
    }

    private void NavigateCore(string viewName)
    {
        switch (viewName)
        {
            case "Dashboard":
                Title = "ميزان للمالية - لوحة التحكم";
                SelectedNavIndex = 0;
                CurrentViewModel = new DashboardViewModel();
                break;
            case "Sales":
                Title = "ميزان للمالية - المبيعات والإيرادات";
                SelectedNavIndex = 2;
                CurrentViewModel = new SalesViewModel();
                break;
            case "Suppliers":
                Title = "ميزان للمالية - إدارة الموردين";
                SelectedNavIndex = 3;
                var supplierVM = new SupplierListViewModel();
                supplierVM.OnViewSupplierDetails = (supplier) =>
                {
                    Title = $"مورد: {supplier.Name} - الفواتير وكشف الحساب";
                    var detailsVM = new SupplierDetailsViewModel(supplier.Id, supplier.Name);
                    detailsVM.OnBack = () => Navigate("Suppliers");
                    CurrentViewModel = detailsVM;
                };
                CurrentViewModel = supplierVM;
                break;
            case "Expenses":
                Title = "ميزان للمالية - إدارة المصروفات";
                SelectedNavIndex = 4;
                var expenseVM = new ExpenseManagementViewModel();
                expenseVM.OnBack = () => Navigate("Dashboard");
                expenseVM.OnEditJournal = async (journalId) =>
                {
                    try
                    {
                        var status = await MeezanPOS.Application.Services.Queries.LookupService.Default.GetJournalStatusAsync(journalId);
                        if (status == MeezanPOS.Domain.Enums.FinancialStatus.Posted || status == MeezanPOS.Domain.Enums.FinancialStatus.Archived)
                        {
                            Title = "ميزان للمالية - عرض حركة يومية مرحّلة";
                        }
                        else
                        {
                            Title = "ميزان للمالية - تعديل حركة يومية";
                        }
                    }
                    catch (Exception ex)
                    {
                        // العنوان فقط: نكمل فتح اليومية بالعنوان الافتراضي
                        Serilog.Log.Warning(ex, "تعذر قراءة حالة اليومية {JournalId} لعنوان الشاشة", journalId);
                        Title = "ميزان للمالية - تعديل حركة يومية";
                    }
                    var journalVM = new DailyJournalViewModel(journalId);
                    journalVM.OnClose = () => Navigate("Expenses");
                    CurrentViewModel = journalVM;
                };
                CurrentViewModel = expenseVM;
                break;
            case "Wages":
                Title = "ميزان للمالية - أجور ومستحقات العمال";
                SelectedNavIndex = 5;
                CurrentViewModel = new WagesManagementViewModel();
                break;
            case "Banking":
                Title = "ميزان للمالية - الخدمات المصرفية والبنكية";
                SelectedNavIndex = 6;
                CurrentViewModel = new BankingServicesViewModel();
                break;
            case "FreeOrdersReturns":
                Title = "ميزان للمالية - الطلبات المجانية والمرتجعات";
                SelectedNavIndex = 7;
                CurrentViewModel = new FreeOrdersReturnsViewModel();
                break;
            case "ClosingAccount":
                Title = "ميزان للمالية - الحساب الختامي والتقارير";
                SelectedNavIndex = 8;
                CurrentViewModel = new ClosingAccountViewModel();
                break;
            case "Settings":
                Title = "ميزان للمالية - الإعدادات النظامية";
                SelectedNavIndex = 9;
                CurrentViewModel = new SettingsViewModel();
                break;
            case "Users":
                Title = "ميزان للمالية - المستخدمون والنشاط";
                SelectedNavIndex = 10;
                CurrentViewModel = new UsersActivityViewModel();
                break;
            case "AddJournal":
                Title = "ميزان للمالية - تسجيل حركة يومية";
                SelectedNavIndex = 1;
                var journalVM2 = new DailyJournalViewModel();
                journalVM2.OnClose = () => Navigate("Dashboard");
                CurrentViewModel = journalVM2;
                break;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        var result = Dialogs.Show(
            "هل أنت متأكد من تسجيل الخروج؟\nيرجى التأكد من حفظ كافة البيانات قبل الخروج.",
            "تأكيد تسجيل الخروج",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No,
            MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);

        if (result == MessageBoxResult.No)
        {
            return;
        }

        // تسجيل الخروج في سجل النشاط (لا يمنع الخروج إن فشل)
        if (_session?.CurrentUser is { } user)
        {
            try
            {
                using var authLease = MeezanPOS.Application.Services.AppServiceProvider.Lease<MeezanPOS.Application.Interfaces.IAuthenticationService>();
                authLease.Service.RecordSessionEventAsync(user.Id, MeezanPOS.Application.Services.ActivityLabels.Logout)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "تعذر تسجيل الخروج في سجل النشاط");
            }
        }

        _session?.ClearSession();

        AppWindows.Current.ShowLogin();
    }
}
