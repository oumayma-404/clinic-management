using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// The Data Protection key ring's table, for a deployment that sets <c>DataProtection:PersistToDatabase</c>
    /// because its host sells no durable disk. Purely additive: one table, nothing altered or dropped.
    ///
    /// <para>⚠️ <b><see cref="Down"/> destroys the key ring, which is not the same kind of loss as dropping any
    /// other table in this project.</b> Everything encrypted under those keys — every administrator's second
    /// factor, every clinic's reminder credentials, every stored Google refresh token — becomes permanently
    /// unreadable, and no backup of the *data* restores it. Rolling this migration back is a decision to be taken
    /// with the certificate and a key-ring export in hand, not a routine down-migration.</para>
    ///
    /// <para>The rows are ciphertext where a protecting certificate is configured (it is required on
    /// <c>HostedMultiTenant</c>), so a database dump does not disclose the ring — see
    /// <c>LocalDataProtection.PersistToDatabaseKey</c>.</para>
    /// </summary>
    public partial class AddDataProtectionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");
        }
    }
}
