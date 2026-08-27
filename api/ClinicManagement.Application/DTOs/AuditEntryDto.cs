namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One row of the audit ledger as « Journal d'activité » renders it.
///
/// <para>The French labels are built <b>server-side</b>, the same decision as the « extrait de caisse »: the three
/// actions and the entity types are a closed set, and a client-side map would be a second list to keep in step with
/// the aggregates — the exact drift the interceptor's derived <c>AggregateRoot</c> check avoids on the write side.
/// The raw <see cref="Action"/> and <see cref="EntityType"/> travel too, so a caller can filter and group on stable
/// keys rather than on a translated string.</para>
/// </summary>
public class AuditEntryDto
{
    public Guid Id { get; set; }

    /// <summary>The actor's id — a `User.Id`, or `job|&lt;name&gt;` for a background job or console verb.</summary>
    public string UserId { get; set; } = string.Empty;

    public string? UserEmail { get; set; }

    /// <summary>
    /// Who to show: the email when there is one, « Tâche automatique (StockExpiryJob) » for a process, and the raw
    /// id as the last resort — an account deleted before this row was read has no email to fall back to, and a
    /// visible id is still traceable, whereas « — » is not.
    /// </summary>
    public string ActorLabel { get; set; } = string.Empty;

    /// <summary>True when the actor is a process rather than a person — lets the UI mark the row without parsing.</summary>
    public bool IsSystemActor { get; set; }

    /// <summary>The CLR name of the aggregate (`Patient`, `Invoice`) — a stable key for filtering.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>« Patient », « Note d'honoraires », « Dépense » — the French name for the same thing.</summary>
    public string EntityLabel { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>`Insert` | `Update` | `Delete`.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>« Création » | « Modification » | « Suppression ».</summary>
    public string ActionLabel { get; set; } = string.Empty;

    /// <summary>The compact summary of what moved. Null for a creation.</summary>
    public string? ChangedFields { get; set; }

    public DateTime OccurredAt { get; set; }
}

/// <summary>
/// One page of the ledger, plus the entity types this clinic actually has rows for — what the « Type » filter can
/// offer. Sent with the page rather than fetched separately because the filter is useless without it and a second
/// round trip to populate a dropdown is a second thing that can fail.
/// </summary>
public class AuditPageDto
{
    public List<AuditEntryDto> Items { get; set; } = new();

    /// <summary>
    /// Derived from the rows, never from a hand-kept list of auditable types: the write side audits every
    /// <c>AggregateRoot</c> by construction, so anything else here would be a list to remember to extend.
    /// </summary>
    public List<AuditEntityTypeOptionDto> EntityTypes { get; set; } = new();

    /// <summary>
    /// The actors this clinic has rows for, for the « Auteur » filter. Derived from the ledger like
    /// <see cref="EntityTypes"/>, so a colleague who has left is still selectable — which is when the question is
    /// asked most.
    /// </summary>
    public List<AuditActorOptionDto> Actors { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}

/// <summary>A filterable actor: the stored id plus the name to show for it (an email, or « Tâche automatique »).</summary>
public class AuditActorOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>A filterable entity type: the stable key plus its French name.</summary>
public class AuditEntityTypeOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
