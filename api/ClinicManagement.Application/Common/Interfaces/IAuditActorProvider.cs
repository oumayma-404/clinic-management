namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Who to stamp on the audit rows written in this scope.
///
/// <para><b>Why the audit interceptor does not just read <see cref="IClinicContext"/>.</b> It would work for
/// every HTTP request and produce nothing at all for the writes that most need attributing: the Hangfire jobs and
/// the console verbs run with no <c>HttpContext</c>, so the claims are empty and the row would either be dropped
/// or say « unknown » about a mutation whose cause is perfectly well known. The spec's rule is that a job writes
/// the row under <b>the job's name</b> rather than skipping it, and something has to be able to say that name.
/// </para>
///
/// <para>So: a request resolves itself from the token with no help, and a non-interactive caller declares itself
/// once with <see cref="RunAs"/>. Scoped, so a job that forgot to declare cannot leak its identity into the next
/// one — the fallback is <c>job|unknown</c>, which is honest, rather than the previous job's name, which is a
/// lie.</para>
/// </summary>
public interface IAuditActorProvider
{
    /// <summary>The actor for rows written in this scope. Never null — see <see cref="AuditActor"/>.</summary>
    AuditActor Current { get; }

    /// <summary>
    /// Name a non-interactive caller — a background job, a console verb — as the actor for this scope. Called once
    /// at the top of the job's work, before anything is saved. Ignored when a real user is already in scope: a
    /// signed-in caller's identity must not be overwritten by a helper that happens to run inside their request.
    /// </summary>
    void RunAs(string processName);
}

/// <summary>
/// The actor's identity as the ledger records it. <see cref="UserId"/> is a <c>User.Id</c> for a person, or
/// <c>job|&lt;name&gt;</c> for a process; <see cref="Email"/> is present only when the token carried one.
/// </summary>
public readonly record struct AuditActor(string UserId, string? Email)
{
    /// <summary>The prefix that distinguishes a process from a person. `User.Id` can never collide with it —
    /// Cloud ids are Auth0 <c>sub</c>s and Local ids are minted as <c>local|{guid}</c>.</summary>
    public const string ProcessPrefix = "job|";

    /// <summary>
    /// The prefix that distinguishes the <b>vendor's console</b> from both a clinic user and a background job
    /// (<c>platform-console</c> AC-4.7, AC-2.2/EC-10).
    ///
    /// <para><b>A third kind, not a flavour of <see cref="ProcessPrefix"/>.</b> A console write has a real human
    /// behind it and must be attributable to that account in the affected cabinet's own « Journal d'activité » —
    /// <c>job|…</c> would say « une tâche automatique » about something a person did. And it must be <i>excluded</i>
    /// from that cabinet's activity counters, which a clinic user id would not be: granting a dormant cabinet a
    /// subscription would otherwise make it read as active the next morning, on exactly the cabinet the « dormant »
    /// filter surfaced.</para>
    ///
    /// <para>⚠️ <b>Both the writer and the counter pass's exclusion read this constant</b>, never a retyped
    /// <c>"console|"</c> literal. A second copy of a prefix is a filter that keeps passing while the writer moves —
    /// the <c>fixes-dont-propagate</c> shape.</para>
    /// </summary>
    public const string ConsolePrefix = "console|";

    /// <summary>
    /// What a mutation with neither a token nor a declared process name is recorded as. Deliberately a value and
    /// not a null: the row exists either way, and « we do not know » is information, whereas a missing row is
    /// indistinguishable from nothing having happened.
    /// </summary>
    public static AuditActor Unknown { get; } = new($"{ProcessPrefix}unknown", null);

    public static AuditActor Process(string processName) =>
        new($"{ProcessPrefix}{(string.IsNullOrWhiteSpace(processName) ? "unknown" : processName.Trim())}", null);

    /// <summary>The vendor's console acting as <paramref name="accountId"/>. See <see cref="ConsolePrefix"/>.</summary>
    public static AuditActor Console(Guid accountId, string? email = null) =>
        new($"{ConsolePrefix}{accountId}", email);

    /// <summary>True when this is a process rather than a person — what lets the read side label the row.</summary>
    public bool IsProcess => UserId.StartsWith(ProcessPrefix, StringComparison.Ordinal);

    /// <summary>True when the vendor's console wrote this row, rather than anyone at the cabinet.</summary>
    public bool IsConsole => UserId.StartsWith(ConsolePrefix, StringComparison.Ordinal);
}
