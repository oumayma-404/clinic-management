using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCnamCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CnamLetterValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LettreCle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CnamLetterValues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CnamNomenclatureEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeActe = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DesignationFr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LettreCle = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Coefficient = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CnamNomenclatureEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CnamLetterValues_LettreCle",
                table: "CnamLetterValues",
                column: "LettreCle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CnamNomenclatureEntries_CodeActe",
                table: "CnamNomenclatureEntries",
                column: "CodeActe",
                unique: true);

            // The seeding loops that used to be here are gone (feature single-act-catalogue). They were already
            // dead: AddPerClinicCatalogs (2026-07-23) deletes every row of both tables when the catalogs became
            // per-clinic, and ClinicCatalogSeeder re-seeds them. CnamNomenclatureEntries is dropped outright by
            // DropCnamNomenclatureCatalog, and its entity no longer exists to seed from.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CnamLetterValues");

            migrationBuilder.DropTable(
                name: "CnamNomenclatureEntries");
        }
    }
}
