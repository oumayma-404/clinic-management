namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// This read is refused (403 <c>money_hidden</c>) while the cabinet's « Mode discret » is on. Read from endpoint
/// metadata by the API's <c>MoneyMaskGateMiddleware</c>; it never widens authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class HiddenWhenMoneyMaskedAttribute : Attribute
{
    /// <summary>
    /// A query parameter that, when present, scopes the read to one patient and lets it through — the same route
    /// serves the clinic's whole ledger and one patient's file (<c>GET /api/invoices?patientId=</c>).
    /// </summary>
    public string? UnlessQuery { get; init; }
}
