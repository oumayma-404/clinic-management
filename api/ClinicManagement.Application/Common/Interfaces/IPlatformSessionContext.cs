namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// « Which console account is acting? » — the console's analogue of <see cref="IClinicContext"/>.
///
/// <para><b>It exists so the audit actor can be resolved correctly, not merely so a handler can read an id.</b>
/// <c>AuditActorProvider</c> returns the token's <c>sub</c> first, so without this seam a console write would be
/// recorded as a bare GUID — indistinguishable from a clinic user in that cabinet's « Journal d'activité », and
/// invisible to the counter pass's <c>console|</c> exclusion, which would then match nothing. Both failures are
/// silent, which is why the seam lands in Part 1 with the principal rather than in Part 4 with the first write.</para>
///
/// <para>⚠️ <b>A console principal is recognised by a claim, never by the shape of its <c>sub</c>.</b> Both token
/// kinds carry a <c>sub</c>, and a clinic Local id is <c>local|{guid}</c> while a console id is a bare GUID — so
/// « does it parse as a GUID » would classify a Cloud Auth0 <c>sub</c> as neither and a future id format as
/// either. <see cref="TokenKindClaim"/> is emitted only by the console's own issuer.</para>
/// </summary>
public interface IPlatformSessionContext
{
    /// <summary>The claim type marking a token as the console's. Read by this context and written by nothing else.</summary>
    public const string TokenKindClaim = "token_kind";

    /// <summary>The one value <see cref="TokenKindClaim"/> ever carries.</summary>
    public const string PlatformTokenKind = "platform-console";

    /// <summary>
    /// The acting console account's id, or null when the caller is not one (every clinic request, and every
    /// job and console verb, which have no HTTP context at all).
    /// </summary>
    Guid? GetAccountId();

    /// <summary>The acting console account's address, for the ledger's own reading. Null when there is none.</summary>
    string? GetEmail();
}
