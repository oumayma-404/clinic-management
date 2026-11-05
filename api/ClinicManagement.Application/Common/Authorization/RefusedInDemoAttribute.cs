namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// A read the public demo refuses (<c>demo-cabinet</c> FR-2): it hands data out of the demo (a CSV, a dossier, the
/// archive) or completes a write begun elsewhere (an OAuth callback). The reason is mandatory.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RefusedInDemoAttribute : Attribute
{
    public RefusedInDemoAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Une lecture refusée dans la démo doit indiquer sa raison.", nameof(reason));
        }

        Reason = reason;
    }

    public string Reason { get; }
}
