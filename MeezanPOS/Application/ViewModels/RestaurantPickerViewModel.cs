using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeezanPOS.Infrastructure.Data;

namespace MeezanPOS.Application.ViewModels;

public enum RestaurantPickerMode { List, Add, Rename, OwnerPassword }

/// <summary>بطاقة مطعم في شاشة الاختيار.</summary>
public sealed record RestaurantCard(string Id, string Code, string Name, bool IsPrimary, bool IsLastUsed, DateTime? LastOpenedAt)
{
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "؟" : Name.Trim()[..1];
    public string LastOpenedText => LastOpenedAt is { } d ? $"آخر فتح: {d:yyyy/MM/dd HH:mm}" : "لم يُفتح بعد";
}

/// <summary>
/// شاشة اختيار المطعم قبل تسجيل الدخول. فتح أي مطعم متاح للجميع (الدخول بعده بمستخدمي ذلك المطعم)،
/// أما إضافة مطعم أو إعادة تسميته فتتطلب كلمة مرور المالك.
/// </summary>
public partial class RestaurantPickerViewModel : ObservableObject
{
    private readonly RestaurantRegistry _registry;
    private RestaurantPickerMode _afterPassword = RestaurantPickerMode.List;
    private string? _renameId;

    public RestaurantPickerViewModel(RestaurantRegistry registry)
    {
        _registry = registry;
        Reload(registry.LastUsedId);
    }

    public ObservableCollection<RestaurantCard> Restaurants { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private RestaurantCard? selectedRestaurant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsAddMode), nameof(IsRenameMode), nameof(IsPasswordMode),
        nameof(EditTitle), nameof(EditHint), nameof(ConfirmText), nameof(ShowNameField), nameof(ShowOwnerPasswordField),
        nameof(ShowNewPasswordFields))]
    private RestaurantPickerMode mode;

    [ObservableProperty] private string editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string error = string.Empty;

    /// <summary>كلمات المرور تُمرَّر من PasswordBox في الواجهة (لا تُربط مباشرة).</summary>
    public string OwnerPassword { get; set; } = string.Empty;
    public string NewOwnerPassword { get; set; } = string.Empty;
    public string ConfirmOwnerPassword { get; set; } = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool HasOwnerPassword => _registry.HasOwnerPassword;
    public int RestaurantCount => Restaurants.Count;

    public bool IsEditing => Mode != RestaurantPickerMode.List;
    public bool IsAddMode => Mode == RestaurantPickerMode.Add;
    public bool IsRenameMode => Mode == RestaurantPickerMode.Rename;
    public bool IsPasswordMode => Mode == RestaurantPickerMode.OwnerPassword;
    public bool ShowNameField => IsAddMode || IsRenameMode;
    /// <summary>كلمة المرور الحالية: للإضافة والتسمية، ولتغيير كلمة موجودة.</summary>
    public bool ShowOwnerPasswordField => !IsPasswordMode || HasOwnerPassword;
    public bool ShowNewPasswordFields => IsPasswordMode;

    public string EditTitle => Mode switch
    {
        RestaurantPickerMode.Add => "إضافة مطعم جديد",
        RestaurantPickerMode.Rename => "إعادة تسمية المطعم",
        RestaurantPickerMode.OwnerPassword => HasOwnerPassword ? "تغيير كلمة مرور المالك" : "تعيين كلمة مرور المالك",
        _ => string.Empty,
    };

    public string EditHint => Mode switch
    {
        RestaurantPickerMode.Add => "يُنشأ للمطعم ملف بيانات مستقل تماماً: حساباته ومستخدموه ونسخه الاحتياطية منفصلة عن بقية المطاعم. يبدأ بمستخدم admin وكلمة المرور admin ويُطلب تغييرها عند أول دخول.",
        RestaurantPickerMode.Rename => "يتغير الاسم المعروض فقط؛ بيانات المطعم تبقى كما هي.",
        RestaurantPickerMode.OwnerPassword when !HasOwnerPassword =>
            $"كلمة مرور المالك منفصلة عن مستخدمي المطاعم، وتلزم لإضافة مطعم أو تغيير اسمه. احفظها جيداً ({RestaurantRegistry.MinOwnerPasswordLength} أحرف على الأقل).",
        RestaurantPickerMode.OwnerPassword => "أدخل الكلمة الحالية ثم الجديدة.",
        _ => string.Empty,
    };

    public string ConfirmText => Mode switch
    {
        RestaurantPickerMode.Add => "إضافة المطعم",
        RestaurantPickerMode.Rename => "حفظ الاسم",
        _ => "حفظ كلمة المرور",
    };

    /// <summary>المطعم المختار عند الإغلاق بـ "فتح".</summary>
    public RestaurantEntry? Result { get; private set; }

    /// <summary>true = فُتح مطعم، false = خروج.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>تطلب من الواجهة تفريغ حقول كلمات المرور.</summary>
    public event Action? PasswordsCleared;

    private void Reload(string? selectId)
    {
        Restaurants.Clear();
        foreach (var r in _registry.Restaurants.OrderBy(r => r.CreatedAt))
            Restaurants.Add(new RestaurantCard(r.Id, r.Code, r.Name, r.IsPrimary, r.Id == _registry.LastUsedId, r.LastOpenedAt));
        SelectedRestaurant = Restaurants.FirstOrDefault(r => r.Id == selectId) ?? Restaurants.FirstOrDefault();
        OnPropertyChanged(nameof(RestaurantCount));
    }

    private bool CanOpen() => SelectedRestaurant != null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        if (SelectedRestaurant == null) return;
        Result = _registry.Find(SelectedRestaurant.Id);
        if (Result != null)
            CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void OpenRestaurant(RestaurantCard? card)
    {
        if (card == null) return;
        SelectedRestaurant = card;
        Open();
    }

    [RelayCommand]
    private void StartAdd()
    {
        EditName = string.Empty;
        if (HasOwnerPassword)
        {
            BeginEdit(RestaurantPickerMode.Add);
        }
        else
        {
            // أول إضافة: تُعيَّن كلمة مرور المالك أولاً ثم يُكمل إلى الإضافة
            _afterPassword = RestaurantPickerMode.Add;
            BeginEdit(RestaurantPickerMode.OwnerPassword);
        }
    }

    [RelayCommand]
    private void StartRename(RestaurantCard? card)
    {
        if (card == null) return;
        if (!HasOwnerPassword)
        {
            _afterPassword = RestaurantPickerMode.List;
            BeginEdit(RestaurantPickerMode.OwnerPassword);
            Error = "عيّن كلمة مرور المالك أولاً، ثم أعد تسمية المطعم.";
            return;
        }
        _renameId = card.Id;
        EditName = card.Name;
        BeginEdit(RestaurantPickerMode.Rename);
    }

    [RelayCommand]
    private void StartOwnerPassword()
    {
        _afterPassword = RestaurantPickerMode.List;
        BeginEdit(RestaurantPickerMode.OwnerPassword);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        _afterPassword = RestaurantPickerMode.List;
        BeginEdit(RestaurantPickerMode.List);
    }

    [RelayCommand]
    private void Confirm()
    {
        Error = string.Empty;
        try
        {
            switch (Mode)
            {
                case RestaurantPickerMode.Add:
                    var added = _registry.Add(EditName, OwnerPassword);
                    Reload(added.Id);
                    BeginEdit(RestaurantPickerMode.List);
                    break;

                case RestaurantPickerMode.Rename when _renameId != null:
                    _registry.Rename(_renameId, EditName, OwnerPassword);
                    Reload(_renameId);
                    BeginEdit(RestaurantPickerMode.List);
                    break;

                case RestaurantPickerMode.OwnerPassword:
                    if (NewOwnerPassword != ConfirmOwnerPassword)
                    {
                        Error = "كلمتا المرور الجديدتان غير متطابقتين.";
                        return;
                    }
                    _registry.SetOwnerPassword(NewOwnerPassword, HasOwnerPassword ? OwnerPassword : null);
                    OnPropertyChanged(nameof(HasOwnerPassword));
                    var next = _afterPassword;
                    _afterPassword = RestaurantPickerMode.List;
                    BeginEdit(next);
                    break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void Exit() => CloseRequested?.Invoke(false);

    private void BeginEdit(RestaurantPickerMode newMode)
    {
        Error = string.Empty;
        OwnerPassword = NewOwnerPassword = ConfirmOwnerPassword = string.Empty;
        PasswordsCleared?.Invoke();
        Mode = newMode;
        // الخصائص المحسوبة التي تعتمد على وجود كلمة المرور
        OnPropertyChanged(nameof(ShowOwnerPasswordField));
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditHint));
    }
}
