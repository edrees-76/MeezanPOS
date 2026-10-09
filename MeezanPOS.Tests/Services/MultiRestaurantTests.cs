using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using MeezanPOS.Application.ViewModels;
using MeezanPOS.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MeezanPOS.Tests.Services;

/// <summary>
/// تعدد المطاعم على جهاز واحد: سجل المطاعم، كلمة مرور المالك، شاشة الاختيار، ورفض استعادة نسخة مطعم آخر.
/// كل اختبار في مجلد مؤقت خاص به، فلا يلمس سجل المطاعم الحقيقي.
/// </summary>
public class MultiRestaurantTests : IDisposable
{
    private const string OwnerPassword = "Owner#2026";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MeezanRestaurants_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void FirstLoad_RegistersExistingDataAsTheFirstRestaurant_InTheRootFolder()
    {
        var registry = RestaurantRegistry.Load(_root);

        var only = registry.Restaurants.Should().ContainSingle().Subject;
        only.Name.Should().Be(RestaurantRegistry.PrimaryDefaultName);
        only.IsPrimary.Should().BeTrue();
        registry.DataDirectoryOf(only).Should().Be(_root, "existing data stays where it is");
        File.Exists(Path.Combine(_root, RestaurantRegistry.FileName)).Should().BeTrue();
        RestaurantRegistry.Load(_root).Restaurants.Single().Id.Should().Be(only.Id, "the id is persisted");
    }

    [Fact]
    public void AddingAndRenaming_RequireTheOwnerPassword()
    {
        var registry = RestaurantRegistry.Load(_root);

        FluentActions.Invoking(() => registry.Add("مطعم الشاطئ", OwnerPassword))
            .Should().Throw<InvalidOperationException>("no owner password has been set yet");

        registry.SetOwnerPassword(OwnerPassword, currentPassword: null);
        FluentActions.Invoking(() => registry.Add("مطعم الشاطئ", "wrong"))
            .Should().Throw<UnauthorizedAccessException>();

        var added = registry.Add("مطعم الشاطئ", OwnerPassword);
        added.Code.Should().Be("R2");
        Directory.Exists(registry.DataDirectoryOf(added)).Should().BeTrue();
        registry.DataDirectoryOf(added).Should().NotBe(_root);

        FluentActions.Invoking(() => registry.Add("  مطعم الشاطئ ", OwnerPassword))
            .Should().Throw<ArgumentException>("names must be unique");

        var primary = registry.Restaurants.Single(r => r.IsPrimary);
        FluentActions.Invoking(() => registry.Rename(primary.Id, "الفرع الرئيسي", "wrong"))
            .Should().Throw<UnauthorizedAccessException>();
        registry.Rename(primary.Id, "الفرع الرئيسي", OwnerPassword);

        var reloaded = RestaurantRegistry.Load(_root);
        reloaded.Restaurants.Select(r => r.Name).Should().BeEquivalentTo(new[] { "الفرع الرئيسي", "مطعم الشاطئ" });
        reloaded.VerifyOwnerPassword(OwnerPassword).Should().BeTrue();
    }

    [Fact]
    public void ChangingTheOwnerPassword_RequiresTheCurrentOne()
    {
        var registry = RestaurantRegistry.Load(_root);
        registry.SetOwnerPassword(OwnerPassword, null);

        FluentActions.Invoking(() => registry.SetOwnerPassword("NewOwner#1", "wrong"))
            .Should().Throw<UnauthorizedAccessException>();
        FluentActions.Invoking(() => registry.SetOwnerPassword("123", OwnerPassword))
            .Should().Throw<ArgumentException>("too short");

        registry.SetOwnerPassword("NewOwner#1", OwnerPassword);
        registry.VerifyOwnerPassword("NewOwner#1").Should().BeTrue();
        registry.VerifyOwnerPassword(OwnerPassword).Should().BeFalse();
    }

    [Fact]
    public void CorruptRegistry_FallsBackToThePreviousSave()
    {
        var registry = RestaurantRegistry.Load(_root);
        registry.SetOwnerPassword(OwnerPassword, null); // ثاني حفظ: ينشئ نسخة .bak

        File.WriteAllText(registry.FilePath, "{ broken");

        RestaurantRegistry.Load(_root).Restaurants.Should().ContainSingle(r => r.IsPrimary);
    }

    [Fact]
    public void Picker_FirstAddSetsTheOwnerPassword_ThenAddsAndOpensTheRestaurant()
    {
        var vm = new RestaurantPickerViewModel(RestaurantRegistry.Load(_root));

        vm.StartAddCommand.Execute(null);
        vm.Mode.Should().Be(RestaurantPickerMode.OwnerPassword);
        vm.ShowOwnerPasswordField.Should().BeFalse("there is no current owner password yet");

        vm.NewOwnerPassword = OwnerPassword;
        vm.ConfirmOwnerPassword = "different";
        vm.ConfirmCommand.Execute(null);
        vm.HasError.Should().BeTrue();

        vm.NewOwnerPassword = OwnerPassword;
        vm.ConfirmOwnerPassword = OwnerPassword;
        vm.ConfirmCommand.Execute(null);
        vm.Mode.Should().Be(RestaurantPickerMode.Add, "it continues to the add form");

        vm.EditName = "مطعم الواحة";
        vm.OwnerPassword = OwnerPassword;
        vm.ConfirmCommand.Execute(null);
        vm.HasError.Should().BeFalse();
        vm.Restaurants.Should().HaveCount(2);
        vm.SelectedRestaurant!.Name.Should().Be("مطعم الواحة");

        bool? closed = null;
        vm.CloseRequested += opened => closed = opened;
        vm.OpenCommand.Execute(null);
        closed.Should().BeTrue();
        vm.Result!.Name.Should().Be("مطعم الواحة");
    }

    [Fact]
    public void Restore_RejectsABackupOfAnotherRestaurant()
    {
        var registry = RestaurantRegistry.Load(_root);
        registry.SetOwnerPassword(OwnerPassword, null);
        var primary = registry.ToActive(registry.Restaurants.Single());
        var second = registry.ToActive(registry.Add("مطعم الواحة", OwnerPassword));

        var primaryBackup = CreateBackupFile("primary.db", primary.Id);
        var legacyBackup = CreateBackupFile("legacy.db", restaurantId: null);

        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(primaryBackup, primary)).Should().NotThrow();
        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(primaryBackup, second))
            .Should().Throw<InvalidDataException>().WithMessage("*مطعماً آخر*");

        // نسخة أقدم من تعدد المطاعم: لا تخص إلا المطعم الأول
        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(legacyBackup, primary)).Should().NotThrow();
        FluentActions.Invoking(() => DatabaseBackupHelper.ValidateMeezanDatabase(legacyBackup, second))
            .Should().Throw<InvalidDataException>();
    }

    private string CreateBackupFile(string name, string? restaurantId)
    {
        var path = Path.Combine(_root, name);
        using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE DailyJournals (Id INTEGER PRIMARY KEY);
            CREATE TABLE CashMovements (Id INTEGER PRIMARY KEY);
            CREATE TABLE Suppliers (Id INTEGER PRIMARY KEY);
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);
            CREATE TABLE Settings (Id INTEGER PRIMARY KEY, Key TEXT, Value TEXT);
            """;
        cmd.ExecuteNonQuery();
        if (restaurantId != null)
        {
            cmd.CommandText = "INSERT INTO Settings (Key, Value) VALUES ($k, $v);";
            cmd.Parameters.AddWithValue("$k", RestaurantContext.SettingKey);
            cmd.Parameters.AddWithValue("$v", restaurantId);
            cmd.ExecuteNonQuery();
        }
        return path;
    }
}
