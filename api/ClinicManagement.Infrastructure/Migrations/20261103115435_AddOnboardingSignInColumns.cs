using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// guided-first-days Part A: three nullable <c>Users</c> columns. <c>TotpEnrolmentStartedAt</c> lets the sign-in
    /// enrolment show the SAME pending QR for 24 h instead of minting a new one per attempt; the two
    /// <c>PhoneApp…</c> columns record the first sign-in from the phone app (« Premiers pas »). Purely additive,
    /// no default and no backfill — NULL is the right reading for every existing row.
    /// </summary>
    public partial class AddOnboardingSignInColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneAppFirstSignInAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneAppPlatform",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TotpEnrolmentStartedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneAppFirstSignInAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneAppPlatform",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpEnrolmentStartedAt",
                table: "Users");
        }
    }
}
