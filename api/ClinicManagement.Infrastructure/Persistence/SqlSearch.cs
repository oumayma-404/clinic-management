using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// The SQL half of <see cref="Application.Common.SearchTerm"/>: PostgreSQL's <c>unaccent()</c>, mapped so a
/// repository can push an accent-insensitive <c>ILIKE</c> into the database instead of materialising a clinic's
/// rows and folding accents in C#.
///
/// <para><b>Why a mapped function and not a normalised column.</b> The alternative is a persisted
/// <c>SearchKey</c> column per searchable entity, maintained on write. That is a migration, a backfill and a
/// write-path obligation on eleven aggregates, and every writer that forgets it produces a row that is simply
/// invisible to search — the worst possible failure for a patient lookup, because it looks exactly like the
/// patient not existing. <c>unaccent()</c> at read time cannot be forgotten by a writer.</para>
///
/// <para><b>The cost is honest and worth naming:</b> <c>unaccent(col)</c> is not sargable, so these predicates
/// scan rather than seek. That is still strictly better than what they replace (load every row into the
/// application, then scan in C#), and it stays bounded because every caller now takes a page. If a clinic ever
/// grows past that, the fix is a GIN index on <c>unaccent(col) gin_trgm_ops</c> — which needs <c>unaccent</c>
/// wrapped in an IMMUTABLE function first, since the extension's own is only STABLE.</para>
/// </summary>
public static class SqlSearch
{
    /// <summary>
    /// Placeholder for PostgreSQL's <c>unaccent(text)</c>. Only ever valid inside a LINQ expression tree that
    /// EF translates — calling it in ordinary code is a bug, so it throws rather than returning something
    /// plausible that would silently make an in-memory search accent-sensitive.
    /// </summary>
    public static string? Unaccent(string? value) =>
        throw new InvalidOperationException(
            $"{nameof(SqlSearch)}.{nameof(Unaccent)} is a SQL function mapping and can only be used inside an EF " +
            $"query. For an in-memory match use {nameof(Application.Common.SearchTerm)}.Matches.");

    internal static MethodInfo UnaccentMethod { get; } =
        typeof(SqlSearch).GetMethod(nameof(Unaccent), new[] { typeof(string) })!;

    /// <summary>
    /// Registers the mapping. Called from <c>OnModelCreating</c>.
    ///
    /// <para>Deliberately <b>not</b> schema-qualified: the name resolves through the connection's
    /// <c>search_path</c>, the same way the <c>btree_gist</c> operators the booking constraint depends on do.
    /// Hardcoding <c>public</c> would break any install that relocates extensions.</para>
    /// </summary>
    public static void MapUnaccent(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(UnaccentMethod).HasName("unaccent");
    }

    /// <summary>
    /// The LIKE escape character, as the string <c>ILike</c>'s third argument wants. Matches
    /// <see cref="Application.Common.SearchTerm.LikeEscape"/>, which is what escaped the pattern — pass it, or
    /// the escaping done there is inert and a typed <c>%</c> silently matches every row.
    ///
    /// <para>⚠️ There is no shared "does this column match" helper here on purpose: EF cannot translate a call
    /// to one, so repositories must write the predicate inline. The canonical form is</para>
    /// <code>
    /// EF.Functions.ILike(SqlSearch.Unaccent(p.LastName)!, pattern, SqlSearch.EscapeString)
    /// </code>
    /// </summary>
    public const string EscapeString = "\\";
}
