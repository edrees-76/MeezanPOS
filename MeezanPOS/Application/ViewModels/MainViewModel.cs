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
            case "AddJournal":
                Title = "ميزان للمالية - تسجيل حركة يومية";
                var journalVM = new DailyJournalViewModel();
                journalVM.OnClose = () => Navigate("Dashboard");
                CurrentViewModel = journalVM;
                break;
            case "Sales":
                Title = "ميزان للمالية - الإيرادات";
                CurrentViewModel = new SalesViewModel();
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
