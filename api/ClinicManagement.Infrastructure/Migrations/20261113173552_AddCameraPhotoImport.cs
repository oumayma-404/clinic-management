using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraPhotoImport : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Camera photo import (features/camera-photo-import): capture facts and the « original kept on the PC »
        /// record on PatientFiles, the same capture facts on FileUploadSessions, and the cabinet's paired cameras.
        /// Purely additive — every column nullable or defaulted, no backfill. ⚠️ The scaffolded <c>xmin</c> column of
        /// <c>ClinicCameras</c> was removed by hand: it maps onto PostgreSQL's system column, which CREATE TABLE refuses.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CameraModel",
                table: "PatientFiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureKey",
                table: "PatientFiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapturedAt",
                table: "PatientFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "PatientFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OriginalHeldBy",
                table: "PatientFiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalKeptAt",
                table: "PatientFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalSha256",
                table: "PatientFiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OriginalSizeBytes",
                table: "PatientFiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraModel",
                table: "FileUploadSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CaptureKey",
                table: "FileUploadSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapturedAt",
                table: "FileUploadSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "FileUploadSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ClinicCameras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Make = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SerialSuffix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClockOffsetMinutes = table.Column<int>(type: "integer", nullable: false),
                    LastImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicCameras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicCameras_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientFiles_ClinicId_CaptureKey",
                table: "PatientFiles",
                columns: new[] { "ClinicId", "CaptureKey" },
                unique: true,
                filter: "\"CaptureKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicCameras_ClinicId_Fingerprint",
                table: "ClinicCameras",
                columns: new[] { "ClinicId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicCameras_DoctorId",
                table: "ClinicCameras",
                column: "DoctorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicCameras");

            migrationBuilder.DropIndex(
                name: "IX_PatientFiles_ClinicId_CaptureKey",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "CameraModel",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "CaptureKey",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "CapturedAt",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "OriginalHeldBy",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "OriginalKeptAt",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "OriginalSha256",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "OriginalSizeBytes",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "CameraModel",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "CaptureKey",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "CapturedAt",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "FileUploadSessions");
        }
    }
}
