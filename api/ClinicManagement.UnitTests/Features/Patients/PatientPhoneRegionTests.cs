using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// The country selector's choice reaches the server — on create and on edit.
///
/// <para><b>The defect these pin, measured in production on 2026-09-11.</b> A receptionist picked « France » in
/// the patient form's country control, typed <c>06 12 34 56 78</c> — a valid French number, which the browser's
/// own pre-check resolved to <c>+33612345678</c> and accepted — pressed Enregistrer, and was refused with
/// « Numéro de téléphone invalide. Choisissez le pays, ou saisissez le numéro au format international (+33…). »
/// The country control had no effect whatsoever: <c>PhoneNumber.ToE164</c> has taken a <c>region</c> since
/// international-phone-numbers shipped, and <b>not one production call site passed one</b>, so the server went
/// on validating every number against Tunisia while the form offered 250 countries. Re-typing the number as
/// <c>+33 6 12 34 56 78</c> worked, which is the only reason the feature looked half-alive.</para>
///
/// <para>⚠️ <b>Why the existing suite was green throughout.</b> <c>PhoneRuleCorpusTests</c> drives
/// <c>shared/phone-e164-corpus.json</c>, which already contains this exact case
/// (<c>"06 12 34 56 78"</c> + <c>"FR"</c> ⇒ <c>+33612345678</c>) — and it passed, because the <i>rule</i> was
/// never wrong. A corpus pins a function; nothing pinned that the <i>callers</i> hand it the region. These tests
/// are therefore at the entry points and not at the rule, and they are the half the corpus structurally cannot
/// cover.</para>
/// </summary>
public class PatientPhoneRegionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public PatientPhoneRegionTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ------------------------------------------------------------------ create

    /// <summary>The reported refusal. This is the whole bug, at the door it came through.</summary>
    [Fact]
    public void A_French_Number_Is_Accepted_When_The_Form_Says_France()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Camille",
                LastName = "Moreau",
                PhoneNumber = "06 12 34 56 78",
                PhoneRegion = "FR",
            },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// The same number with no region is still refused — and must be. It is not a Tunisian number, and the
    /// default region is the honest answer when nothing says otherwise.
    /// </summary>
    [Fact]
    public void The_Same_Number_With_No_Region_Is_Still_Refused()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Camille",
                LastName = "Moreau",
                PhoneNumber = "06 12 34 56 78",
            },
            ClinicId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PhoneRefusals.Invalid, result.Error);
    }

    /// <summary>Why the user's own workaround worked: an explicit <c>+</c> needs no region at all.</summary>
    [Fact]
    public void An_International_Number_Needs_No_Region()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Camille",
                LastName = "Moreau",
                PhoneNumber = "+33 6 12 34 56 78",
            },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// ⚠️ The rule only ever widened. A Tunisian number sent with no region — every existing caller, the import,
    /// the calendar patient auto-creator — behaves exactly as before.
    /// </summary>
    [Theory]
    [InlineData("20 123 456")]
    [InlineData("+216 20 123 456")]
    public void A_Tunisian_Number_Still_Needs_Nothing(string phone)
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand { FirstName = "Sonia", LastName = "Bel Hadj", PhoneNumber = phone },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// ⚠️ <b>A region widens nothing on its own.</b> Validity stays per-country, so the selector cannot be used
    /// to smuggle a number past the rule — eight Tunisian digits are not a French number, and
    /// <c>201234567</c> is not a Tunisian one however it is labelled.
    /// </summary>
    [Theory]
    [InlineData("20123456", "FR")]
    [InlineData("201234567", "TN")]
    [InlineData("06 12 34 56 78", "US")]
    public void A_Region_Does_Not_Make_A_Bad_Number_Good(string phone, string region)
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Camille",
                LastName = "Moreau",
                PhoneNumber = phone,
                PhoneRegion = region,
            },
            ClinicId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PhoneRefusals.Invalid, result.Error);
    }

    /// <summary>An unknown or junk region falls back to the default rather than throwing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZZ")]
    [InlineData("not-a-region")]
    public void An_Unusable_Region_Falls_Back_To_The_Default(string region)
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Sonia",
                LastName = "Bel Hadj",
                PhoneNumber = "20 123 456",
                PhoneRegion = region,
            },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// ⚠️ <b>Accepting the number is only half of it.</b> The region must reach the CONSTRUCTOR too, or the
    /// patient saves and is then reachable by nothing — `PatientDto.PhoneE164` null, no WhatsApp action, no
    /// reminder. Accepting a number the product cannot dial is the quieter half of the same defect.
    /// </summary>
    [Fact]
    public void An_Accepted_French_Number_Is_Also_Stored_As_Dialable()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Camille",
                LastName = "Moreau",
                PhoneNumber = "06 12 34 56 78",
                PhoneRegion = "FR",
            },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("+33612345678", result.Value.PhoneNumber?.E164);
        // The typed value is kept — reception reads back what it entered.
        Assert.Equal("06 12 34 56 78", result.Value.PhoneNumber?.Value);
    }

    /// <summary>
    /// ⚠️ The emergency contact is deliberately NOT validated and deliberately NOT given the patient's region:
    /// it is a different person's number, so « France » chosen for the patient says nothing about a relative's.
    /// It is stored as typed and is simply not dialable.
    /// </summary>
    [Fact]
    public void An_Emergency_Contact_Is_Stored_As_Typed_And_Never_Refused()
    {
        var result = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Sonia",
                LastName = "Bel Hadj",
                EmergencyContactName = "Voisine",
                EmergencyContactPhone = "71 555 (bureau)",
            },
            ClinicId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("71 555 (bureau)", result.Value.EmergencyContactPhone?.Value);
        Assert.Null(result.Value.EmergencyContactPhone?.E164);
    }

    // ------------------------------------------------------------------ update

    /// <summary>
    /// ⚠️ The edit path was refused for exactly the same reason the create path was, and it is a separate call
    /// site — this is the repo's « a fix wired to one call site » shape, so both are pinned.
    /// </summary>
    [Fact]
    public async Task Editing_A_Patient_To_A_French_Number_Is_Accepted()
    {
        var patient = ExistingPatient();

        var result = await UpdateAsync(new UpdatePatientCommand
        {
            Id = patient.Id,
            PhoneNumber = "06 12 34 56 78",
            PhoneRegion = "FR",
        });

        Assert.True(result.IsSuccess, result.Error);
        // Stored dialable, not merely accepted — and the DTO reports the stored form.
        Assert.Equal("+33612345678", patient.PhoneNumber?.E164);
        Assert.Equal("+33612345678", result.Value.PhoneE164);
    }

    /// <summary>
    /// ⚠️ An edit that does not mention the phone leaves the stored normalisation exactly as it was — it
    /// neither re-derives it (which would lose a foreign number's country) nor drops it.
    /// </summary>
    [Fact]
    public async Task An_Edit_That_Ignores_The_Phone_Leaves_Its_Normalisation_Alone()
    {
        var patient = ExistingPatient();
        var before = patient.PhoneNumber?.E164;

        var result = await UpdateAsync(new UpdatePatientCommand { Id = patient.Id, FirstName = "Camille-Anne" });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(before, patient.PhoneNumber?.E164);
    }

    [Fact]
    public async Task Editing_A_Patient_To_That_Number_Without_A_Region_Is_Refused()
    {
        var patient = ExistingPatient();

        var result = await UpdateAsync(new UpdatePatientCommand
        {
            Id = patient.Id,
            PhoneNumber = "06 12 34 56 78",
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(PhoneRefusals.Invalid, result.Error);
    }

    /// <summary>
    /// ⚠️ <c>PhoneRegion</c> is deliberately <b>not</b> tri-state, unlike every other field on the update
    /// command: nothing stores it, so it cannot be « left alone ». Setting it must not make the command think a
    /// phone number was supplied — that would drag the stored number into a request that never mentioned it.
    /// </summary>
    [Fact]
    public void A_Region_Alone_Does_Not_Count_As_Supplying_A_Number()
    {
        var command = new UpdatePatientCommand { Id = Guid.NewGuid(), PhoneRegion = "FR" };

        Assert.False(command.PhoneNumberSpecified);
    }

    private Patient ExistingPatient()
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Camille", "Moreau",
            new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), "F", null,
            new PhoneNumber("20 123 456"));

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
