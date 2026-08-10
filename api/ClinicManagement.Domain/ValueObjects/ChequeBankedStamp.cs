using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// That a cheque has been taken to the bank, when, and by whom (Group B) — <see cref="ChequeDetails"/>'s sibling,
/// under the same one-guard rule.
///
/// <para><b>What it is not.</b> It is <b>not a money movement</b>. La caisse counts a cheque on the day it was
/// received, never on the day it cleared, and Group B deliberately does not change that: « Encaissé » is what the
/// clinic collected, and re-dating it on banking would move every historical figure the practice has already read.
/// So nothing here touches an amount, a status or a total — it answers « which of the cheques in my drawer have I
/// actually taken to the bank? », which no screen could previously ask.</para>
///
/// <para><b>Why a value object rather than three loose columns.</b> Exactly <see cref="ChequeDetails"/>'s reason:
/// two ledgers carry the same three fields under the same invariant — a banked stamp belongs only to a cheque —
/// and the devis→facture bridge has to move one across. With the guard at each call site, the next write path is
/// the one that forgets.</para>
///
/// <para>⚠️ Flattened into three plain nullable columns like its sibling, not mapped as an EF-owned type: the
/// banked date is filtered on by itself, and an owned type inside an aggregate child makes every projection nest
/// for nothing.</para>
/// </summary>
public sealed class ChequeBankedStamp : ValueObject
{
    /// <summary>When the cheque was marked as banked — a moment, not a money date.</summary>
    public DateTime BankedOn { get; }

    /// <summary>Who marked it. A soft link, no foreign key: <c>User.Id</c> is a string and users get deactivated.</summary>
    public string? ByUserId { get; }

    /// <summary>Name snapshot of the actor, so reading the trail needs no user lookup.</summary>
    public string? ByName { get; }

    private ChequeBankedStamp(DateTime bankedOn, string? byUserId, string? byName)
    {
        BankedOn = bankedOn;
        ByUserId = byUserId;
        ByName = byName;
    }

    /// <summary>
    /// The stamp for a payment made by <paramref name="method"/>, or <c>null</c> when the cheque is still held.
    ///
    /// <para>The actor is <b>optional</b>, like every other trail in this product: a missing user must never block
    /// a correction (the void commands resolve the name best-effort for the same reason). The date is not — a stamp
    /// that cannot say when is not a stamp.</para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// When a stamp is supplied for a method that is not <see cref="PaymentMethod.Cheque"/>. Espèces are already in
    /// the drawer and a card or a transfer settles itself, so the mark would describe nothing — and it would put a
    /// row in a « chèques » view that is not a cheque.
    /// </exception>
    public static ChequeBankedStamp? For(PaymentMethod method, DateTime? bankedOn, string? byUserId, string? byName)
    {
        if (bankedOn is not { } moment)
        {
            return null;
        }

        if (method != PaymentMethod.Cheque)
        {
            throw new ArgumentException(
                "Seul un règlement par chèque peut être marqué comme encaissé en banque.",
                nameof(method));
        }

        return new ChequeBankedStamp(moment, Normalize(byUserId), Normalize(byName));
    }

    /// <summary>Blank is blank — an unresolved actor name must not store an empty string a read then has to test for.</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return BankedOn;
        yield return ByUserId ?? string.Empty;
        yield return ByName ?? string.Empty;
    }
}
