using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

public class PhoneNumber : ValueObject
{
    public string Value { get; private set; }

    private PhoneNumber() { } // For EF Core

    public PhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));

        Value = phoneNumber.Trim();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}



