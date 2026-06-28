using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using MeezanPOS.Domain.Entities;
using MeezanPOS.Domain.Enums;
using MeezanPOS.Infrastructure.Data;
using MeezanPOS.Tests.Helpers;

namespace MeezanPOS.Tests.Integration;

public class ResetSystemTests : IDisposable
{
    private readonly AppDbContext _context;

    public ResetSystemTests()
    {
        MessageBoxMock.Initialize();
        MessageBoxMock.Reset();

        var dbPath = AppDbContext.GetDatabasePath();
        SqliteConnection.ClearAllPools();
        if (System.IO.File.Exists(dbPath))
        {
            try
            {
                System.IO.File.Delete(dbPath);
            }
            catch { }
        }

        _context = new AppDbContext();
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        SqliteConnection.ClearAllPools();
        var dbPath = AppDbContext.GetDatabasePath();
        if (System.IO.File.Exists(dbPath))
        {
            try
            {
                System.IO.File.Delete(dbPath);
            }
            catch { }
        }
    }

    private async Task SimulateResetSystemAsync(string dbPath, bool simulateFailure)
    {
        var tablesToKeep = new HashSet<string>
        {
            "Users",
            "Roles",
            "__EFMigrationsHistory",
            "Settings"
        };

        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            await conn.OpenAsync();

            // Turn off foreign keys temporarily
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA foreign_keys = OFF;";
                await pragmaCmd.ExecuteNonQueryAsync();
            }

            // Drop all triggers
            var allTriggers = new List<string>();
            using (var getTrigCmd = conn.CreateCommand())
            {
                getTrigCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger'";
                using (var trigReader = await getTrigCmd.ExecuteReaderAsync())
                {
                    while (await trigReader.ReadAsync())
                        allTriggers.Add(trigReader.GetString(0));
                }
            }
            foreach (var trigger in allTriggers)
            {
                using (var dropCmd = conn.CreateCommand())
                {
                    dropCmd.CommandText = $"DROP TRIGGER IF EXISTS \"{trigger}\";";
                    await dropCmd.ExecuteNonQueryAsync();
                }
            }

            // Get all tables
            var allTables = new List<string>();
            using (var getTablesCmd = conn.CreateCommand())
            {
                getTablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                using (var reader = await getTablesCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        allTables.Add(reader.GetString(0));
                    }
                }
            }

            using var transaction = conn.BeginTransaction();
            try
            {
                foreach (var table in allTables)
                {
                    if (!tablesToKeep.Contains(table) && !table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var deleteCmd = conn.CreateCommand())
                        {
                            deleteCmd.Transaction = transaction;
                            deleteCmd.CommandText = $"DELETE FROM \"{table}\";";
                            await deleteCmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                if (simulateFailure)
                {
                    throw new InvalidOperationException("Simulated failure mid-reset.");
                }

                using (var resetSeqCmd = conn.CreateCommand())
                {
                    resetSeqCmd.Transaction = transaction;
                    resetSeqCmd.CommandText = "DELETE FROM sqlite_sequence;";
                    await resetSeqCmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
                await pragmaCmd.ExecuteNonQueryAsync();
            }
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task ResetSystem_ShouldClearAllOperationalTables()
    {
        // Arrange
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.Today, FinancialStatus.Draft, 1000m, 100m);
        var expense = new GeneralExpense
        {
            Amount = 250m,
            ExpenseType = GeneralExpenseType.Rent,
            PaymentDate = DateTime.Today,
            PaymentMethod = PaymentMethodType.Cash,
            FinancialStatus = FinancialStatus.Draft
        };
        var bankAcc = TestDataBuilder.BuildBankAccount("Reset Bank", 500m);

        _context.DailyJournals.Add(journal);
        _context.GeneralExpenses.Add(expense);
        _context.BankAccounts.Add(bankAcc);
        await _context.SaveChangesAsync();

        var bankTx = TestDataBuilder.BuildBankTransaction(100m, BankTransactionType.Deposit, bankAcc.Id, DateTime.Today);
        _context.BankTransactions.Add(bankTx);
        await _context.SaveChangesAsync();

        // Act
        var dbPath = AppDbContext.GetDatabasePath();
        await SimulateResetSystemAsync(dbPath, simulateFailure: false);

        // Assert
        using (var checkContext = new AppDbContext())
        {
            var journalsCount = await checkContext.DailyJournals.CountAsync();
            var expensesCount = await checkContext.GeneralExpenses.CountAsync();
            var bankTransactionsCount = await checkContext.BankTransactions.CountAsync();

            journalsCount.Should().Be(0);
            expensesCount.Should().Be(0);
            bankTransactionsCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task ResetSystem_ShouldPreserve_UsersAndRoles()
    {
        // Arrange: Seed Users and Roles
        var role = new Role { Type = RoleType.Admin, Name = "TestRole" };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        var user = new User { Username = "ResetUser", PasswordHash = "hash", RoleId = role.Id };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var dbPath = AppDbContext.GetDatabasePath();
        await SimulateResetSystemAsync(dbPath, simulateFailure: false);

        // Assert
        using (var checkContext = new AppDbContext())
        {
            var dbUser = await checkContext.Users.FirstOrDefaultAsync(u => u.Username == "ResetUser");
            var dbRole = await checkContext.Roles.FirstOrDefaultAsync(r => r.Name == "TestRole");

            dbUser.Should().NotBeNull();
            dbRole.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ResetSystem_ShouldPreserve_SettingsTable()
    {
        // Arrange
        var setting = new Setting { Key = "BackupPath", Value = "D:\\Backup" };
        _context.Settings.Add(setting);
        await _context.SaveChangesAsync();

        // Act
        var dbPath = AppDbContext.GetDatabasePath();
        await SimulateResetSystemAsync(dbPath, simulateFailure: false);

        // Assert
        using (var checkContext = new AppDbContext())
        {
            var dbSetting = await checkContext.Settings.FirstOrDefaultAsync(s => s.Key == "BackupPath");
            dbSetting.Should().NotBeNull();
            dbSetting!.Value.Should().Be("D:\\Backup");
        }
    }

    [Fact]
    public async Task ResetSystem_WithoutTransaction_ShouldRollback_OnFailure()
    {
        // Arrange: Seed some operational data
        var journal = TestDataBuilder.BuildDailyJournal(DateTime.Today, FinancialStatus.Draft, 1000m, 100m);
        _context.DailyJournals.Add(journal);
        await _context.SaveChangesAsync();

        // Act: Execute reset and simulate failure mid-transaction
        var dbPath = AppDbContext.GetDatabasePath();
        Func<Task> act = async () => await SimulateResetSystemAsync(dbPath, simulateFailure: true);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Verify data was NOT partially deleted (transaction rollback worked)
        using (var checkContext = new AppDbContext())
        {
            var journalsCount = await checkContext.DailyJournals.CountAsync();
            journalsCount.Should().Be(1);
        }
    }
}
