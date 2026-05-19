using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialPostingSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinancialStatus",
                table: "SaleHeaders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PostedByUserId",
                table: "SaleHeaders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedDate",
                table: "SaleHeaders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostingSessionId",
                table: "SaleHeaders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SaleHeaders",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "FinancialStatus",
                table: "GeneralExpenses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PostedByUserId",
                table: "GeneralExpenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedDate",
                table: "GeneralExpenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostingSessionId",
                table: "GeneralExpenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "GeneralExpenses",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "FinancialStatus",
                table: "DailyJournals",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PostedByUserId",
                table: "DailyJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedDate",
                table: "DailyJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostingSessionId",
                table: "DailyJournals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "DailyJournals",
                type: "BLOB",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "FinancialPeriods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostingSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PostedUntilDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalAffectedRows = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostingSessionDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostingSessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostingSessionId1 = table.Column<int>(type: "INTEGER", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingSessionDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostingSessionDetails_PostingSessions_PostingSessionId1",
                        column: x => x.PostingSessionId1,
                        principalTable: "PostingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostingSessionDetails_PostingSessionId1",
                table: "PostingSessionDetails",
                column: "PostingSessionId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialPeriods");

            migrationBuilder.DropTable(
                name: "PostingSessionDetails");

            migrationBuilder.DropTable(
                name: "PostingSessions");

            migrationBuilder.DropColumn(
                name: "FinancialStatus",
                table: "SaleHeaders");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "SaleHeaders");

            migrationBuilder.DropColumn(
                name: "PostedDate",
                table: "SaleHeaders");

            migrationBuilder.DropColumn(
                name: "PostingSessionId",
                table: "SaleHeaders");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SaleHeaders");

            migrationBuilder.DropColumn(
                name: "FinancialStatus",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "PostedDate",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "PostingSessionId",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "GeneralExpenses");

            migrationBuilder.DropColumn(
                name: "FinancialStatus",
                table: "DailyJournals");

            migrationBuilder.DropColumn(
                name: "PostedByUserId",
                table: "DailyJournals");

            migrationBuilder.DropColumn(
                name: "PostedDate",
                table: "DailyJournals");

            migrationBuilder.DropColumn(
                name: "PostingSessionId",
                table: "DailyJournals");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "DailyJournals");
        }
    }
}
