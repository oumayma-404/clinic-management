using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MergeSnapshotReconcile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecurrencePattern",
                table: "RecurringAppointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            // interval -> bigint (ticks): EF's naive AlterColumn cannot cast interval to bigint (fails on ANY
            // DB, even empty). Convert explicitly — TimeSpan.Ticks == seconds * 10,000,000 (1 tick = 100 ns).
            migrationBuilder.Sql(
                "ALTER TABLE \"RecurringAppointments\" ALTER COLUMN \"Duration\" TYPE bigint " +
                "USING (EXTRACT(EPOCH FROM \"Duration\") * 10000000)::bigint;");

            migrationBuilder.AlterColumn<string>(
                name: "DoctorName",
                table: "RecurringAppointments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "RecurringAppointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DoctorId",
                table: "RecurringAppointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "RecurringAppointments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcedureTypeId",
                table: "RecurringAppointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRecallContactedAt",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecallReason",
                table: "Patients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecallSnoozedUntil",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingHoursJson",
                table: "Doctors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecallIntervalMonths",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 6);

            // text -> uuid: DoctorId becomes a Doctor FK. Preserve values that are already GUID strings; null
            // anything else (legacy free-text DoctorId can't map to a Doctor.Id).
            migrationBuilder.Sql(
                "ALTER TABLE \"Appointments\" ALTER COLUMN \"DoctorId\" TYPE uuid " +
                "USING (CASE WHEN \"DoctorId\" ~ '^[0-9a-fA-F-]{36}$' THEN \"DoctorId\"::uuid ELSE NULL END);");

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabWorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToothNumber = table.Column<int>(type: "integer", nullable: true),
                    Prosthetist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabWorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabWorkOrders_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LabWorkOrders_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WaitingListEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredDoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    DesiredTimeframe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResultingAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitingListEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitingListEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WaitingListEntries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringAppointments_ClinicId",
                table: "RecurringAppointments",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ClinicId_ExpenseDate",
                table: "Expenses",
                columns: new[] { "ClinicId", "ExpenseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LabWorkOrders_ClinicId_Status",
                table: "LabWorkOrders",
                columns: new[] { "ClinicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LabWorkOrders_PatientId",
                table: "LabWorkOrders",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitingListEntries_ClinicId_Status",
                table: "WaitingListEntries",
                columns: new[] { "ClinicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitingListEntries_PatientId",
                table: "WaitingListEntries",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Doctors_DoctorId",
                table: "Appointments",
                column: "DoctorId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringAppointments_Clinics_ClinicId",
                table: "RecurringAppointments",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Doctors_DoctorId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringAppointments_Clinics_ClinicId",
                table: "RecurringAppointments");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "LabWorkOrders");

            migrationBuilder.DropTable(
                name: "WaitingListEntries");

            migrationBuilder.DropIndex(
                name: "IX_RecurringAppointments_ClinicId",
                table: "RecurringAppointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ClinicId",
                table: "RecurringAppointments");

            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "RecurringAppointments");

            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                table: "RecurringAppointments");

            migrationBuilder.DropColumn(
                name: "ProcedureTypeId",
                table: "RecurringAppointments");

            migrationBuilder.DropColumn(
                name: "LastRecallContactedAt",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "RecallReason",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "RecallSnoozedUntil",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "WorkingHoursJson",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "RecallIntervalMonths",
                table: "Clinics");

            migrationBuilder.AlterColumn<string>(
                name: "RecurrencePattern",
                table: "RecurringAppointments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.Sql(
                "ALTER TABLE \"RecurringAppointments\" ALTER COLUMN \"Duration\" TYPE interval " +
                "USING (\"Duration\"::double precision / 10000000.0 * interval '1 second');");

            migrationBuilder.AlterColumn<string>(
                name: "DoctorName",
                table: "RecurringAppointments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.Sql(
                "ALTER TABLE \"Appointments\" ALTER COLUMN \"DoctorId\" TYPE text USING \"DoctorId\"::text;");
        }
    }
}
