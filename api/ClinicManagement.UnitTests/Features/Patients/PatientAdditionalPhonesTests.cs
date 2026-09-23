using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// A patient carries more than one number — « le portable du conjoint », « le fixe de la maison » — and the
/// list survives every ordinary save.
///
/// <para><b>What these are really guarding.</b> <c>Patient.SetAdditionalPhoneNumbers</c> replaces the WHOLE
/// list, which is the shape this codebase has paid for three times over (<c>DentalRecord.SetActs</c>:
/// <c>PonticToothNumbers</c>, <c>ImplantPilierToothNumbers</c>, <c>IsUnfinished</c>). The only thing standing
/// between that setter and « reopening a fiche to fix a typo silently deleted three numbers » is the command's
/// tri-state: an omitted key must never reach the setter. <see cref="An_Update_That_Does_Not_Mention_Them_Leaves_Them_Alone"/>
/// is therefore the most important test in this file, not the least.</para>
/// </summary>
public class PatientAdditionalPhonesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public PatientAdditionalPhonesTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ------------------------------------------------------------------ the value object

    /// <summary>Each row resolves against its OWN country — the whole reason the region is per row.</summary>
    [Fact]
    public void A_Row_Resolves_Against_Its_Own_Country()
    {
        var tunisian = new PatientPhone("20 123 456", "TN", 0);
        var french = new PatientPhone("06 12 34 56 78", "FR", 1);

        Assert.Equal("+21620123456", tunisian.E164);
        Assert.Equal("+33612345678", french.E164);
        // The typed value is kept — reception reads back what it entered.
        Assert.Equal("06 12 34 56 78", french.Value);
    }

    /// <summary>
    /// The constructor refuses what it cannot dial, so <c>E164</c> can be non-nullable — which is what lets
    /// every reader dial a row of this table without the legacy fallback <c>PhoneNumber.E164</c> carries.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("71 555 (bureau)")]
    [InlineData("201234567")]
    public void An_Undialable_Row_Cannot_Be_Constructed(string value)
    {
        Assert.Throws<ArgumentException>(() => new PatientPhone(value, "TN"));
    }

    // ------------------------------------------------------------------ the mapping

    /// <summary>
    /// A blank row is DROPPED, never refused. « Ajouter un numéro » appends one and a user who changes their
    /// mind leaves it there; refusing the save over it would make the button a trap.
    /// </summary>
    [Fact]
    public void A_Blank_Row_Is_Dropped_Not_Refused()
    {
        var result = PatientPhoneMapping.Build(
            new List<PatientPhoneInputDto>
            {
                new() { Value = "20 123 456", Region = "TN" },
                new() { Value = "   " },
                new() { Value = null },
            },
            primary: null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Value!);
        // The kept row's rank counts the rows KEPT, so dropping a blank leaves no hole.
        Assert.Equal(0, result.Value![0].SortOrder);
    }

    /// <summary>A row with something in it must be a real number — the primary's rule, applied per row.</summary>
    [Fact]
    public void A_Row_That_Is_Not_A_Number_Is_Refused()
    {
        var result = PatientPhoneMapping.Build(
            new List<PatientPhoneInputDto> { new() { Value = "06 12 34 56 78", Region = "TN" } },
            primary: null);

        Assert.False(result.IsSuccess);
        Assert.Equal(PhoneRefusals.Invalid, result.Error);
    }

    /// <summary>
    /// Re-typing the main number into « autres numéros » is the ordinary mistake at a desk, and storing it
    /// twice makes the fiche read as though the patient has two lines. Compared on E.164, so
    /// « 20 123 456 » and « +216 20 123 456 » are one number.
    /// </summary>
    [Fact]
    public void A_Duplicate_Of_The_Primary_Or_Of_Another_Row_Is_Dropped()
    {
        var result = PatientPhoneMapping.Build(
            new List<PatientPhoneInputDto>
            {
                new() { Value = "+216 20 123 456", Region = "TN" },
                new() { Value = "71 234 567", Region = "TN" },
                new() { Value = "71234567", Region = "TN" },
            },
            primary: new PhoneNumber("20 123 456", "TN"));

        Assert.True(result.IsSuccess, result.Error);
        var kept = Assert.Single(result.Value!);
        // The one that survived is the SECOND row — the first duplicated the primary.
        Assert.Equal("+21671234567", kept.E164);
    }

    /// <summary>The cap is the server's, not the form's — a scripted caller cannot grow the row without bound.</summary>
    [Fact]
    public void More_Than_The_Cap_Is_Refused()
    {
        var rows = new[] { "20 123 451", "20 123 452", "20 123 453", "20 123 454", "20 123 455", "20 123 456" }
            .Select(v => new PatientPhoneInputDto { Value = v, Region = "TN" })
            .ToList();

        Assert.Equal(Patient.MaxAdditionalPhoneNumbers + 1, rows.Count);

        var result = PatientPhoneMapping.Build(rows, primary: null);

        Assert.False(result.IsSuccess);
        Assert.Contains(Patient.MaxAdditionalPhoneNumbers.ToString(), result.Error);
    }

    // ------------------------------------------------------------------ create

    [Fact]
    public void A_New_Patient_Keeps_The_Numbers_The_Form_Sent()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Sonia",
                LastName = "Bel Hadj",
                PhoneNumber = "20 123 456",
                PhoneRegion = "TN",
                AdditionalPhones = new List<PatientPhoneInputDto>
                {
                    new() { Value = "71 234 567", Region = "TN" },
                    new() { Value = "06 12 34 56 78", Region = "FR" },
                },
            },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
        var phones = result.Value!.AdditionalPhoneNumbers.OrderBy(p => p.SortOrder).ToList();
        Assert.Equal(2, phones.Count);
        Assert.Equal("+21671234567", phones[0].E164);
        Assert.Equal("+33612345678", phones[1].E164);
    }

    /// <summary>Nearly every patient has none, and the CSV import sends none — that must not be an error.</summary>
    [Fact]
    public void A_New_Patient_Without_Any_Is_Ordinary()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand { FirstName = "Ali", LastName = "Trabelsi", PhoneNumber = "20 123 456" },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.AdditionalPhoneNumbers);
    }

    // ------------------------------------------------------------------ update (tri-state)

    /// <summary>
    /// ⚠️ <b>The one that matters.</b> The setter replaces the whole list, so any caller that does not mention
    /// these numbers — the calendar-import review save, a partial PATCH, any future surface — would erase them
    /// all. The tri-state is the only thing preventing it, and « the handler compiles » does not prove it.
    /// </summary>
    [Fact]
    public async Task An_Update_That_Does_Not_Mention_Them_Leaves_Them_Alone()
    {
        var patient = ExistingPatientWithTwoExtraNumbers();

        var result = await UpdateAsync(new UpdatePatientCommand { Id = patient.Id, FirstName = "Camille-Marie" });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, patient.AdditionalPhoneNumbers.Count);
        Assert.Equal(2, result.Value!.AdditionalPhones.Count);
    }

    /// <summary>An empty list is how the last number is deleted — the other half of the tri-state.</summary>
    [Fact]
    public async Task An_Empty_List_Clears_Them()
    {
        var patient = ExistingPatientWithTwoExtraNumbers();

        var result = await UpdateAsync(new UpdatePatientCommand
        {
            Id = patient.Id,
            AdditionalPhones = new List<PatientPhoneInputDto>(),
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(patient.AdditionalPhoneNumbers);
        Assert.Empty(result.Value!.AdditionalPhones);
    }

    [Fact]
    public async Task A_Sent_List_Replaces_What_Was_Stored()
    {
        var patient = ExistingPatientWithTwoExtraNumbers();

        var result = await UpdateAsync(new UpdatePatientCommand
        {
            Id = patient.Id,
            AdditionalPhones = new List<PatientPhoneInputDto>
            {
                new() { Value = "98 765 432", Region = "TN" },
            },
        });

        Assert.True(result.IsSuccess, result.Error);
        var kept = Assert.Single(patient.AdditionalPhoneNumbers);
        Assert.Equal("+21698765432", kept.E164);
    }

    /// <summary>
    /// ⚠️ The dedupe compares against the NEW primary when a request changes both — which is why the
    /// additional-phones block sits after the contact block in the handler and not before it.
    /// </summary>
    [Fact]
    public async Task Changing_The_Primary_And_The_List_Together_Dedupes_Against_The_New_Primary()
    {
        var patient = ExistingPatientWithTwoExtraNumbers();

        var result = await UpdateAsync(new UpdatePatientCommand
        {
            Id = patient.Id,
            PhoneNumber = "98 765 432",
            PhoneRegion = "TN",
            AdditionalPhones = new List<PatientPhoneInputDto>
            {
                new() { Value = "98 765 432", Region = "TN" },
                new() { Value = "71 234 567", Region = "TN" },
            },
        });

        Assert.True(result.IsSuccess, result.Error);
        // The row equal to the NEW primary is dropped; the other survives.
        var kept = Assert.Single(patient.AdditionalPhoneNumbers);
        Assert.Equal("+21671234567", kept.E164);
    }

    /// <summary>A bad row comes back as the French refusal, not as the constructor's exception.</summary>
    [Fact]
    public async Task A_Bad_Row_Is_Refused_With_The_French_Sentence()
    {
        var patient = ExistingPatientWithTwoExtraNumbers();

        var result = await UpdateAsync(new UpdatePatientCommand
        {
            Id = patient.Id,
            AdditionalPhones = new List<PatientPhoneInputDto> { new() { Value = "abc", Region = "TN" } },
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(PhoneRefusals.Invalid, result.Error);
        // ⚠️ And nothing was written: the refusal happens before the setter, so the stored list is intact.
        Assert.Equal(2, patient.AdditionalPhoneNumbers.Count);
    }

    /// <summary>A region alone must not make the command think a list was supplied — the `Specified` contract.</summary>
    [Fact]
    public void An_Untouched_Command_Reports_The_List_As_Unspecified()
    {
        var command = new UpdatePatientCommand { Id = Guid.NewGuid(), PhoneRegion = "FR" };

        Assert.False(command.AdditionalPhonesSpecified);
    }

    // ------------------------------------------------------------------ the read

    /// <summary>The DTO carries them back in the practice's own order, with the dialable form on every row.</summary>
    [Fact]
    public void The_Dto_Carries_Them_Back_In_Order()
    {
        var patient = ExistingPatientWithTwoExtraNumbers();

        var dto = patient.ToDto();

        Assert.Equal(new[] { "71 234 567", "06 12 34 56 78" }, dto.AdditionalPhones.Select(p => p.Value).ToArray());
        Assert.Equal(new[] { "+21671234567", "+33612345678" }, dto.AdditionalPhones.Select(p => p.E164).ToArray());
    }

    private Patient ExistingPatientWithTwoExtraNumbers()
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Camille", "Moreau",
            new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), "F", null,
            new PhoneNumber("20 123 456", "TN"));

        patient.SetAdditionalPhoneNumbers(new[]
        {
            new PatientPhone("71 234 567", "TN", 0),
            new PatientPhone("06 12 34 56 78", "FR", 1),
        });

        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        return patient;
    }

    private async Task<Result<PatientDto>> UpdateAsync(UpdatePatientCommand command)
    {
        var handler = new UpdatePatientCommandHandler(
            _patients.Object, _clinicResolver.Object, _uow.Object, new Mock<IClinicContext>().Object);
        return await handler.Handle(command, CancellationToken.None);
    }
}
