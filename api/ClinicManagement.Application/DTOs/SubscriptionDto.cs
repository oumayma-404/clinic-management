namespace ClinicManagement.Application.DTOs;

/// <summary>
/// What « Abonnement » tells a cabinet about where it stands and how to pay (US-2, AC-2.1). Readable by
/// <b>every</b> role (AC-2.2) and reachable on an expired cabinet (AC-4.8) — it is the one screen that says what
/// to do about the refusal.
///
/// <para><b>Nothing here is stored.</b> The state, the countdown and both booleans come out of
/// <c>SubscriptionStateReader</c> — the single FR-1 rule the gate, the banner, the warning job and the vendor
/// verbs all read — so the sentence a cabinet reads here and the refusal it meets on a save cannot disagree.</para>
/// </summary>
public class SubscriptionDto
{
    /// <summary>`Trial` | `Active` | `Expired` | `Suspended`. Derived, never stored (FR-1).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>« Essai gratuit » | « Actif » | « Expiré » | « Suspendu » — the state's own French name.</summary>
    public string StateLabel { get; set; } = string.Empty;

    /// <summary>The forfait, or null — the ordinary state of a cabinet on its free days and of a grandfathered one.</summary>
    public string? Plan { get; set; }

    /// <summary>The forfait's French name, or null with no forfait. Never invented — see <see cref="Plans"/>.</summary>
    public string? PlanLabel { get; set; }

    /// <summary>
    /// The <b>inclusive</b> last day new work may be recorded, or null for « sans échéance » (AC-2.5).
    ///
    /// <para>⚠️ Null is a real state the screen must render <b>in words</b>, not as a far-future date: a
    /// grandfathered or complimentary cabinet has no end date at all, and « 31/12/9999 » is a sentence nobody can
    /// act on.</para>
    /// </summary>
    public DateTime? EndsOn { get; set; }

    /// <summary>
    /// Whole clinic-local days left, <b>0 on the last working day</b> (the cabinet may work all of
    /// <see cref="EndsOn"/>). Null when there is no end date, and null once the date has passed — a negative
    /// countdown is never surfaced.
    /// </summary>
    public int? DaysRemaining { get; set; }

    public bool AllowsWrites { get; set; }

    /// <summary>True from <c>SubscriptionStateReader.WarningWindowDays</c> before the end (AC-3.1's banner window).</summary>
    public bool ShouldWarn { get; set; }

    /// <summary>Why the vendor stopped this cabinet. Set only in the <c>Suspended</c> state (EC-11).</summary>
    public string? SuspensionReason { get; set; }

    /// <summary>The cabinet's own forfait's monthly price, or null when it has chosen none or none is published.</summary>
    public decimal? PriceMonthlyDt { get; set; }

    /// <summary>Its annual price. Not derived from the monthly one — an annual rate is a discount (FR-10).</summary>
    public decimal? PriceAnnualDt { get; set; }

    /// <summary>
    /// The deployment's whole published tariff, one row per forfait, in enum order.
    ///
    /// <para><b>Why it is here as well as the two fields above.</b> A cabinet on its free days and every
    /// grandfathered one has <see cref="Plan"/> null, so those two fields are null for exactly the readers deciding
    /// whether to pay — and AC-2.1 requires the screen to show the price. The pair answers « what am I paying? »
    /// and this answers « what would I pay? »; neither can stand in for the other.</para>
    ///
    /// <para>Every forfait is listed even where no figure is published, so « Sur-mesure — sur devis » is a
    /// statement rather than an absent row. Prices are operator configuration (AC-2.4), so this is empty on a
    /// deployment that has not filled the section in.</para>
    /// </summary>
    public List<SubscriptionPlanPriceDto> Plans { get; set; } = new();

    /// <summary>
    /// How to pay, in French, from per-deployment configuration (AC-2.4).
    ///
    /// <para>⚠️ <b>The reason the screen exists</b>, so it is never behind a disclosure client-side. Null where the
    /// deployment has published none, which the screen states as such rather than leaving blank.</para>
    /// </summary>
    public string? PaymentInstructions { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }
}

/// <summary>
/// One forfait of the published tariff: a label and a price, and it <b>gates nothing</b> (FR-10) — every capability
/// is available on every plan.
/// </summary>
public class SubscriptionPlanPriceDto
{
    /// <summary>The stable wire value (`Cabinet` | `Clinique` | `SurMesure`), for comparing against <see cref="SubscriptionDto.Plan"/>.</summary>
    public string Plan { get; set; } = string.Empty;

    /// <summary>Its French name — « Sur-mesure », not `SurMesure`.</summary>
    public string Label { get; set; } = string.Empty;

    public decimal? PriceMonthlyDt { get; set; }

    public decimal? PriceAnnualDt { get; set; }
}
