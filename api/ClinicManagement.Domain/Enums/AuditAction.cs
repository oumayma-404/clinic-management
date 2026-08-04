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
    Delete = 2
}
