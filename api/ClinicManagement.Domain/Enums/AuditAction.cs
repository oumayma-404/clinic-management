namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What happened to the aggregate. Deliberately the three shapes a save can take and nothing more — an audit
/// ledger that tried to name business operations (« facture annulée », « paiement annulé ») would be a second,
/// hand-maintained list of every command in the product, and the first command someone forgot to add to it would
/// be invisible in the one place built to see everything. The <em>what</em> is carried by the entity type plus
/// the changed-field summary; this says only which of the three happened.
/// </summary>
public enum AuditAction
{
    Insert = 0,
    Update = 1,
    Delete = 2,

    /// <summary>
    /// Somebody <b>looked</b> at a patient's record — a radiograph downloaded, a medical document opened.
    ///
    /// <para>⚠️ <b>The one shape a save-interceptor can never see</b>, and the reason it is here despite the class
    /// note above arguing against naming business operations. A read is not a <c>SaveChanges</c>, so it has to be
    /// recorded explicitly by the handler that performs it — the same position <c>ArchiveAccessLedger</c> and
    /// <c>ListExportLedger</c> are already in.</para>
    ///
    /// <para><b>Why it had to exist.</b> The ledger held Insert/Update/Delete alone, so the product could not
    /// answer « qui a ouvert le dossier de ce patient ? » — the first question a regulator or a patient complaint
    /// puts to a medical record, and the only one that matters against a colleague who is *supposed* to have
    /// access. Every write was traceable and every read was invisible.</para>
    ///
    /// <para>⚠️ <b>Deliberately NOT every read.</b> Auditing each screen render would write hundreds of rows a day
    /// per practice and bury the record it exists to make readable — the same argument that keeps
    /// <c>Notification</c> off the interceptor. What is recorded is a patient's <i>content leaving the server</i>:
    /// a file or a document downloaded. Opening the list is not that; taking the x-ray away is.</para>
    /// </summary>
    Read = 3
}
