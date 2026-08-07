using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Creating the same patient twice is refused, whichever door they come in through.
///
/// <para><b>The report.</b> A receptionist booking an appointment used the dialog's « Nouveau patient » switch and
/// afterwards found the person listed twice on <c>/patients</c>. Two independent defects produced that, and both are
/// covered here plus in the client:</para>
/// <list type="number">
///   <item>The appointment dialog's <c>performCreate</c> re-ran <c>patientsApi.create</c> on every retry, and it is
///     retried by design — the slot-taken, out-of-hours and past-time confirmations all call it again. Confirming
///     « créer quand même » after a collision therefore created a second patient. Fixed client-side (the created id
///     is remembered), which nothing in this project can assert — hence the second defect mattering as much.</item>
///   <item>The server had <b>no duplicate check on the hand-typed path at all</b>. The CSV import was the only door
///     that checked, and it is by far the least-used one. This file covers the guard that closes it.</item>
/// </list>
///
/// <para>A duplicate is the one mistake this product cannot undo: <c>Patient</c> has no merge and no soft delete, and
/// <c>DeletePatientCommand</c> refuses as soon as anything is attached — so the second file, its appointments and its
/// money are permanent. That asymmetry is why the matching is eager and why the refusal is advisory rather than
/// silent-and-safe: see <see cref="PatientDuplicateIndex"/>.</para>
/// </summary>
public class PatientDuplicateGuardTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Auth0Sub = "auth0|user-1";

    private static readonly DateTime Dob = new(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public PatientDuplicateGuardTests()
    {
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, ClinicId, "secretary", "staff@clinic.tn", "Staff"));
        _patients.Setup(r => r.AddAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient p, CancellationToken _) => p);
        _patients.Setup(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        OnFile();
    }

    // ------------------------------------------------------------------ the guard

    // The strongest signal: same name, same date of birth. The patient form requires a birthdate, so this is the
    // shape a duplicate takes when it is typed into « Ajouter un patient ».
    [Fact]
    public async Task Same_Name_And_Birth_Date_Is_Refused()
    {
        OnFile(Identity("Sonia", "Bel Hadj", Dob));

        var result = await CreateAsync(Command("Sonia", "Bel Hadj", Dob));

        Assert.True(result.IsFailure);
        Assert.Equal(PatientDuplicateIndex.RefusalCode, result.Code);
        // The refusal has to name who was matched — « ce patient existe déjà » with no name is unactionable when
        // reception is looking at a queue of walk-ins.
        Assert.Contains("Sonia Bel Hadj", result.Error!);
    }

    // The appointment dialog's quick-add form collects a name and a phone and nothing else, so this — not the case
    // above — is the shape the reported defect actually had. `DateOfBirth` arrives as `default`, which must be read
    // as « not supplied » rather than compared as a real date.
    [Fact]
    public async Task Same_Name_With_No_Birth_Date_Supplied_Is_Refused()
    {
        OnFile(Identity("Sonia", "Bel Hadj", Dob));

        var result = await CreateAsync(Command("Sonia", "Bel Hadj", dateOfBirth: default));

        Assert.True(result.IsFailure);
        Assert.Equal(PatientDuplicateIndex.RefusalCode, result.Code);
    }

    // ⚠️ The refusal must land BEFORE the write, not as a post-hoc complaint. A guard that refuses after
    // `AddAsync` + `SaveChangesAsync` would report the duplicate *and* create it — strictly worse than no guard,
    // because the user is told it failed.
    [Fact]
    public async Task A_Refusal_Writes_Nothing()
    {
        OnFile(Identity("Sonia", "Bel Hadj", Dob));

        var result = await CreateAsync(Command("Sonia", "Bel Hadj", Dob));

        Assert.True(result.IsFailure);
        _patients.Verify(r => r.AddAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A stored number typed differently is the same number. The hand-typed write path stores it as typed
    // (`PhoneNumber`'s ctor only trims), so the clinic really does hold « 20 123 456 » while the form sends
    // « +216 20 12 34 56 » — matching on the raw strings would see two different patients.
    [Fact]
    public async Task Same_Phone_Written_Differently_Is_Refused()
    {
        OnFile(Identity("Sonia", "Bel Hadj", Dob, phoneNumber: "20 123 456"));

        // A different person's name, so only the phone can match.
        var command = Command("Karim", "Trabelsi", new DateTime(1975, 1, 9, 0, 0, 0, DateTimeKind.Utc));
        command.PhoneNumber = "+216 20 12 34 56";

        var result = await CreateAsync(command);

        Assert.True(result.IsFailure);
        Assert.Equal(PatientDuplicateIndex.RefusalCode, result.Code);
    }

    // Case and accents do not make a second person. Folded through `SearchTerm.Normalize`, the same authority the
    // patient search uses — so the guard cannot disagree with the search box about who is on file.
    [Fact]
    public async Task Case_And_Accents_Do_Not_Defeat_The_Guard()
    {
        OnFile(Identity("Béchir", "Ben Salah", Dob));

        var result = await CreateAsync(Command("BECHIR", "BEN SALAH", Dob));

        Assert.True(result.IsFailure);
        Assert.Equal(PatientDuplicateIndex.RefusalCode, result.Code);
    }

    // ------------------------------------------------------------------ what must still be allowed

    // ⚠️ The whole point of a code + an override rather than a hard block. Two different people share a name far
    // too often in one governorate for « refuse » to be a defensible answer, and this dialog is also where an
    // emergency walk-in is registered with nothing but a name.
    [Fact]
    public async Task AllowDuplicate_Creates_The_Second_Record()
    {
        OnFile(Identity("Sonia", "Bel Hadj", Dob));

        var command = Command("Sonia", "Bel Hadj", Dob);
        command.AllowDuplicate = true;

        var result = await CreateAsync(command);

        Assert.True(result.IsSuccess);
        _patients.Verify(r => r.AddAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Same name, a birthdate that disagrees: two people, and neither the guard nor a human should hesitate.
    [Fact]
    public async Task A_Namesake_With_A_Different_Birth_Date_Is_Created()
    {
        OnFile(Identity("Sonia", "Bel Hadj", Dob));

        var result = await CreateAsync(
            Command("Sonia", "Bel Hadj", new DateTime(1974, 11, 30, 0, 0, 0, DateTimeKind.Utc)));

        Assert.True(result.IsSuccess);
    }

    // The clinic's first patient, and every genuinely new one after: an empty index refuses nothing.
    [Fact]
    public async Task An_Unknown_Patient_Is_Created()
    {
        var result = await CreateAsync(Command("Karim", "Trabelsi", Dob));

        Assert.True(result.IsSuccess);
        _patients.Verify(r => r.AddAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The check is scoped to the caller's clinic, resolved from the DB user record — another practice's patients
    // are not the reason this one cannot register somebody. (The projection read is clinic-bound; this pins that
    // the handler passes its own clinic and not, say, a claim.)
    [Fact]
    public async Task The_Index_Is_Read_For_The_Callers_Clinic()
    {
        await CreateAsync(Command("Karim", "Trabelsi", Dob));

        _patients.Verify(r => r.GetIdentitiesAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Skipping the check when the caller has already confirmed also skips the read — a clinic-wide projection is
    // not worth loading to answer a question whose answer is already « create it ».
    [Fact]
    public async Task AllowDuplicate_Skips_The_Read_Entirely()
    {
        var command = Command("Karim", "Trabelsi", Dob);
        command.AllowDuplicate = true;

        await CreateAsync(command);

        _patients.Verify(
            r => r.GetIdentitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------------- helpers

    private void OnFile(params PatientIdentity[] identities) =>
        _patients.Setup(r => r.GetIdentitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(identities);

    private static PatientIdentity Identity(
        string firstName,
        string lastName,
        DateTime dateOfBirth,
        string? phoneNumber = null) =>
        new(Guid.NewGuid(), firstName, lastName, dateOfBirth, phoneNumber);

    private static CreatePatientCommand Command(string firstName, string lastName, DateTime dateOfBirth) =>
        new()
        {
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            Gender = "F",
        };

    private async Task<Result<PatientDto>> CreateAsync(CreatePatientCommand command)
    {
        var handler = new CreatePatientCommandHandler(
            _patients.Object, _users.Object, _clinicContext.Object, _uow.Object);
        return await handler.Handle(command, CancellationToken.None);
    }
}
