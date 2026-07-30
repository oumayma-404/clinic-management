using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Installs PostgreSQL's <c>unaccent</c> extension, which every paginated list's free-text search depends on.
    ///
    /// <para><b>Why the extension became necessary.</b> Search used to run in C#, over rows the handler had already
    /// loaded — which was equivalent to searching the clinic only because the handler loaded the whole clinic. Once
    /// the lists take a page, an in-memory search can only see the rows on it, so a patient on page 7 reads as
    /// « aucun résultat ». Pushing the predicate into SQL is therefore not an optimisation, it is the correctness
    /// fix; and doing it without losing accent-insensitivity (Amïne, Béchir, Chaâbane — nobody types the accents)
    /// requires the database to be able to fold accents itself.</para>
    ///
    /// <para><b>The alternative was rejected:</b> a persisted normalised column per searchable entity, maintained
    /// on write. That is a migration, a backfill and a standing obligation on eleven aggregates, and any writer
    /// that forgets it produces a row invisible to search — indistinguishable, to the person looking, from the
    /// record not existing. A read-time function cannot be forgotten by a writer.</para>
    ///
    /// <para>The EF-side mapping lives in <c>SqlSearch.MapUnaccent</c>, registered from
    /// <c>ApplicationDbContext.OnModelCreating</c>. EF does not manage function definitions, so the model diff for
    /// that change is empty and this migration is hand-written — the same reason
    /// <c>AddAppointmentBookingIntegrity</c> had to hand-write its own <c>CREATE EXTENSION</c>.</para>
    /// </summary>
    public partial class AddUnaccentForListSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `unaccent` ships `trusted = true` in PostgreSQL 16, so the database owner can create it without
            // superuser — the Local installer's `clinic_user` owns its database and Cloud runs Postgres in-stack.
            // This is the same precondition `btree_gist` already relies on (AddAppointmentBookingIntegrity), so an
            // install that can host this schema at all can host this.
            //
            // Not schema-qualified: it lands wherever the current `search_path` resolves to, and the EF mapping is
            // likewise unqualified, so the two cannot disagree about where the function lives.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately NOT `DROP EXTENSION`. Reverting the C# is enough to stop using it, whereas dropping it
            // fails outright on any database where something else has come to depend on it — turning a routine
            // rollback into a stuck migration. An unused extension costs nothing; a rollback that cannot finish
            // costs an outage.
        }
    }
}
