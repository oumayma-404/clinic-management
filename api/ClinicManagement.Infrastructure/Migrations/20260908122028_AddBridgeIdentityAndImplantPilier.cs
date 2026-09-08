using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBridgeIdentityAndImplantPilier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * **Which bridge a tooth belongs to** — an opaque grouping token, deliberately NOT a foreign key,
             * because there is nothing to point at.
             *
             * ⚠️ **NULL is a first-class value and there is deliberately NO BACKFILL.** Every row written
             * before today carries null, and the client reads that as « ungrouped » and answers it with the
             * legacy arch-adjacency scan — which is exactly how those bridges have always rendered. A backfill
             * would have to infer a group from adjacency, i.e. freeze today's *wrong* answer into data where
             * nothing can ever correct it: that inference is what drew two neighbouring bridges as one bar and
             * painted a finished crown as still-to-place. `BridgeCharting` refuses to read a bridge's shape off
             * position; its extent cannot be read off position either.
             *
             * ⚠️ The index is what `bridge-runs.ts` groups the whole odontogramme read on.
             */
            migrationBuilder.AddColumn<Guid>(
                name: "BridgeGroupId",
                table: "ToothStates",
                type: "uuid",
                nullable: true);

            /*
             * Which teeth of a bridge act are piliers carried by an **implant** — the third role, and the same
             * JSON int array as `PonticToothNumbers` beside it, for the reasons that migration records.
             *
             * ⚠️ Purely additive: `""` deserialises to an empty list through the value converter, and **both
             * lists empty** is what `BridgeCharting.ConditionFor`'s fourth branch keys on to leave a row
             * charting exactly as it always did. So every historical act is unaffected by construction.
             *
             * ⚠️ Checked for the scaffolded `xmin` column this solution's 38 `Entity<TId>.Version` mappings
             * provoke — none was emitted, because the model snapshot was current when this was generated. It was
             * also checked for a scaffolded `DropColumn` placed above a backfill that reads it; there is none,
             * since nothing is dropped and nothing is backfilled.
             */
            migrationBuilder.AddColumn<string>(
                name: "ImplantPilierToothNumbers",
                table: "DentalRecordActs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ToothStates_BridgeGroupId",
                table: "ToothStates",
                column: "BridgeGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ToothStates_BridgeGroupId",
                table: "ToothStates");

            migrationBuilder.DropColumn(
                name: "BridgeGroupId",
                table: "ToothStates");

            migrationBuilder.DropColumn(
                name: "ImplantPilierToothNumbers",
                table: "DentalRecordActs");
        }
    }
}
