using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// « Tabac » — whether the patient smokes and, if so, how much a day.
///
/// <para>A dental risk factor read at a glance, which is why it is surfaced beside the allergies rather than
/// buried in the free-text antécédents: it bears on healing, on implant survival and on periodontal work, and a
/// paragraph nobody scrolls to is not where that belongs.</para>
///
/// <para>⚠️ <b>Null means the question was never put.</b> <see cref="SmokingStatus.NonSmoker"/> means it was put
/// and the answer was no. Those are different clinical facts and the record must be able to tell them apart —
/// see <see cref="SmokingStatus"/>. Unlike <see cref="CnamInfo"/> there is no <c>IsEmpty</c>: a block cannot
/// exist without a status, so a constructed one always asserts something and « unanswered » is the null.</para>
///
/// <para>⚠️ <b>The quantity is normalised away unless the status is <see cref="SmokingStatus.Smoker"/>.</b>
/// Answering « 20 cigarettes » and then correcting the status to « Non-fumeur » must not leave a stale 20 on the
/// row: a non-smoker who smokes twenty a day is a contradiction the record would state confidently, and no read
/// downstream could tell it from a real answer. The UI withholds the quantity control for the other two statuses
/// for the same reason, but the rule is imposed here so it cannot be forgotten by a second writer.</para>
/// </summary>
public class TobaccoUse : ValueObject
{
    /// <summary>Answered status. Required — a block exists only because somebody answered.</summary>
    public SmokingStatus Status { get; private set; }

    /// <summary>How many per day. Null unless <see cref="Status"/> is <see cref="SmokingStatus.Smoker"/>.</summary>
    public int? PerDay { get; private set; }

    /// <summary>What <see cref="PerDay"/> counts. Null whenever <see cref="PerDay"/> is.</summary>
    public TobaccoUnit? Unit { get; private set; }

    /// <summary>The largest daily quantity a typed answer is accepted for, in either unit.</summary>
    /// <remarks>
    /// A ceiling, not a clinical claim: it exists so a mis-typed « 200 » is refused at the field instead of
    /// printing an implausible figure next to the patient's name. Chosen well above any real answer — ten packs
    /// a day is already far beyond what is recorded in practice.
    /// </remarks>
    public const int MaxPerDay = 200;

    private TobaccoUse() { } // For EF Core

    public TobaccoUse(SmokingStatus status, int? perDay = null, TobaccoUnit? unit = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException("Statut tabagique inconnu.", nameof(status));
        }

        Status = status;

        // Only a smoker carries a quantity — see the class remarks. Silently dropping it is correct here and
        // is not the `SetSteps` trap: the value being dropped contradicts the answer that was just given, rather
        // than being an independent field a caller forgot to pass through.
        if (status != SmokingStatus.Smoker)
        {
            PerDay = null;
            Unit = null;
            return;
        }

        if (perDay is { } quantity)
        {
            if (quantity <= 0 || quantity > MaxPerDay)
            {
                throw new ArgumentException(
                    $"Indiquez une quantité entre 1 et {MaxPerDay} par jour.", nameof(perDay));
            }

            if (unit is not null && !Enum.IsDefined(unit.Value))
            {
                throw new ArgumentException("Unité de consommation inconnue.", nameof(unit));
            }

            PerDay = quantity;
            // A quantity with no unit is unreadable, and cigarettes is what a bare number means at a Tunisian
            // desk — but the unit is still stored explicitly so no reader has to re-derive that assumption.
            Unit = unit ?? TobaccoUnit.Cigarettes;
            return;
        }

        // « Fumeur » with no figure is a real, common answer: the patient smokes and would not say how much.
        // It is recorded as the status alone rather than refused.
        PerDay = null;
        Unit = null;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Status;
        yield return PerDay ?? 0;
        yield return (int?)Unit ?? 0;
    }
}
