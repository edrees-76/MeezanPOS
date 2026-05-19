using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeezanPOS.Migrations
{
    /// <inheritdoc />
    public partial class FixPostingSessionIdType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostingSessionDetails_PostingSessions_PostingSessionId1",
                table: "PostingSessionDetails");

            migrationBuilder.DropIndex(
                name: "IX_PostingSessionDetails_PostingSessionId1",
                table: "PostingSessionDetails");

            migrationBuilder.DropColumn(
                name: "PostingSessionId1",
                table: "PostingSessionDetails");

            migrationBuilder.AlterColumn<int>(
                name: "PostingSessionId",
                table: "SaleHeaders",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PostingSessionId",
                table: "PostingSessionDetails",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "EntityId",
                table: "PostingSessionDetails",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<int>(
                name: "PostingSessionId",
                table: "GeneralExpenses",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PostingSessionId",
                table: "DailyJournals",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingSessionDetails_PostingSessionId",
                table: "PostingSessionDetails",
                column: "PostingSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostingSessionDetails_PostingSessions_PostingSessionId",
                table: "PostingSessionDetails",
                column: "PostingSessionId",
                principalTable: "PostingSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostingSessionDetails_PostingSessions_PostingSessionId",
                table: "PostingSessionDetails");

            migrationBuilder.DropIndex(
                name: "IX_PostingSessionDetails_PostingSessionId",
                table: "PostingSessionDetails");

            migrationBuilder.AlterColumn<Guid>(
                name: "PostingSessionId",
                table: "SaleHeaders",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PostingSessionId",
                table: "PostingSessionDetails",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<Guid>(
                name: "EntityId",
                table: "PostingSessionDetails",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "PostingSessionId1",
                table: "PostingSessionDetails",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PostingSessionId",
                table: "GeneralExpenses",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PostingSessionId",
                table: "DailyJournals",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostingSessionDetails_PostingSessionId1",
                table: "PostingSessionDetails",
                column: "PostingSessionId1");

            migrationBuilder.AddForeignKey(
                name: "FK_PostingSessionDetails_PostingSessions_PostingSessionId1",
                table: "PostingSessionDetails",
                column: "PostingSessionId1",
                principalTable: "PostingSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
