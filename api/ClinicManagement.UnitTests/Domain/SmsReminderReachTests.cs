using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Who an SMS reminder can reach — a Tunisian mobile only, the primary first and then an extra number — and what the
/// reminder says the séance is for.
/// </summary>
public class SmsReminderReachTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData("+21620123456")] // Ooredoo
    [InlineData("+21655123456")] // Orange
    [InlineData("+21698123456")] // Tunisie Telecom
    [InlineData("+21629123456")]
    [InlineData("+21641123456")]
    public void A_Tunisian_Mobile_Is_Reachable_By_Sms(string e164) =>
        Assert.True(PhoneNumber.IsTunisianMobile(e164));

    [Theory]
    [InlineData("+21671123456")] // Tunis landline
    [InlineData("+21674123456")] // Sfax landline
    [InlineData("+33612345678")] // French mobile
    [InlineData("+212612345678")] // Moroccan mobile
    [InlineData("")]
    [InlineData(null)]
    [InlineData("71 555 (bureau)")]
    public void A_Landline_A_Foreign_Number_Or_Nothing_Is_Not(string? e164) =>
        Assert.False(PhoneNumber.IsTunisianMobile(e164));

    private static Patient PatientWith(string primary, params string[] extras)
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Sami", "Ben Ali",
            new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            phoneNumber: new PhoneNumber(primary));
        patient.SetAdditionalPhoneNumbers(extras.Select((number, index) => new PatientPhone(number, sortOrder: index)));
        return patient;
    }

    [Fact]
    public void The_Primary_Is_Used_When_It_Is_A_Tunisian_Mobile() =>
        Assert.Equal("+21620123456", PatientWith("20 123 456", "55 123 456").SmsNumberE164());

    [Fact]
    public void A_Landline_Primary_Falls_Back_To_An_Extra_Mobile() =>
        Assert.Equal("+21655123456", PatientWith("71 123 456", "55 123 456").SmsNumberE164());

    [Fact]
    public void The_Extra_Numbers_Are_Tried_In_Their_Order() =>
        Assert.Equal("+21698000111", PatientWith("71 123 456", "74 000 111", "98 000 111", "20 000 111").SmsNumberE164());

    [Fact]
    public void A_Foreign_Mobile_Primary_With_No_Tunisian_Mobile_Gets_No_Sms() =>
        Assert.Null(PatientWith("+33 6 12 34 56 78", "71 123 456").SmsNumberE164());

    [Fact]
    public void The_Acts_Of_A_Seance_Are_Named_In_Booking_Order_Each_Once()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, Guid.NewGuid(), null,
            new DateTime(2026, 10, 21, 13, 30, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(60));
        appointment.SetProcedures(
        [
            new AppointmentProcedureInput(Guid.NewGuid(), "Détartrage", 30, null, null, null),
            new AppointmentProcedureInput(Guid.NewGuid(), "Obturation", 30, null, null, null),
            new AppointmentProcedureInput(Guid.NewGuid(), "détartrage", 15, null, null, null),
        ]);

        Assert.Equal(["Détartrage", "Obturation"], appointment.BookedActNames());
    }

    [Fact]
    public void A_Seance_With_No_Act_Names_None()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, Guid.NewGuid(), null,
            new DateTime(2026, 10, 21, 13, 30, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

        Assert.Empty(appointment.BookedActNames());
    }
}
