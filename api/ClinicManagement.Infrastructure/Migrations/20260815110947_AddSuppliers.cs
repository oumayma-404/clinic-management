using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Turns the two free-text contact names into a real <c>Suppliers</c> aggregate, and links both sides to it.
    ///
    /// <para>⚠️ <b>The statement order is the design, not the scaffolder's.</b> EF emitted
    /// <c>DropColumn("Supplier")</c> as the <i>first</i> statement — it has no idea the backfill three blocks
    /// below reads that column — which would have silently created zero suppliers and linked zero articles on
    /// every existing database, while reporting a clean migration. Every backfill therefore sits below the DDL
    /// that creates what it writes into and <b>above</b> the drop of what it reads.</para>
    ///
    /// <para>⚠️ <b>EF's differ also emitted an <c>xmin</c> column</b> in the <c>CreateTable</c> block, because
    /// <c>Entity&lt;T&gt;.Version</c> is mapped onto PostgreSQL's <i>system</i> column of that name. PostgreSQL
    /// refuses it (<c>column name "xmin" conflicts with a system column name</c>) — the same rejection that makes
    /// <c>AddConcurrencyToken</c>'s <c>Up()</c> deliberately empty. It is removed by hand; every row still gets
    /// its token from the system column.</para>
    ///
    /// <para>⚠️ <b>Every backfill is gated on « this row does not exist yet »</b>, so <c>Up()</c> is safe to
    /// re-run against a populated database rather than double-allocating.</para>
    /// </summary>
    public partial class AddSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "StockItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "LabWorkOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                    // xmin removed by hand — see the class remarks.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Suppliers_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_SupplierId",
                table: "StockItems",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_LabWorkOrders_SupplierId",
                table: "LabWorkOrders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_ClinicId_Category",
                table: "Suppliers",
                columns: new[] { "ClinicId", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_ClinicId_Name",
                table: "Suppliers",
                columns: new[] { "ClinicId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LabWorkOrders_Suppliers_SupplierId",
                table: "LabWorkOrders",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockItems_Suppliers_SupplierId",
                table: "StockItems",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Backfill 1 of 4 — a Supplier row per distinct stock-article supplier name, per clinic.
            //
            // Folded on lower(btrim(…)), so « Dentalex », « dentalex » and « Dentalex  » become ONE row (EC-2);
            // MIN picks the spelling deterministically rather than letting it depend on row order. Category is
            // left NULL: what a stock supplier sells is not derivable from an article's own category, and
            // guessing one would file a dépôt under whatever the first article to name it happened to be.
            migrationBuilder.Sql("""
                INSERT INTO "Suppliers" ("Id", "ClinicId", "Name", "IsActive", "CreatedAt")
                SELECT gen_random_uuid(), s."ClinicId", MIN(btrim(s."Supplier")), TRUE, now()
                FROM "StockItems" s
                WHERE s."Supplier" IS NOT NULL
                  AND btrim(s."Supplier") <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM "Suppliers" sup
                      WHERE sup."ClinicId" = s."ClinicId"
                        AND lower(btrim(sup."Name")) = lower(btrim(s."Supplier")))
                GROUP BY s."ClinicId", lower(btrim(s."Supplier"));
                """);

            // ── Backfill 2 of 4 — point each article at the row just created for its name.
            migrationBuilder.Sql("""
                UPDATE "StockItems" s
                SET "SupplierId" = sup."Id"
                FROM "Suppliers" sup
                WHERE sup."ClinicId" = s."ClinicId"
                  AND s."Supplier" IS NOT NULL
                  AND btrim(s."Supplier") <> ''
                  AND lower(btrim(sup."Name")) = lower(btrim(s."Supplier"));
                """);

            // ── Backfill 3 of 4 — the same for the laboratories named on bons de prothèse.
            //
            // It runs AFTER the stock pass and carries a NOT EXISTS against the rows that pass created, so a
            // dépôt that is both a stock supplier and a prothésiste reuses the one row instead of producing a
            // second that differs only in case — which the unique index (case-sensitive) would happily accept
            // and the link below would then match ambiguously.
            //
            // Unlike the stock pass this one DOES know the category: the name came off a bon de prothèse.
            migrationBuilder.Sql("""
                INSERT INTO "Suppliers" ("Id", "ClinicId", "Name", "Category", "IsActive", "CreatedAt")
                SELECT gen_random_uuid(), o."ClinicId", MIN(btrim(o."Prosthetist")),
                       'Laboratoire de prothèse', TRUE, now()
                FROM "LabWorkOrders" o
                WHERE o."Prosthetist" IS NOT NULL
                  AND btrim(o."Prosthetist") <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM "Suppliers" sup
                      WHERE sup."ClinicId" = o."ClinicId"
                        AND lower(btrim(sup."Name")) = lower(btrim(o."Prosthetist")))
                GROUP BY o."ClinicId", lower(btrim(o."Prosthetist"));
                """);

            migrationBuilder.Sql("""
                UPDATE "LabWorkOrders" o
                SET "SupplierId" = sup."Id"
                FROM "Suppliers" sup
                WHERE sup."ClinicId" = o."ClinicId"
                  AND o."Prosthetist" IS NOT NULL
                  AND btrim(o."Prosthetist") <> ''
                  AND lower(btrim(sup."Name")) = lower(btrim(o."Prosthetist"));
                """);

            // ── Backfill 4 of 4 — the six English category keys become their French labels.
            //
            // ⚠️ These pairs mirror `Domain/Services/StockCategories.LegacyKeys`, which is the authority and which
            // still folds an English key at runtime — an older client, a bookmarked `?category=PPE` filter and a
            // CSV import can all present one after this has run, and folding costs nothing.
            migrationBuilder.Sql("""
                UPDATE "StockItems"
                SET "Category" = CASE "Category"
                    WHEN 'Medical Supplies'  THEN 'Consommables médicaux'
                    WHEN 'PPE'               THEN 'Protection (EPI)'
                    WHEN 'Medications'       THEN 'Médicaments'
                    WHEN 'Medical Equipment' THEN 'Équipement médical'
                    WHEN 'Lab Supplies'      THEN 'Fournitures de laboratoire'
                    WHEN 'Office Supplies'   THEN 'Fournitures de bureau'
                    ELSE "Category"
                END
                WHERE "Category" IN (
                    'Medical Supplies', 'PPE', 'Medications',
                    'Medical Equipment', 'Lab Supplies', 'Office Supplies');
                """);

            // Last, and only now that everything above has read it.
            migrationBuilder.DropColumn(
                name: "Supplier",
                table: "StockItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Supplier",
                table: "StockItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Put the names back before the rows that hold them are dropped, or the down migration loses the
            // very data the up migration was careful to preserve.
            migrationBuilder.Sql("""
                UPDATE "StockItems" s
                SET "Supplier" = sup."Name"
                FROM "Suppliers" sup
                WHERE sup."Id" = s."SupplierId";
                """);

            // The category rewrite is reversed too — leaving French labels behind would strand a downgraded
            // deployment on values its own STOCK_CATEGORIES no longer contains.
            migrationBuilder.Sql("""
                UPDATE "StockItems"
                SET "Category" = CASE "Category"
                    WHEN 'Consommables médicaux'        THEN 'Medical Supplies'
                    WHEN 'Protection (EPI)'             THEN 'PPE'
                    WHEN 'Médicaments'                  THEN 'Medications'
                    WHEN 'Équipement médical'           THEN 'Medical Equipment'
                    WHEN 'Fournitures de laboratoire'   THEN 'Lab Supplies'
                    WHEN 'Fournitures de bureau'        THEN 'Office Supplies'
                    ELSE "Category"
                END
                WHERE "Category" IN (
                    'Consommables médicaux', 'Protection (EPI)', 'Médicaments',
                    'Équipement médical', 'Fournitures de laboratoire', 'Fournitures de bureau');
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_LabWorkOrders_Suppliers_SupplierId",
                table: "LabWorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_StockItems_Suppliers_SupplierId",
                table: "StockItems");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StockItems_SupplierId",
                table: "StockItems");

            migrationBuilder.DropIndex(
                name: "IX_LabWorkOrders_SupplierId",
                table: "LabWorkOrders");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "LabWorkOrders");
        }
    }
}
