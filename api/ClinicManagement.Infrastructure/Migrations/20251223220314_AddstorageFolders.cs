using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddstorageFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add FolderId column (nullable)
            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "PatientFiles",
                type: "uuid",
                nullable: true);

            // Add StorageKey column as nullable first
            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "PatientFiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Migrate existing FilePath data to StorageKey
            // Note: If FilePath contains actual file paths, they will be preserved
            // If migrating to MinIO, you may need to upload files separately
            migrationBuilder.Sql(@"
                UPDATE ""PatientFiles""
                SET ""StorageKey"" = ""FilePath""
                WHERE ""FilePath"" IS NOT NULL AND ""FilePath"" != '';
            ");

            // Generate storage keys for any remaining null values (if FilePath was null/empty)
            migrationBuilder.Sql(@"
                UPDATE ""PatientFiles""
                SET ""StorageKey"" = gen_random_uuid()::text || '-' || ""FileName""
                WHERE ""StorageKey"" IS NULL OR ""StorageKey"" = '';
            ");

            // Now make StorageKey required
            migrationBuilder.AlterColumn<string>(
                name: "StorageKey",
                table: "PatientFiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            // Drop FilePath column after data migration
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "PatientFiles");

            migrationBuilder.CreateTable(
                name: "PatientFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientFolders_PatientFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "PatientFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientFolders_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientFiles_FolderId",
                table: "PatientFiles",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientFolders_ParentFolderId",
                table: "PatientFolders",
                column: "ParentFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientFolders_PatientId",
                table: "PatientFolders",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_PatientFiles_PatientFolders_FolderId",
                table: "PatientFiles",
                column: "FolderId",
                principalTable: "PatientFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PatientFiles_PatientFolders_FolderId",
                table: "PatientFiles");

            migrationBuilder.DropTable(
                name: "PatientFolders");

            migrationBuilder.DropIndex(
                name: "IX_PatientFiles_FolderId",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "PatientFiles");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "PatientFiles");

            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "PatientFiles",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }
    }
}
