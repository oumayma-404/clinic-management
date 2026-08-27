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

    /// <summary>
    /// Name the <b>person</b> whose credentials this scope has just verified, for a request that has no token yet.
    ///
    /// <para>⚠️ <b>Sign-in is the case, and it is not a corner one.</b> <c>POST /api/auth/login</c> is anonymous by
    /// construction — the token is its <i>output</i> — so <see cref="IClinicContext"/> answers null, and the save
    /// that stamps <c>LastLoginAt</c> was therefore attributed to <c>job|unknown</c> and rendered
    /// « Tâche automatique ». 329 of 1 868 journal rows, ~18 %, asserted that a process did what a person did, on
    /// the ledger an owner would reach for after anything else went wrong.</para>
    ///
    /// <para>Deliberately NOT <see cref="RunAs"/>: that one names a process and is ignored while a real user is in
    /// scope. This names a real user and is ignored once anything else has been resolved — same first-read-wins
    /// rule, so it can never overwrite a token-bearing caller.</para>
    /// </summary>
    void AuthenticatedAs(string userId, string? email);

    /// <summary>
    /// Mark this scope's rows as written by an <b>archive restore</b> rather than by hand
    /// (<c>clinic-data-archive-and-restore</c> AC-9).
    ///
    /// <para><b>Why not <see cref="RunAs"/>.</b> That one is deliberately ignored while a real user is in scope,
    /// and a restore always has one — an admin clicked it, or a console account did. Losing that identity would be
    /// the wrong trade: re-inserting three thousand patient rows is exactly the operation an owner needs to be able
    /// to attribute to a person afterwards. So this <i>decorates</i> whoever is in scope rather than replacing them,
    /// through <see cref="AuditActor.AsRestore"/>.</para>
    ///
    /// <para>Without it a restore is indistinguishable from mass data entry in « Journal d'activité »: three
    /// thousand <c>Insert</c> rows against a named colleague, on a day they typed nothing.</para>
    /// </summary>
    void RestoringAnArchive();
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
    /// The prefix marking a row an <b>archive restore</b> re-inserted rather than anyone typed
    /// (<c>clinic-data-archive-and-restore</c> AC-9).
    ///
    /// <para><b>A decoration, not a fourth kind.</b> It wraps whichever identity was already in scope — a clinic
    /// admin, or the vendor's console — so « qui a restauré ? » stays answerable while « ces trois mille fiches
    /// ont-elles été saisies ? » answers no. A restore that erased the actor would trade the second question's
    /// answer for the first's, and the ledger needs both.</para>
    /// </summary>
    public const string RestorePrefix = "restore|";

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

    /// <summary>
    /// This same actor, marked as restoring an archive. Idempotent, so a nested declaration cannot produce
    /// <c>restore|restore|…</c>. See <see cref="RestorePrefix"/>.
    /// </summary>
    public AuditActor AsRestore() =>
        IsRestore ? this : new($"{RestorePrefix}{UserId}", Email);

    /// <summary>True when this is a process rather than a person — what lets the read side label the row.</summary>
    public bool IsProcess => UserId.StartsWith(ProcessPrefix, StringComparison.Ordinal);

    /// <summary>True when an archive restore wrote this row, rather than anybody entering it.</summary>
    public bool IsRestore => UserId.StartsWith(RestorePrefix, StringComparison.Ordinal);

    /// <summary>True when the vendor's console wrote this row, rather than anyone at the cabinet.</summary>
    public bool IsConsole => UserId.StartsWith(ConsolePrefix, StringComparison.Ordinal);
}
