using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ClinicManagement.Infrastructure.Persistence;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Medications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Form = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Strength = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsProvisional = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicationActiveIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dci = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationActiveIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationActiveIngredients_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationActiveIngredients_MedicationId_Dci",
                table: "MedicationActiveIngredients",
                columns: new[] { "MedicationId", "Dci" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Medications_BrandName",
                table: "Medications",
                column: "BrandName");

            // Seed the provisional starter medication catalog from the shared MedicationCatalogSeed
            // single-source-of-truth. Every row is IsProvisional = true ("à vérifier") until an admin
            // confirms it. Ids are deterministic (stable across machines / re-generations).
            var seededAt = MedicationCatalogSeed.SeededAtUtc;

            foreach (var m in MedicationCatalogSeed.Medications)
            {
                migrationBuilder.InsertData(
                    table: "Medications",
                    columns: new[] { "Id", "BrandName", "Form", "Strength", "IsActive", "IsProvisional", "CreatedAt", "UpdatedAt" },
                    values: new object[] { m.Id, m.BrandName, m.Form, m.Strength, true, true, seededAt, null });
            }

            foreach (var i in MedicationCatalogSeed.Ingredients)
            {
                migrationBuilder.InsertData(
                    table: "MedicationActiveIngredients",
                    columns: new[] { "Id", "MedicationId", "Dci", "CreatedAt" },
                    values: new object[] { i.Id, i.MedicationId, i.Dci, seededAt });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicationActiveIngredients");

            migrationBuilder.DropTable(
                name: "Medications");
        }
    }
}
