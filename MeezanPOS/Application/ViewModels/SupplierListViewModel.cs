using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeezanPOS.Application.ViewModels;

public partial class SupplierListViewModel : ObservableObject
{
    private readonly MeezanPOS.Application.Services.Queries.ISupplierQueryService _suppliers;

    [ObservableProperty]
    private ObservableCollection<Supplier> suppliers = new();

    [ObservableProperty]
    private Supplier? selectedSupplier;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string searchText = string.Empty;

    // --- حقول الإضافة والتعديل ---
    [ObservableProperty]
    private bool isFormOpen;

    [ObservableProperty]
    private string formTitle = "إضافة مورد جديد";

    [ObservableProperty]
    private Supplier formSupplier = new();

    public Action<Supplier>? OnViewSupplierDetails { get; set; }

    public SupplierListViewModel()
    {
        _suppliers = new MeezanPOS.Application.Services.Queries.SupplierQueryService();
        _ = LoadSuppliersAsync();
    }

    [RelayCommand]
    public async Task LoadSuppliersAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _suppliers.SearchAsync(SearchText);

            int seq = 1;
            foreach (var s in list)
            {
                s.Sequence = seq++;
            }

            Suppliers = new ObservableCollection<Supplier>(list);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "خطأ تحميل الموردين");
            Dialogs.Show($"تعذر تحميل قائمة الموردين:\n{ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenAddForm()
    {
        FormSupplier = new Supplier();
        FormTitle = "إضافة مورد جديد";
        IsFormOpen = true;
    }

    [RelayCommand]
    public void OpenEditForm(Supplier supplier)
    {
        if (supplier == null) return;

        FormSupplier = new Supplier
        {
            Id = supplier.Id,
            Name = supplier.Name,
            Phone = supplier.Phone,
            OpeningBalance = supplier.OpeningBalance,
            CreditLimit = supplier.CreditLimit,
            IsActive = supplier.IsActive,
            CurrentBalance = supplier.CurrentBalance
        };
        FormTitle = "تعديل بيانات المورد";
        IsFormOpen = true;
    }

    [RelayCommand]
    public void ViewSupplierDetails(Supplier supplier)
    {
        if (supplier == null) return;
        OnViewSupplierDetails?.Invoke(supplier);
    }

    [RelayCommand]
    public void CloseForm()
    {
        IsFormOpen = false;
    }

    [RelayCommand]
    public async Task SaveSupplierAsync()
    {
        if (string.IsNullOrWhiteSpace(FormSupplier.Name))
            return; // تحقق بسيط

        try
        {
            await _suppliers.SaveSupplierAsync(FormSupplier);
            IsFormOpen = false;
            await LoadSuppliersAsync();
        }
        catch (Exception ex)
        {
            // كان الخطأ يُكتب في نافذة التصحيح فقط فيظن المستخدم أن الحفظ تم
            Serilog.Log.Error(ex, "خطأ حفظ بيانات المورد");
            Dialogs.Show($"تعذر حفظ بيانات المورد:\n{ex.Message}", "خطأ", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
