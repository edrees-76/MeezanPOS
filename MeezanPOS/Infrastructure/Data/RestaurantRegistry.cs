using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MeezanPOS.Infrastructure.Data;

/// <summary>مطعم مسجّل على هذا الجهاز. Folder فارغ = المجلد الرئيسي (بيانات المطعم الأول كما كانت).</summary>
public sealed class RestaurantEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastOpenedAt { get; set; }

    public bool IsPrimary => string.IsNullOrEmpty(Folder);
}

/// <summary>
/// المطعم المفتوح حالياً. كل قاعدة بيانات وسجلاتها ونسخها الاحتياطية في مجلده.
/// IsOverride: مجلد محدد بـ MEEZANPOS_DATA_DIR (البيانات التجريبية) خارج سجل المطاعم.
/// </summary>
public sealed record ActiveRestaurant(string Id, string Code, string Name, string DataDirectory, bool IsPrimary, bool IsOverride = false);

/// <summary>المطعم المفتوح في هذا التشغيل (null في الاختبارات وقبل الاختيار).</summary>
public static class RestaurantContext
{
    /// <summary>مفتاح الإعداد الذي يربط قاعدة البيانات بمطعمها (يُستخدم لرفض استعادة نسخة مطعم آخر).</summary>
    public const string SettingKey = "RestaurantId";

    public static ActiveRestaurant? Current { get; private set; }

    public static string DisplayName => Current?.Name ?? string.Empty;

    /// <summary>بادئة أسماء ملفات النسخ الاحتياطية حتى لا تختلط نسخ المطاعم في مجلد واحد.</summary>
    public static string FileTag => Current == null ? string.Empty : Current.Code + "_";

    /// <summary>نص تذييل التقارير: اسم المنظومة واسم المطعم.</summary>
    public static string ReportFooter => Current == null ? "منظومة ميزان" : $"منظومة ميزان — {Current.Name}";

    public static void Set(ActiveRestaurant restaurant) => Current = restaurant;

    public static void Clear() => Current = null;
}

/// <summary>
/// سجل المطاعم على الجهاز (restaurants.json في المجلد الرئيسي).
/// إضافة مطعم أو إعادة تسميته تتطلب كلمة مرور المالك، وهي منفصلة عن مستخدمي أي مطعم.
/// </summary>
public sealed class RestaurantRegistry
{
    public const string FileName = "restaurants.json";
    public const string PrimaryDefaultName = "المطعم 1";
    public const int MinOwnerPasswordLength = 6;

    private sealed class Data
    {
        public int Version { get; set; } = 1;
        public string? OwnerPasswordHash { get; set; }
        public string? LastUsedId { get; set; }
        public List<RestaurantEntry> Restaurants { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Data _data;

    public string RootDirectory { get; }
    public string FilePath => Path.Combine(RootDirectory, FileName);

    private RestaurantRegistry(string root, Data data)
    {
        RootDirectory = root;
        _data = data;
    }

    /// <summary>المجلد الرئيسي. MEEZANPOS_ROOT_DIR يوجّه سجل المطاعم كله إلى مجلد تجربة دون لمس البيانات الحقيقية.</summary>
    public static string DefaultRoot
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable("MEEZANPOS_ROOT_DIR");
            return string.IsNullOrWhiteSpace(overrideRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeezanPOS")
                : overrideRoot;
        }
    }

    public IReadOnlyList<RestaurantEntry> Restaurants => _data.Restaurants;
    public bool HasOwnerPassword => !string.IsNullOrEmpty(_data.OwnerPasswordHash);
    public string? LastUsedId => _data.LastUsedId;

    /// <summary>يقرأ السجل، أو ينشئه أول مرة بمطعم واحد هو البيانات الموجودة في المجلد الرئيسي.</summary>
    public static RestaurantRegistry Load(string? rootDirectory = null)
    {
        var root = rootDirectory ?? DefaultRoot;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, FileName);

        Data? data = null;
        if (File.Exists(path))
        {
            try
            {
                data = Read(path);
            }
            catch (Exception) when (File.Exists(path + ".bak"))
            {
                // الملف تالف (انقطاع أثناء الكتابة مثلاً): النسخة السابقة المحفوظة عند آخر حفظ
                data = Read(path + ".bak");
            }
        }

        var registry = new RestaurantRegistry(root, data ?? new Data());
        if (registry._data.Restaurants.Count == 0)
        {
            registry._data.Restaurants.Add(new RestaurantEntry { Code = "R1", Name = PrimaryDefaultName, Folder = string.Empty });
            registry.Save();
        }
        return registry;
    }

    private static Data Read(string file) =>
        JsonSerializer.Deserialize<Data>(File.ReadAllText(file), JsonOptions)
        ?? throw new InvalidDataException("ملف سجل المطاعم تالف.");

    public RestaurantEntry? Find(string id) => _data.Restaurants.FirstOrDefault(r => r.Id == id);

    public string DataDirectoryOf(RestaurantEntry entry) =>
        entry.IsPrimary ? RootDirectory : Path.Combine(RootDirectory, entry.Folder);

    public ActiveRestaurant ToActive(RestaurantEntry entry) =>
        new(entry.Id, entry.Code, entry.Name, DataDirectoryOf(entry), entry.IsPrimary);

    public bool VerifyOwnerPassword(string? password) =>
        HasOwnerPassword && !string.IsNullOrEmpty(password) && BCrypt.Net.BCrypt.Verify(password, _data.OwnerPasswordHash);

    /// <summary>أول مرة: بلا كلمة حالية. بعد ذلك يلزم إدخال الكلمة الحالية.</summary>
    public void SetOwnerPassword(string newPassword, string? currentPassword)
    {
        if (HasOwnerPassword && !VerifyOwnerPassword(currentPassword))
            throw new UnauthorizedAccessException("كلمة مرور المالك الحالية غير صحيحة.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinOwnerPasswordLength)
            throw new ArgumentException($"كلمة مرور المالك يجب ألا تقل عن {MinOwnerPasswordLength} أحرف.");

        _data.OwnerPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, 12);
        Save();
    }

    public RestaurantEntry Add(string name, string ownerPassword)
    {
        RequireOwner(ownerPassword);
        var clean = ValidateName(name, exceptId: null);

        int n = 1;
        while (_data.Restaurants.Any(r => r.Code.Equals($"R{n}", StringComparison.OrdinalIgnoreCase))
               || Directory.Exists(Path.Combine(RootDirectory, "Restaurants", $"R{n}")))
            n++;

        var entry = new RestaurantEntry { Code = $"R{n}", Name = clean, Folder = Path.Combine("Restaurants", $"R{n}") };
        Directory.CreateDirectory(DataDirectoryOf(entry));
        _data.Restaurants.Add(entry);
        Save();
        return entry;
    }

    public void Rename(string id, string newName, string ownerPassword)
    {
        RequireOwner(ownerPassword);
        var entry = Find(id) ?? throw new InvalidOperationException("المطعم غير موجود.");
        entry.Name = ValidateName(newName, exceptId: id);
        Save();
    }

    public void MarkOpened(string id)
    {
        var entry = Find(id) ?? throw new InvalidOperationException("المطعم غير موجود.");
        entry.LastOpenedAt = DateTime.Now;
        _data.LastUsedId = id;
        Save();
    }

    private void RequireOwner(string password)
    {
        if (!HasOwnerPassword)
            throw new InvalidOperationException("يجب تعيين كلمة مرور المالك أولاً.");
        if (!VerifyOwnerPassword(password))
            throw new UnauthorizedAccessException("كلمة مرور المالك غير صحيحة.");
    }

    private string ValidateName(string name, string? exceptId)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0)
            throw new ArgumentException("اكتب اسم المطعم.");
        if (clean.Length > 60)
            throw new ArgumentException("اسم المطعم طويل جداً (60 حرفاً كحد أقصى).");
        if (_data.Restaurants.Any(r => r.Id != exceptId && string.Equals(r.Name.Trim(), clean, StringComparison.CurrentCultureIgnoreCase)))
            throw new ArgumentException("يوجد مطعم بهذا الاسم.");
        return clean;
    }

    /// <summary>كتابة ذرية: ملف مؤقت ثم استبدال، فلا يتلف السجل إن انقطع التيار أثناء الحفظ.</summary>
    private void Save()
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, JsonOptions));
        if (File.Exists(FilePath))
            File.Replace(temp, FilePath, FilePath + ".bak");
        else
            File.Move(temp, FilePath);
    }
}
