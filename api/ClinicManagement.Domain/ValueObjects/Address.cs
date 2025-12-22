using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

public class Address : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }
    public string ZipCode { get; private set; }
    public string? Country { get; private set; }

    private Address() { } // For EF Core

    public Address(string street, string city, string state, string zipCode, string? country = null)
    {
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street cannot be empty", nameof(street));
        if (string.IsNullOrWhiteSpace(city))
            throw new ArgumentException("City cannot be empty", nameof(city));
        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("State cannot be empty", nameof(state));
        if (string.IsNullOrWhiteSpace(zipCode))
            throw new ArgumentException("Zip code cannot be empty", nameof(zipCode));

        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return ZipCode;
        yield return Country ?? string.Empty;
    }
}



