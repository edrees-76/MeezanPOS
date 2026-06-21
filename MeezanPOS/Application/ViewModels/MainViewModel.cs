using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public MainViewModel()
    {
        CurrentViewModel = new DashboardViewModel();
    }

    private int selectedNavIndex = 0;
    public int SelectedNavIndex
    {
        get => selectedNavIndex;
        set
        {
            if (SetProperty(ref selectedNavIndex, value))
            {
                if (value == 0) Navigate("Dashboard");
                else if (value == 1) Navigate("Sales");
                else if (value == 2) Navigate("Suppliers");
                else if (value == 3) Navigate("Expenses");
                else if (value == 4) Navigate("Wages");
                else if (value == 5) Navigate("Banking");
                else if (value == 6) Navigate("FreeOrdersReturns");
            }
        }
    }


    [RelayCommand]
    private void Navigate(string viewName)
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
                SelectedNavIndex = 1;
                CurrentViewModel = new SalesViewModel();
                break;
            case "Suppliers":
                Title = "ميزان للمالية - إدارة الموردين";
                SelectedNavIndex = 2;
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
                SelectedNavIndex = 3;
                var expenseVM = new ExpenseManagementViewModel();
                expenseVM.OnBack = () => Navigate("Dashboard");
                expenseVM.OnEditJournal = (journalId) => 
                {
                    try
                    {
                        using var db = new MeezanPOS.Infrastructure.Data.AppDbContext();
                        var journal = db.DailyJournals.FirstOrDefault(j => j.Id == journalId);
                        if (journal != null && (journal.FinancialStatus == MeezanPOS.Domain.Enums.FinancialStatus.Posted || journal.FinancialStatus == MeezanPOS.Domain.Enums.FinancialStatus.Archived))
                        {
                            Title = "ميزان للمالية - عرض حركة يومية مرحّلة";
                        }
                        else
                        {
                            Title = "ميزان للمالية - تعديل حركة يومية";
                        }
                    }
                    catch
                    {
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
                SelectedNavIndex = 4;
                CurrentViewModel = new WagesManagementViewModel();
                break;
            case "Banking":
                Title = "ميزان للمالية - الخدمات المصرفية والبنكية";
                SelectedNavIndex = 5;
                CurrentViewModel = new BankingServicesViewModel();
                break;
            case "FreeOrdersReturns":
                Title = "ميزان للمالية - الطلبات المجانية والمرتجعات";
                SelectedNavIndex = 6;
                CurrentViewModel = new FreeOrdersReturnsViewModel();
                break;
            case "AddJournal":
                Title = "ميزان للمالية - تسجيل حركة يومية";
                var journalVM2 = new DailyJournalViewModel();
                journalVM2.OnClose = () => Navigate("Dashboard");
                CurrentViewModel = journalVM2;
                break;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        var result = MessageBox.Show(
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

        var currentWindow = System.Windows.Application.Current.MainWindow;
        var loginView = new Presentation.Views.LoginView();
        System.Windows.Application.Current.MainWindow = loginView;
        loginView.Show();
        
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window != loginView)
            {
                window.Close();
            }
        }
    }
}
