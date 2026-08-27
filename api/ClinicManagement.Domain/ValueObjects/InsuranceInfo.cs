using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// A patient's private insurance identity — « assurance » and « n° de police ».
///
/// <para>⚠️ <b>One side is enough (AC-21).</b> Both used to be mandatory, which is a rule about a *form* imposed on
/// a *record*: a patient routinely arrives naming their insurer with the card at home, or hands over a policy
/// number on a slip that does not name the company. The client compensated by padding the missing half with the
/// literal <c>"Unknown"</c>, so the screen showed a provider called « Unknown » and no read could tell an unknown
/// insurer from a real one — the same defect as the retired contact sentinels. Blank is stored as blank; what is
/// refused is a value with <b>neither</b> side, which the caller expresses as a null <see cref="InsuranceInfo"/>
/// rather than an empty one.</para>
/// </summary>
public class InsuranceInfo : ValueObject
{
    /// <summary>The insurer. Null when the patient gave only a policy number.</summary>
    public string? Provider { get; private set; }

    /// <summary>The policy number. Null when the patient named only their insurer.</summary>
    public string? PolicyNumber { get; private set; }

    public string? GroupNumber { get; private set; }
    public DateTime? ExpiryDate { get; private set; }

    private InsuranceInfo() { } // For EF Core

    public InsuranceInfo(string? provider, string? policyNumber, string? groupNumber = null, DateTime? expiryDate = null)
    {
        var trimmedProvider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim();
        var trimmedPolicy = string.IsNullOrWhiteSpace(policyNumber) ? null : policyNumber.Trim();

        if (trimmedProvider is null && trimmedPolicy is null)
        {
            throw new ArgumentException(
                "Renseignez au moins l'assurance ou le numéro de police.", nameof(provider));
        }

        Provider = trimmedProvider;
        PolicyNumber = trimmedPolicy;
        GroupNumber = string.IsNullOrWhiteSpace(groupNumber) ? null : groupNumber.Trim();
        ExpiryDate = expiryDate;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Provider ?? string.Empty;
        yield return PolicyNumber ?? string.Empty;
        yield return GroupNumber ?? string.Empty;
        yield return ExpiryDate ?? DateTime.MinValue;
    }
}
