using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Application.Features.Recall.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// [AC-46][AC-47][AC-48][AC-49][AC-50][AC-51] Patient contact details become genuinely optional.
///
/// <para>
/// The columns were <c>NOT NULL</c>, so two code paths manufactured placeholders to satisfy them —
/// <c>noemail@example.com</c> / <c>0000000000</c> from the create form, and
/// <c>unknown@example.com</c> / <c>000-000-0000</c> from the Google-Calendar patient auto-creator. Every
/// contact-less patient therefore shared an address that would silently absorb anything mailed to it, and
/// "we cannot reach this person" was indistinguishable from "we have their details" in every screen, export
/// and report.
/// </para>
/// </summary>
public class PatientContactOptionalTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Auth0UserClinicId = ClinicId;
    private const string Auth0Sub = "auth0|user-1";

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public PatientContactOptionalTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _users.Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, Auth0UserClinicId, "secretary", "staff@clinic.tn", "Staff"));
        _patients.Setup(r => r.AddAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient p, CancellationToken _) => p);
        _patients.Setup(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // The create handler now runs the duplicate guard first (see PatientDuplicateGuardTests). Left unstubbed,
        // Moq returns null for a Task<IReadOnlyList<T>> — the guard NREs, the handler's catch-all swallows it, and
        // every create test here fails on a generic « Error creating patient » that says nothing about contact
        // details. An empty clinic is the right default for this file: it has no opinion about duplicates.
        _patients.Setup(r => r.GetIdentitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientIdentity>());
    }

    private static Patient PatientWith(Email? email, PhoneNumber? phone, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), ClinicId, "Sonia", "Bel Hadj",
            new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), "F", email, phone);

    // ---------------------------------------------------------------- domain

    // [AC-46] The entity accepts a patient with neither. It used to throw ArgumentNullException on both.
    [Fact]
    public void A_Patient_Can_Exist_With_No_Contact_Details()
    {
        var patient = PatientWith(null, null);

        Assert.Null(patient.Email);
        Assert.Null(patient.PhoneNumber);
    }

    // [AC-49] UpdateContact sets and clears each field independently, and touches nothing else. Routing this
    // through UpdatePersonalInfo — six positional parameters — is what made a contact edit able to overwrite
    // a stale name or address it was never asked to change.
    [Fact]
    public void UpdateContact_Clears_One_Field_Without_Disturbing_Anything_Else()
    {
        var patient = PatientWith(new Email("sonia@example.tn"), new PhoneNumber("20123456"));

        patient.UpdateContact(null, patient.PhoneNumber);

        Assert.Null(patient.Email);
        Assert.Equal("20123456", patient.PhoneNumber?.Value);
        Assert.Equal("Sonia", patient.FirstName);
        Assert.Equal("Bel Hadj", patient.LastName);
    }

    // ---------------------------------------------------------------- create

    // [AC-46][AC-47] A blank field stays blank. No sentinel is written.
    [Fact]
    public async Task Create_With_Blank_Contact_Writes_No_Sentinel()
    {
        Patient? saved = null;
        _patients.Setup(r => r.AddAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Callback<Patient, CancellationToken>((p, _) => saved = p)
            .ReturnsAsync((Patient p, CancellationToken _) => p);

        var result = await CreateAsync(new CreatePatientCommand
        {
            FirstName = "Sonia", LastName = "Bel Hadj", Gender = "F",
            DateOfBirth = new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            Email = "   ", PhoneNumber = "",
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Null(saved!.Email);
        Assert.Null(saved.PhoneNumber);
        Assert.Null(result.Value!.Email);
        Assert.Null(result.Value.PhoneNumber);
    }

    // [AC-50] A non-blank number is still held to the Tunisian deliverability rule.
    [Fact]
    public async Task Create_Still_Rejects_A_Non_Deliverable_Phone()
    {
        var result = await CreateAsync(new CreatePatientCommand
        {
            FirstName = "Sonia", LastName = "Bel Hadj", Gender = "F",
            DateOfBirth = new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            PhoneNumber = "12",
        });

        Assert.True(result.IsFailure);
        Assert.Contains("téléphone", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- update (tri-state)

    // [AC-49] Explicit null clears. Under the old "blank ⇒ keep existing" reading there was no request that
    // could ever remove an e-mail once one had been saved.
    [Fact]
    public async Task Update_With_Explicit_Null_Clears_The_Field()
    {
        var patient = PatientWith(new Email("sonia@example.tn"), new PhoneNumber("20123456"));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand { Id = patient.Id, Email = null };
        Assert.True(command.EmailSpecified);          // the setter ran ⇒ the key was present
        Assert.False(command.PhoneNumberSpecified);

        var result = await UpdateAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Null(patient.Email);
        Assert.Equal("20123456", patient.PhoneNumber?.Value);   // untouched
    }

    // [AC-49] Omitting the key leaves the stored value alone — the other half of the tri-state.
    [Fact]
    public async Task Update_Without_The_Key_Keeps_The_Stored_Contact()
    {
        var patient = PatientWith(new Email("sonia@example.tn"), new PhoneNumber("20123456"));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var result = await UpdateAsync(new UpdatePatientCommand { Id = patient.Id, FirstName = "Sonya" });

        Assert.True(result.IsSuccess);
        Assert.Equal("sonia@example.tn", patient.Email?.Value);
        Assert.Equal("20123456", patient.PhoneNumber?.Value);
        Assert.Equal("Sonya", patient.FirstName);
    }

    // The date of birth carries the SAME tri-state, and it did not until the patient form made it optional.
    // `request.DateOfBirth ?? patient.DateOfBirth` made « effacer » and « laisser tel quel » one request, so a
    // birthday somebody had guessed at could never be removed and the form reported success having changed nothing.
    [Fact]
    public async Task Update_With_An_Explicit_Null_Clears_The_Date_Of_Birth()
    {
        var patient = PatientWith(new Email("sonia@example.tn"), new PhoneNumber("20123456"));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand { Id = patient.Id, DateOfBirth = null };
        Assert.True(command.DateOfBirthSpecified);    // the setter ran ⇒ the key was present

        var result = await UpdateAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Null(patient.DateOfBirth);
        Assert.Equal("Sonia", patient.FirstName);     // untouched
    }

    [Fact]
    public async Task Update_Without_The_Key_Keeps_The_Stored_Date_Of_Birth()
    {
        var patient = PatientWith(new Email("sonia@example.tn"), new PhoneNumber("20123456"));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand { Id = patient.Id, FirstName = "Sonya" };
        Assert.False(command.DateOfBirthSpecified);

        var result = await UpdateAsync(command);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), patient.DateOfBirth);
    }

    // ---------------------------------------------------------------- reads

    // [AC-48] The single defect that hurt most: an unguarded p.PhoneNumber.Value inside an in-memory Where over the
    // whole clinic, so ONE contact-less patient 500s the patient list and the header search.
    //
    // ⚠️ This asserts the *surviving* half of that AC. It used to assert the filtering too — that searching
    // "20123456" returned only the patient holding it — and `list-pagination` moved every free-text filter into SQL
    // (`IPatientRepository.GetByClinicIdAsync(searchTerm: …)`), because a filter applied after the page is cut
    // answers a different question ("the matches among these 25"). With the repository mocked, its rows come back
    // verbatim by definition, so the old `Assert.Single` had stopped testing the handler and started testing the
    // mock — it asserted the very in-memory `Where` whose removal was the point. What a mocked-repository test can
    // still prove is the two things that would actually reintroduce the 500: the term is handed to the database, and
    // a phone-less row maps without dereferencing a null.
    [Fact]
    public async Task Search_Survives_A_Patient_With_No_Phone()
    {
        var withPhone = PatientWith(null, new PhoneNumber("20123456"));
        var without = PatientWith(null, null);
        _patients.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<PatientListSort>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { without, withPhone }).AsPage());

        var handler = new GetPatientsQueryHandler(_patients.Object, _users.Object, _clinicContext.Object);
        var result = await handler.Handle(new GetPatientsQuery { SearchTerm = "20123456" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, p => p.Id == withPhone.Id && p.PhoneNumber == "20123456");
        Assert.Contains(result.Value.Items, p => p.Id == without.Id && p.PhoneNumber == null);

        // The search is the database's question to answer, over every row of the clinic — not the handler's over
        // one page of them.
        _patients.Verify(
            r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), "20123456",
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<PatientListSort>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-48] The DTO carries null, not "" and not a placeholder.
    [Fact]
    public void The_Dto_Carries_Null_For_A_Missing_Contact()
    {
        PatientDto dto = PatientWith(null, null).ToDto();

        Assert.Null(dto.Email);
        Assert.Null(dto.PhoneNumber);
    }

    // ---------------------------------------------------------------- recall

    // [AC-51] The recall used to enqueue nothing (the number was undeliverable), then mark the patient
    // contacted and snooze them 30 days regardless — so a phone-less patient silently vanished from the
    // relance list for a month and nobody was told to call them instead.
    [Fact]
    public async Task Recall_Refuses_For_A_Phone_Less_Patient_And_Does_Not_Snooze()
    {
        var patient = PatientWith(null, null);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var scheduler = new Mock<IReminderScheduler>();

        var handler = new SendRecallCommandHandler(
            _patients.Object, scheduler.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new SendRecallCommand { PatientId = patient.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(patient.RecallSnoozedUntil);
        Assert.Null(patient.LastRecallContactedAt);
        scheduler.Verify(
            s => s.ScheduleRecallAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-51] A reachable patient is unaffected: still sent, still stamped, still snoozed.
    [Fact]
    public async Task Recall_Still_Works_For_A_Reachable_Patient()
    {
        var patient = PatientWith(null, new PhoneNumber("20123456"));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        var scheduler = new Mock<IReminderScheduler>();
        // AC-P3.1 — the enqueue's outcome is now load-bearing, so a "recall works" test has to say that a
        // channel was configured. Left unstubbed, Moq's default outcome is not Enqueued and the handler
        // correctly refuses (which is the whole point of AC-P3.2).
        scheduler
            .Setup(s => s.ScheduleRecallAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecallDispatchOutcome.Enqueued);

        var handler = new SendRecallCommandHandler(
            _patients.Object, scheduler.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new SendRecallCommand { PatientId = patient.Id, Reason = "contrôle" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(patient.RecallSnoozedUntil);
        scheduler.Verify(
            s => s.ScheduleRecallAsync(
                ClinicId, patient.Id, It.IsAny<string>(), "contrôle", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Result<PatientDto>> CreateAsync(CreatePatientCommand command)
    {
        var handler = new CreatePatientCommandHandler(
            _patients.Object, _users.Object, _clinicContext.Object, _uow.Object);
        return await handler.Handle(command, CancellationToken.None);
    }

    private async Task<Result<PatientDto>> UpdateAsync(UpdatePatientCommand command)
    {
        var handler = new UpdatePatientCommandHandler(
            _patients.Object, _clinicResolver.Object, _uow.Object, new Mock<IClinicContext>().Object);
        return await handler.Handle(command, CancellationToken.None);
    }
}
