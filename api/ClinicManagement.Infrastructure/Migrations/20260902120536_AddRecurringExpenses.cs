using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Les dépenses mensuelles: the standing monthly commitments (loyer, salaire, crédit) and the back-link from
    /// each dépense the posting pass writes.
    ///
    /// <para><c>Expenses.RecurringExpenseId</c> is nullable with no default and no backfill, and its FK is
    /// <b><c>SetNull</c></b> rather than <c>Cascade</c>: the column says where a row came from, while the money is
    /// the row's own. A cascade would let removing one commitment delete every dépense it ever posted, silently
    /// raising the reported Net of every period the series ran through.</para>
    /// </summary>
    public partial class AddRecurringExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecurringExpenseId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: false),
                    LastPostedMonth = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                    // ⚠️ The scaffolder emitted `xmin = table.Column<uint>(type: "xid", …)` here and it was
                    // removed by hand — the same line `AddCalendarImportRuns…`, `AddClinicSubscriptions` and
                    // `AddSuppliers` each had to delete. `Entity<T>.Version` maps onto PostgreSQL's *system*
                    // column, so the differ writes it out as a real one and the migration dies with « column
                    // name "xmin" conflicts with a system column name ». Rows still get their token regardless.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringExpenses_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_RecurringExpenseId",
                table: "Expenses",
                column: "RecurringExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_ClinicId_CancelledAt",
                table: "RecurringExpenses",
                columns: new[] { "ClinicId", "CancelledAt" },
                filter: "\"CancelledAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_RecurringExpenses_RecurringExpenseId",
                table: "Expenses",
                column: "RecurringExpenseId",
                principalTable: "RecurringExpenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_RecurringExpenses_RecurringExpenseId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "RecurringExpenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_RecurringExpenseId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "RecurringExpenseId",
                table: "Expenses");
        }
    }
}
