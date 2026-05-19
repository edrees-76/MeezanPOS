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
    private readonly AppDbContext _context;

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
        _context = new AppDbContext();
        _ = LoadSuppliersAsync();
    }

    [RelayCommand]
    public async Task LoadSuppliersAsync()
    {
        IsLoading = true;
        try
        {
            var query = _context.Suppliers.AsQueryable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(s => s.Name.Contains(SearchText) || (s.Phone != null && s.Phone.Contains(SearchText)));
            }

            var list = await query.OrderBy(s => s.Name).ToListAsync();
            
            int seq = 1;
            foreach (var s in list)
            {
                s.Sequence = seq++;
            }
            
            Suppliers = new ObservableCollection<Supplier>(list);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"خطأ تحميل الموردين: {ex.Message}");
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
            if (FormSupplier.Id == 0)
            {
                // إضافة جديد
                FormSupplier.CurrentBalance = FormSupplier.OpeningBalance; // الرصيد الافتتاحي هو الرصيد الحالي مبدئياً
                _context.Suppliers.Add(FormSupplier);
            }
            else
            {
                // تعديل
                var existing = await _context.Suppliers.FindAsync(FormSupplier.Id);
                if (existing != null)
                {
                    existing.Name = FormSupplier.Name;
                    existing.Phone = FormSupplier.Phone;
                    existing.CreditLimit = FormSupplier.CreditLimit;
                    existing.IsActive = FormSupplier.IsActive;
                    // لا نُعدل CurrentBalance أو OpeningBalance هنا منعاً للتلاعب
                    _context.Suppliers.Update(existing);
                }
            }

            await _context.SaveChangesAsync();
            IsFormOpen = false;
            await LoadSuppliersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"خطأ حفظ بيانات المورد: {ex.Message}");
        }
    }
}
