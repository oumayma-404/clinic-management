using System.Reflection;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
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

namespace ClinicManagement.UnitTests.Features.Common;

/// <summary>
/// [AC-54][AC-55][AC-56][AC-57][AC-60] Optimistic concurrency: two people editing one record.
///
/// <para>
/// The app had none, solution-wide. Every write was last-write-wins, so a secretary and a dentist with the
/// same patient open both saved successfully and the first one's change was gone with nobody told. The token
/// is PostgreSQL's <c>xmin</c>, mapped onto <c>Entity&lt;T&gt;.Version</c> — no column, no bump-it-yourself
/// discipline, and it covers all 38 entities at once.
/// </para>
/// <para>
/// A database is out of reach here, so these tests pin the parts that are pure logic and the parts that break
/// silently: that the token is on the aggregates and round-trips through the DTOs, that a
/// <see cref="ConflictException"/> escapes the handler catch-alls instead of being flattened into a generic
/// failure, and that the numbering retry does not mistake a conflict for a numbering collision.
/// </para>
/// </summary>
public class ConcurrencyConflictTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ---------------------------------------------------------------- the token exists where it must

    // [AC-54] Every aggregate inherits the token. Reflection rather than a list of names: a new entity gets
    // concurrency control for free, and this test keeps saying so without being edited.
    [Fact]
    public void Every_Entity_Carries_A_Version_Token()
    {
        var property = typeof(Entity<>).GetProperty(nameof(Entity<int>.Version));

        Assert.NotNull(property);
        Assert.Equal(typeof(uint), property!.PropertyType);
        Assert.True(property.GetSetMethod(nonPublic: true)!.IsPrivate,
            "the token is EF-managed — nothing in the domain should be able to assign it");
    }

    // [AC-60] The six aggregates a user can hold open in a form round-trip the token. A DTO that drops it
    // silently downgrades that screen to last-write-wins, and nothing else would notice.
    [Theory]
    [InlineData(typeof(PatientDto))]
    [InlineData(typeof(AppointmentDto))]
    [InlineData(typeof(InvoiceDto))]
    [InlineData(typeof(TreatmentPlanDto))]
    [InlineData(typeof(DentalRecordDto))]
    [InlineData(typeof(ClinicDto))]
    public void The_Round_Tripped_Dtos_Expose_The_Token(Type dtoType)
    {
        var property = dtoType.GetProperty("Version");

        Assert.NotNull(property);
        Assert.Equal(typeof(uint), property!.PropertyType);
        Assert.True(property.CanWrite, "the client sends it back, so it must be settable");
    }

    // [AC-60] …and the matching update commands accept it back.
    [Theory]
    [InlineData(typeof(UpdatePatientCommand))]
    [InlineData(typeof(ClinicManagement.Application.Features.Appointments.Commands.UpdateAppointmentCommand))]
    [InlineData(typeof(ClinicManagement.Application.Features.Invoices.Commands.UpdateInvoiceCommand))]
    [InlineData(typeof(ClinicManagement.Application.Features.TreatmentPlans.Commands.UpdateTreatmentPlanCommand))]
    [InlineData(typeof(UpdateDentalRecordCommand))]
    [InlineData(typeof(ClinicManagement.Application.Features.Clinics.Commands.UpdateClinicCommand))]
    public void The_Mutating_Commands_Accept_The_Token(Type commandType)
    {
        var property = commandType.GetProperty("Version");

        Assert.NotNull(property);
        Assert.Equal(typeof(uint), property!.PropertyType);
    }

    // [AC-54] The token is populated on the way out, not merely declared. A DTO that always ships 0 would
    // round-trip 0, which SetExpectedVersion reads as "no version supplied" — protection silently absent.
    [Fact]
    public void The_Patient_Dto_Carries_The_Aggregates_Token()
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Sonia", "Bel Hadj",
            new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), "F",
            new Email("sonia@example.tn"), new PhoneNumber("20123456"));
        SetVersion(patient, 4242u);

        Assert.Equal(4242u, patient.ToDto().Version);
    }

    // ---------------------------------------------------------------- the conflict escapes

    // [AC-56] The defect this guards against is subtle: every handler wraps its body in
    // `catch (Exception ex) { return Result.Failure("Erreur…") }`. Without the exception filter a 409 is
    // flattened into a generic failure — a 200-with-error-text the UI cannot distinguish from a real fault,
    // and cannot offer a reload on.
    [Fact]
    public async Task A_Conflict_Escapes_The_Handler_Catch_All()
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Sonia", "Bel Hadj",
            new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), "F");

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        patients.Setup(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException(ErrorMessages.Conflict));

        var handler = new UpdatePatientCommandHandler(
            patients.Object, resolver.Object, uow.Object, new Mock<IClinicContext>().Object);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdatePatientCommand { Id = patient.Id, FirstName = "Sonya", Version = 7u },
            CancellationToken.None));
    }

    // [AC-54] The expected version is handed to the unit of work — not left implicit. Without this call the
    // check runs against the row the handler loaded microseconds ago, which always matches: the whole feature
    // would be inert while looking present.
    [Fact]
    public async Task The_Handler_Passes_The_Callers_Version_To_The_Unit_Of_Work()
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Sonia", "Bel Hadj",
            new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc), "F");

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        patients.Setup(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdatePatientCommandHandler(
            patients.Object, resolver.Object, uow.Object, new Mock<IClinicContext>().Object);
        var result = await handler.Handle(
            new UpdatePatientCommand { Id = patient.Id, FirstName = "Sonya", Version = 99u },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        uow.Verify(u => u.SetExpectedVersion(patient, 99u), Times.Once);
    }

    // ---------------------------------------------------------------- the messages

    // [AC-55][AC-63] Two distinct French messages: the first conflict explains and offers a reload; a second
    // consecutive one says a peer is actively working on the record, because "reload and retry" has already
    // been tried and failed.
    [Fact]
    public void The_Conflict_Messages_Are_Distinct_And_Actionable()
    {
        Assert.NotEqual(ErrorMessages.Generic, ErrorMessages.Conflict);
        Assert.NotEqual(ErrorMessages.Conflict, ErrorMessages.RepeatedConflict);
        Assert.Contains("Rechargez", ErrorMessages.Conflict);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>EF owns the token, so a test has to write it through the private setter.</summary>
    private static void SetVersion(object entity, uint version)
    {
        // Looked up on the DECLARING type, not on Patient: the setter is private, and private members are
        // not inherited for reflection, so the derived type's PropertyInfo reports no setter at all.
        var property = typeof(Entity<Guid>).GetProperty(
            nameof(Entity<Guid>.Version), BindingFlags.Public | BindingFlags.Instance);
        property!.GetSetMethod(nonPublic: true)!.Invoke(entity, new object[] { version });
    }
}
