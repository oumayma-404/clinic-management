using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockBatchesMaterialsAndSchemaHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "StockItems",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldNullable: true);

            // The differ defaults a new non-nullable int to 0, but 0 is not a legal value here: the domain
            // requires 1-365, and the read treats a non-positive lead time as "never expiring soon" -- which
            // would silently disable the approaching-expiry feature for every EXISTING clinic. Default to the
            // domain's own constant instead.
            migrationBuilder.AddColumn<int>(
                name: "StockExpiryLeadDays",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "ProcedureTypeMaterials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityPerAct = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureTypeMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcedureTypeMaterials_ProcedureTypes_ProcedureTypeId",
                        column: x => x.ProcedureTypeId,
                        principalTable: "ProcedureTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcedureTypeMaterials_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RemainingQuantity = table.Column<int>(type: "integer", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BatchNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockBatches_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ClinicId",
                table: "StockMovements",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status_ScheduledFor",
                table: "Notifications",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureTypeMaterials_ProcedureTypeId_StockItemId",
                table: "ProcedureTypeMaterials",
                columns: new[] { "ProcedureTypeId", "StockItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureTypeMaterials_StockItemId",
                table: "ProcedureTypeMaterials",
                column: "StockItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_StockItemId_ExpiryDate",
                table: "StockBatches",
                columns: new[] { "StockItemId", "ExpiryDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Clinics_ClinicId",
                table: "StockMovements",
                column: "ClinicId",
                principalTable: "Clinics",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // -------------------------------------------------------------------------------------------
            // AC-P4.8 -- fold each item's legacy scalar expiry/batch into ONE opening batch, then and only
            // then drop the old columns.
            //
            // ORDER IS THE WHOLE POINT. EF's differ emitted both DropColumns as the FIRST statements of
            // this migration -- before StockBatches even existed -- which would have destroyed every stored
            // expiry date with nothing to read them into. They are moved below the backfill deliberately
            // (plan R-7: destructive steps last).
            //
            // IDEMPOTENT via NOT EXISTS (R-7 again): a migration that partially applied and re-ran must not
            // create a second opening batch. The opening batch's quantity is the item's CURRENT on-hand,
            // which is ALREADY counted in StockItems.CurrentStock -- this describes existing stock, it does
            // not add any. Items with on-hand <= 0 get no batch: there is no lot to describe, and
            // ReceivedQuantity must be positive.
            migrationBuilder.Sql("""
                INSERT INTO "StockBatches" ("Id", "StockItemId", "ReceivedQuantity", "RemainingQuantity",
                                            "ExpiryDate", "BatchNumber", "ReceivedAt")
                SELECT gen_random_uuid(),
                       s."Id",
                       s."CurrentStock",
                       s."CurrentStock",
                       s."ExpiryDate",
                       s."BatchNumber",
                       COALESCE(s."UpdatedAt", s."CreatedAt")
                FROM "StockItems" s
                WHERE s."CurrentStock" > 0
                  AND (s."ExpiryDate" IS NOT NULL OR s."BatchNumber" IS NOT NULL)
                  AND NOT EXISTS (
                      SELECT 1 FROM "StockBatches" b WHERE b."StockItemId" = s."Id"
                  );
                """);

            // An item with stock but NO legacy expiry/batch still needs an opening lot, or FEFO would find
            // nothing to draw from and every consume would report a full shortfall against stock that is
            // physically on the shelf. Undated, so it sorts last -- exactly the intended FEFO behaviour.
            migrationBuilder.Sql("""
                INSERT INTO "StockBatches" ("Id", "StockItemId", "ReceivedQuantity", "RemainingQuantity",
                                            "ExpiryDate", "BatchNumber", "ReceivedAt")
                SELECT gen_random_uuid(),
                       s."Id",
                       s."CurrentStock",
                       s."CurrentStock",
                       NULL,
                       NULL,
                       COALESCE(s."UpdatedAt", s."CreatedAt")
                FROM "StockItems" s
                WHERE s."CurrentStock" > 0
                  AND s."ExpiryDate" IS NULL
                  AND s."BatchNumber" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "StockBatches" b WHERE b."StockItemId" = s."Id"
                  );
                """);

            // Destructive, and therefore last (R-7). verify-schema's `stock-batch-backfill` check is what
            // proves the statements above covered every item that had a date, before these two ran.
            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "StockItems");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "StockItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Clinics_ClinicId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProcedureTypeMaterials");

            migrationBuilder.DropTable(
                name: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ClinicId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_Status_ScheduledFor",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "StockExpiryLeadDays",
                table: "Clinics");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "StockItems",
                type: "numeric(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,3)",
                oldPrecision: 18,
                oldScale: 3,
                oldNullable: true);

            // The two scalar columns come back, then are repopulated from the EARLIEST remaining lot -- the
            // same "relevant expiry" the scalar used to approximate. A down-migration cannot restore per-lot
            // detail (that is precisely the information the old shape could not hold), but it must not
            // silently return NULL for an item whose expiry is known.
            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "StockItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "StockItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "StockItems" s
                SET "ExpiryDate" = b."ExpiryDate",
                    "BatchNumber" = b."BatchNumber"
                FROM (
                    SELECT DISTINCT ON ("StockItemId") "StockItemId", "ExpiryDate", "BatchNumber"
                    FROM "StockBatches"
                    WHERE "RemainingQuantity" > 0
                    ORDER BY "StockItemId", "ExpiryDate" NULLS LAST, "ReceivedAt"
                ) b
                WHERE b."StockItemId" = s."Id";
                """);
        }
    }
}
