using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

public class InsuranceInfo : ValueObject
{
    public string Provider { get; private set; }
    public string PolicyNumber { get; private set; }
    public string? GroupNumber { get; private set; }
    public DateTime? ExpiryDate { get; private set; }

    private InsuranceInfo() { } // For EF Core

    public InsuranceInfo(string provider, string policyNumber, string? groupNumber = null, DateTime? expiryDate = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Insurance provider cannot be empty", nameof(provider));
        if (string.IsNullOrWhiteSpace(policyNumber))
            throw new ArgumentException("Policy number cannot be empty", nameof(policyNumber));

        Provider = provider;
        PolicyNumber = policyNumber;
        GroupNumber = groupNumber;
        ExpiryDate = expiryDate;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Provider;
        yield return PolicyNumber;
        yield return GroupNumber ?? string.Empty;
        yield return ExpiryDate ?? DateTime.MinValue;
    }
}



