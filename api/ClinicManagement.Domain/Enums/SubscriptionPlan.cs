namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Which forfait a cabinet is on (FR-10) — <b>a label and a price, and it gates nothing</b>. Every capability of
/// the product is available on every plan; what a subscription decides is whether new work may be recorded at all,
/// never which features are reachable.
/// </summary>
public enum SubscriptionPlan
{
    Cabinet = 1,
    Clinique = 2,
    SurMesure = 3
}
