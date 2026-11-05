namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// A non-read action the public demo lets through (<c>demo-cabinet</c> FR-2): entering, keeping the session alive,
/// leaving, and the few POSTs that only read. Everything else that writes is refused with <c>demo_read_only</c>.
/// Grants nothing beyond the demo gate. The reason is mandatory so the allowed set states what each entry is.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowedInDemoAttribute : Attribute
{
    public AllowedInDemoAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une action permise dans la démo doit indiquer sa raison.", nameof(reason));
        }

        Reason = reason;
    }

    public string Reason { get; }
}
